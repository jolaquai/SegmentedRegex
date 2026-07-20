namespace SegmentedRegex;

/// <summary>
/// Base for source-generated matchers. Owns the drive loop and result construction so the generator
/// only has to emit the pattern-specific matching body.
/// </summary>
/// <remarks>
/// <para>
/// The emitted <c>Scan</c> already contains its own find-and-match loop and returns once it has either
/// captured group 0 or exhausted the input, so all that is left here is to set the runner up, call it,
/// and turn the capture arrays into a <see cref="SegExMatch"/>.
/// </para>
/// <para>
/// Group identity arrives as two arrays indexed by dense capture slot, which is the slot the runner's
/// capture arrays use. The generator computes them; they are not the BCL's <c>Caps</c>/<c>CapNames</c>
/// hashtables, because the only thing needed here is a number and a name per slot.
/// </para>
/// </remarks>
public abstract class GeneratedSegEx : SegEx
{
    private readonly int[] _groupNumbers;
    private readonly string[] _groupNames;
    // Slots visited in ascending group-number order, so groups come out numerically ordered even for
    // a pattern that renumbers explicitly. Identity for the usual dense case.
    private readonly int[] _slotsByNumber;

    private SegExRunner _cachedRunner;

    /// <summary>Initializes the generated matcher.</summary>
    /// <param name="pattern">The pattern being matched.</param>
    /// <param name="options">The options it was built with.</param>
    /// <param name="matchTimeout">The per-match timeout, or <see cref="Regex.InfiniteMatchTimeout"/>.</param>
    /// <param name="groupNumbers">The group number at each capture slot.</param>
    /// <param name="groupNames">The group name at each capture slot.</param>
    protected GeneratedSegEx(string pattern, RegexOptions options, TimeSpan matchTimeout, int[] groupNumbers, string[] groupNames)
        : base(pattern, options, matchTimeout)
    {
        if (groupNumbers is null)
        {
            throw new ArgumentNullException(nameof(groupNumbers));
        }
        if (groupNames is null)
        {
            throw new ArgumentNullException(nameof(groupNames));
        }
        if (groupNumbers.Length != groupNames.Length || groupNumbers.Length == 0)
        {
            throw new ArgumentException("Group numbers and names must be parallel and non-empty.", nameof(groupNames));
        }

        _groupNumbers = groupNumbers;
        _groupNames = groupNames;

        var slots = new int[groupNumbers.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = i;
        }
        Array.Sort((int[])groupNumbers.Clone(), slots);
        _slotsByNumber = slots;
    }

    /// <summary>Creates a runner for this pattern.</summary>
    /// <returns>A fresh runner.</returns>
    protected abstract SegExRunner CreateRunner();

    /// <inheritdoc/>
    public sealed override bool IsMatch(in ReadOnlySequence<char> input)
    {
        var length = SequenceText.CheckedLength(input, nameof(input));
        var runner = RentRunner();
        try
        {
            return TryScan(runner, input, length, 0);
        }
        finally
        {
            ReturnRunner(runner);
        }
    }

    /// <inheritdoc/>
    public sealed override SegExMatch Match(in ReadOnlySequence<char> input)
    {
        var length = SequenceText.CheckedLength(input, nameof(input));
        var runner = RentRunner();
        try
        {
            return TryScan(runner, input, length, 0) ? BuildMatch(runner, input) : SegExMatch.Failed(input);
        }
        finally
        {
            ReturnRunner(runner);
        }
    }

    /// <inheritdoc/>
    public sealed override SegExMatchCollection Matches(in ReadOnlySequence<char> input)
    {
        var length = SequenceText.CheckedLength(input, nameof(input));
        List<SegExMatch> results = null;

        var runner = RentRunner();
        try
        {
            var startAt = 0;
            while (startAt <= length && TryScan(runner, input, length, startAt))
            {
                var match = BuildMatch(runner, input);
                (results ??= []).Add(match);

                // An empty match would otherwise be found forever at the same position, so it costs a
                // bump; a non-empty one resumes at its end. This is what makes "a*" report a match
                // between every character rather than looping.
                var end = match.Index + match.Length;
                startAt = match.Length == 0 ? end + 1 : end;
            }
        }
        finally
        {
            ReturnRunner(runner);
        }

        return results is null ? SegExMatchCollection.Empty : new SegExMatchCollection([.. results]);
    }

    private bool TryScan(SegExRunner runner, in ReadOnlySequence<char> input, int length, int startAt)
    {
        runner.InitializeForScan(_groupNumbers.Length, length, startAt, MatchTimeout, Pattern);
        runner.Scan(new SegmentedSpan(input));
        return runner.FoundMatch;
    }

    private SegExMatch BuildMatch(SegExRunner runner, in ReadOnlySequence<char> subject)
    {
        // Resolves the balancing back-references, so every remaining capture can be read straight out.
        runner.Tidy();

        var groups = new SegExGroup[_slotsByNumber.Length];
        SegExCapture[] matchCaptures = null;

        for (var i = 0; i < _slotsByNumber.Length; i++)
        {
            var slot = _slotsByNumber[i];
            var count = runner.CaptureCount(slot);

            SegExCapture[] captures;
            if (count == 0)
            {
                captures = [];
            }
            else
            {
                captures = new SegExCapture[count];
                for (var c = 0; c < count; c++)
                {
                    captures[c] = new SegExCapture(subject, runner.CaptureIndex(slot, c), runner.CaptureLength(slot, c));
                }
            }

            // A group reports its last capture, as System.Text.RegularExpressions does.
            var success = count > 0;
            var index = success ? runner.CaptureIndex(slot, count - 1) : 0;
            var captureLength = success ? runner.CaptureLength(slot, count - 1) : 0;

            groups[i] = new SegExGroup(subject, index, captureLength, success, _groupNumbers[slot], _groupNames[slot], captures);
            if (slot == 0)
            {
                matchCaptures = captures;
            }
        }

        var zero = groups[0];
        return new SegExMatch(subject, zero.Index, zero.Length, true, matchCaptures ?? [], groups);
    }

    // One runner is cached and handed out at a time, so concurrent callers each get their own rather
    // than sharing mutable capture state. Mirrors how Regex pools its runners.
    private SegExRunner RentRunner() => Interlocked.Exchange(ref _cachedRunner, null) ?? CreateRunner();

    private void ReturnRunner(SegExRunner runner) => Volatile.Write(ref _cachedRunner, runner);
}
