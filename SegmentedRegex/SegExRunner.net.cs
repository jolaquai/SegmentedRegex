namespace SegmentedRegex;

/// <summary>
/// Base for generated matchers. Owns the capture and backtracking state the emitted body manipulates,
/// standing in for <see cref="RegexRunner"/>, which cannot be subclassed here because its input path
/// (<c>Scan(ReadOnlySpan&lt;char&gt;)</c>) is contiguous-only.
/// </summary>
/// <remarks>
/// <para>
/// The logic is ported from <c>RegexRunner.cs</c> and <c>Match.cs</c> at the pinned upstream SHA. The
/// BCL splits this in two - the crawl stack on <see cref="RegexRunner"/>, the capture arrays on
/// <c>Match</c>, reached through <c>runmatch</c> - and the runner forwards to the match object purely
/// to get around visibility. With no <c>Match</c> to defer to, both halves live here, which removes
/// the forwarding rather than changing any of the logic.
/// </para>
/// <para>
/// All of it is plain int-array bookkeeping and does not touch the input, which is why it ports across
/// to a segmented subject unchanged.
/// </para>
/// </remarks>
public abstract class SegExRunner
{
    // CA1051: these are fields rather than properties on purpose, exactly as RegexRunner declares
    // them. The emitted push helpers take 'ref base.runstack' so they can reallocate it, and you
    // cannot take a ref to a property. Lowercase names match upstream so the emitter retarget stays a
    // type substitution.
#pragma warning disable CA1051

    /// <summary>The position the current match attempt starts from.</summary>
    protected internal int runtextstart;

    /// <summary>The current position in the subject.</summary>
    protected internal int runtextpos;

    /// <summary>
    /// Scratch storage. The emitted code uses it to hand a position from
    /// <c>TryFindNextPossibleStartingPosition</c> to <c>TryMatchAtCurrentPosition</c> for the
    /// literal-after-loop optimization; upstream describes the field it borrows for this as "an extra,
    /// unused field on RegexRunner". It is not a track stack.
    /// </summary>
    protected internal int runtrackpos;

    /// <summary>
    /// Backing array for the emitted backtracking stack. The emitted push helpers take this by
    /// reference and grow it themselves, and the emitted body keeps its own local stack position, so
    /// there is deliberately no position field here to go with it.
    /// </summary>
    protected internal int[] runstack;

#pragma warning restore CA1051

    // Capture arrays, per group: _matches[cap] holds (offset, length) pairs and _matchcount[cap] says
    // how many pairs are live. Negative offsets are balancing back-references, resolved on read and
    // compacted away by TidyBalancing.
    private int[][] _matches;
    private int[] _matchcount;
    private bool _balancing;

    // The crawl stack records which groups were captured, so backtracking can undo them in order.
    // It fills downwards from the end, so Crawlpos is a height rather than an index.
    private int[] _runcrawl;
    private int _runcrawlpos;

    private bool _checkTimeout;
    private long _timeoutOccursAt;
    private TimeSpan _timeout;
    private string _pattern;

    /// <summary>Runs the match at <see cref="runtextpos"/>, capturing as it goes.</summary>
    /// <param name="inputSpan">The subject to match against.</param>
    protected internal abstract void Scan(SegmentedSpan inputSpan);

    /// <summary>Whether the last scan captured group 0, which is what makes a match successful.</summary>
    protected internal bool FoundMatch => _matchcount[0] > 0;

    /// <summary>Prepares the runner for a scan over a subject of <paramref name="textLength"/> characters.</summary>
    /// <param name="capCount">The number of capturing groups, including group 0.</param>
    /// <param name="textLength">The subject length.</param>
    /// <param name="textStart">The position to start matching at.</param>
    /// <param name="timeout">The per-match timeout, or <see cref="Regex.InfiniteMatchTimeout"/>.</param>
    /// <param name="pattern">The pattern, used only to describe a timeout.</param>
    protected internal void InitializeForScan(int capCount, int textLength, int textStart, TimeSpan timeout, string pattern)
    {
        runtextstart = textStart;
        runtextpos = textStart;
        runtrackpos = 0;
        _pattern = pattern;

        _timeout = timeout;
        _checkTimeout = timeout != Regex.InfiniteMatchTimeout;
        if (_checkTimeout)
        {
            _timeoutOccursAt = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        }

        if (_matchcount is null || _matchcount.Length != capCount)
        {
            _matchcount = new int[capCount];
            _matches = new int[capCount][];
        }
        else
        {
            Array.Clear(_matchcount, 0, _matchcount.Length);
        }
        _balancing = false;

        // Sized as upstream sizes them for a runner with no track-count estimate to go on.
        runstack ??= new int[16];
        if (_runcrawl is null)
        {
            _runcrawl = new int[32];
        }
        _runcrawlpos = _runcrawl.Length;

        _ = textLength;
    }

    /// <summary>Throws if the match has run past its timeout.</summary>
    /// <exception cref="RegexMatchTimeoutException">The timeout elapsed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected internal void CheckTimeout()
    {
        if (_checkTimeout && Environment.TickCount64 >= _timeoutOccursAt)
        {
            ThrowRegexTimeout();
        }

        // The subject is not materialized for this: it can be arbitrarily large and this is the error
        // path, so the exception reports an empty input rather than copying the whole sequence.
        void ThrowRegexTimeout() => throw new RegexMatchTimeoutException(string.Empty, _pattern ?? string.Empty, _timeout);
    }

    /// <summary>Records a capture of group <paramref name="capnum"/> spanning the given positions.</summary>
    /// <param name="capnum">The group number.</param>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    protected void Capture(int capnum, int start, int end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        Crawl(capnum);
        AddMatch(capnum, start, end - start);
    }

    /// <summary>Implements the balancing group construct, transferring one group's capture to another.</summary>
    /// <param name="capnum">The group receiving the capture, or -1 for none.</param>
    /// <param name="uncapnum">The group being balanced away.</param>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    protected void TransferCapture(int capnum, int uncapnum, int start, int end)
    {
        // these are the two intervals that are canceling each other
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var start2 = MatchIndex(uncapnum);
        var end2 = start2 + MatchLength(uncapnum);

        // The new capture gets the innermost defined interval
        if (start >= end2)
        {
            end = start;
            start = end2;
        }
        else if (end <= start2)
        {
            start = start2;

            // Ensure we don't create a capture with negative length
            // When the balancing capture precedes the balanced group, end might be less than the new start
            if (end < start)
            {
                end = start;
            }
        }
        else
        {
            if (end > end2)
            {
                end = end2;
            }
            if (start2 > start)
            {
                start = start2;
            }
        }

        Crawl(uncapnum);
        BalanceMatch(uncapnum);

        if (capnum != -1)
        {
            Crawl(capnum);
            AddMatch(capnum, start, end - start);
        }
    }

    /// <summary>Reverts the most recent capture.</summary>
    protected void Uncapture()
    {
        var capnum = Popcrawl();
        _matchcount[capnum]--;
    }

    /// <summary>The height of the crawl stack, used as a mark to backtrack captures down to.</summary>
    /// <returns>The number of captures recorded so far.</returns>
    protected int Crawlpos() => _runcrawl.Length - _runcrawlpos;

    /// <summary>Whether group <paramref name="cap"/> currently has a capture.</summary>
    /// <param name="cap">The group number.</param>
    /// <returns><see langword="true"/> if it participated.</returns>
    protected bool IsMatched(int cap) => (uint)cap < (uint)_matchcount.Length
        && _matchcount[cap] > 0
        && _matches[cap][_matchcount[cap] * 2 - 1] != -3 + 1;

    /// <summary>The start position of group <paramref name="cap"/>'s most recent capture.</summary>
    /// <param name="cap">The group number.</param>
    /// <returns>The start position.</returns>
    protected int MatchIndex(int cap)
    {
        var i = _matches[cap][_matchcount[cap] * 2 - 2];
        return i >= 0 ? i : _matches[cap][-3 - i];
    }

    /// <summary>The length of group <paramref name="cap"/>'s most recent capture.</summary>
    /// <param name="cap">The group number.</param>
    /// <returns>The length.</returns>
    protected int MatchLength(int cap)
    {
        var i = _matches[cap][_matchcount[cap] * 2 - 1];
        return i >= 0 ? i : _matches[cap][-3 - i];
    }

    /// <summary>
    /// Resolves the balancing back-references left behind by <see cref="TransferCapture"/> so the
    /// capture arrays can be read straight through. Must run before the captures are handed out.
    /// </summary>
    protected internal void Tidy()
    {
        if (!_balancing)
        {
            return;
        }

        // Compact the unbalanced captures: skip forward to the first balancing entry, then walk the
        // rest, dropping negatives and sliding real captures down into the freed slots.
        for (var cap = 0; cap < _matchcount.Length; cap++)
        {
            var limit = _matchcount[cap] * 2;
            var matcharray = _matches[cap];

            int i;
            for (i = 0; i < limit; i++)
            {
                if (matcharray[i] < 0)
                {
                    break;
                }
            }

            int j;
            for (j = i; i < limit; i++)
            {
                if (matcharray[i] < 0)
                {
                    j--;
                }
                else
                {
                    if (i != j)
                    {
                        matcharray[j] = matcharray[i];
                    }
                    j++;
                }
            }

            _matchcount[cap] = j / 2;
        }

        _balancing = false;
    }

    /// <summary>The number of captures group <paramref name="cap"/> made. Call <see cref="Tidy"/> first.</summary>
    /// <param name="cap">The group number.</param>
    /// <returns>The capture count.</returns>
    protected internal int CaptureCount(int cap) => _matchcount[cap];

    /// <summary>The start position of one of group <paramref name="cap"/>'s captures.</summary>
    /// <param name="cap">The group number.</param>
    /// <param name="capture">The zero-based capture index within the group.</param>
    /// <returns>The start position.</returns>
    protected internal int CaptureIndex(int cap, int capture) => _matches[cap][capture * 2];

    /// <summary>The length of one of group <paramref name="cap"/>'s captures.</summary>
    /// <param name="cap">The group number.</param>
    /// <param name="capture">The zero-based capture index within the group.</param>
    /// <returns>The length.</returns>
    protected internal int CaptureLength(int cap, int capture) => _matches[cap][(capture * 2) + 1];

    private void AddMatch(int cap, int start, int len)
    {
        _matches[cap] ??= new int[2];

        var capcount = _matchcount[cap];
        if ((capcount * 2) + 2 > _matches[cap].Length)
        {
            var newmatches = new int[capcount * 8];
            Array.Copy(_matches[cap], newmatches, capcount * 2);
            _matches[cap] = newmatches;
        }

        _matches[cap][capcount * 2] = start;
        _matches[cap][(capcount * 2) + 1] = len;
        _matchcount[cap] = capcount + 1;
    }

    private void BalanceMatch(int cap)
    {
        _balancing = true;

        // Look at the last capture first. If it is negative it is a reference to another slot, so
        // follow it before stepping back to the previous capture.
        var capcount = _matchcount[cap];
        var target = (capcount * 2) - 2;

        if (_matches[cap][target] < 0)
        {
            target = -3 - _matches[cap][target];
        }

        target -= 2;

        if (target >= 0 && _matches[cap][target] < 0)
        {
            AddMatch(cap, _matches[cap][target], _matches[cap][target + 1]);
        }
        else
        {
            AddMatch(cap, -3 - target, -4 - target /* == -3 - (target + 1) */ );
        }
    }

    private void Crawl(int i)
    {
        if (_runcrawlpos == 0)
        {
            DoubleCrawl();
        }

        _runcrawl[--_runcrawlpos] = i;
    }

    private int Popcrawl() => _runcrawl[_runcrawlpos++];

    private void DoubleCrawl()
    {
        var newcrawl = new int[_runcrawl.Length * 2];
        Array.Copy(_runcrawl, 0, newcrawl, _runcrawl.Length, _runcrawl.Length);
        _runcrawlpos += _runcrawl.Length;
        _runcrawl = newcrawl;
    }
}
