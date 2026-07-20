namespace SegmentedRegex;

/// <summary>
/// A forward-reading window over a <see cref="ReadOnlySequence{T}"/> of characters that mirrors the
/// <see cref="ReadOnlySpan{T}"/> surface the regex emitter uses, so retargeting the emitter is type
/// substitution rather than a rewrite.
/// </summary>
/// <remarks>
/// <para>
/// Holds the original sequence plus a window into it, never a re-sliced sequence, so
/// <see cref="Slice(int)"/> is a struct copy with adjusted bounds and carries the chunk cache over
/// intact rather than re-walking from the origin.
/// </para>
/// <para>
/// Search operations run the real vectorized <see cref="MemoryExtensions"/> primitive per chunk, so a
/// single-segment subject collapses to one call on one span. The cache is mutated as a side effect of
/// reading, which is why this is not a <see langword="readonly"/> ref struct.
/// </para>
/// </remarks>
public ref struct SegmentedSpan
{
    private readonly ReadOnlySequence<char> _sequence;
    private readonly int _absStart;
    private readonly int _length;

    // Cached chunk, described in absolute offsets into _sequence so slicing does not invalidate it.
    // Invariant: _chunkAbsStart + _chunk.Length is the absolute start of the chunk _next points at.
    private ReadOnlySpan<char> _chunk;
    private int _chunkAbsStart;
    private SequencePosition _next;

    /// <summary>Creates a reader over the whole of <paramref name="sequence"/>.</summary>
    /// <param name="sequence">The subject to read.</param>
    public SegmentedSpan(scoped in ReadOnlySequence<char> sequence)
    {
        _sequence = sequence;
        _absStart = 0;
        _length = SequenceText.CheckedLength(sequence, nameof(sequence));
        _chunk = default;
        _chunkAbsStart = 0;
        _next = sequence.Start;
    }

    // scoped: the ref must not be captured, or slicing off a field would look like a ref escape.
    private SegmentedSpan(scoped in ReadOnlySequence<char> sequence, int absStart, int length, ReadOnlySpan<char> chunk, int chunkAbsStart, SequencePosition next)
    {
        _sequence = sequence;
        _absStart = absStart;
        _length = length;
        _chunk = chunk;
        _chunkAbsStart = chunkAbsStart;
        _next = next;
    }

    /// <summary>The number of characters in this window.</summary>
    public readonly int Length => _length;

    /// <summary>Whether this window is empty.</summary>
    public readonly bool IsEmpty => _length == 0;

    /// <summary>Gets the character at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based offset into this window.</param>
    /// <returns>The character at that offset.</returns>
    public char this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var abs = _absStart + index;
            var local = abs - _chunkAbsStart;
            return (uint)local < (uint)_chunk.Length ? _chunk[local] : SeekAndRead(abs);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private char SeekAndRead(int abs)
    {
        var chunk = ChunkContaining(abs, out var chunkAbsStart);
        return chunk[abs - chunkAbsStart];
    }

    /// <summary>Forms a window starting at <paramref name="start"/> and running to the end.</summary>
    /// <param name="start">The zero-based offset to start at.</param>
    /// <returns>The narrowed window.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly SegmentedSpan Slice(int start)
    {
        if ((uint)start > (uint)_length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        return new SegmentedSpan(_sequence, _absStart + start, _length - start, _chunk, _chunkAbsStart, _next);
    }

    /// <summary>Forms a window of <paramref name="length"/> characters starting at <paramref name="start"/>.</summary>
    /// <param name="start">The zero-based offset to start at.</param>
    /// <param name="length">The number of characters to include.</param>
    /// <returns>The narrowed window.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly SegmentedSpan Slice(int start, int length)
    {
        if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        return new SegmentedSpan(_sequence, _absStart + start, length, _chunk, _chunkAbsStart, _next);
    }

    /// <summary>
    /// Exposes the whole window as one span when it happens to live inside a single chunk, which is
    /// always the case for a contiguous subject.
    /// </summary>
    /// <param name="span">The contiguous window, when there is one.</param>
    /// <returns><see langword="true"/> if the window is contiguous.</returns>
    public bool TryGetContiguous(out ReadOnlySpan<char> span)
    {
        if (_length == 0)
        {
            span = default;
            return true;
        }

        var chunk = ChunkContaining(_absStart, out var chunkAbsStart);
        var local = _absStart - chunkAbsStart;
        if (chunk.Length - local >= _length)
        {
            span = chunk.Slice(local, _length);
            return true;
        }

        span = default;
        return false;
    }

    /// <summary>Copies this window into <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer to copy into. Must be at least <see cref="Length"/> long.</param>
    public void CopyTo(scoped Span<char> destination)
    {
        if (destination.Length < _length)
        {
            throw new ArgumentException("Destination is too short.", nameof(destination));
        }

        var i = 0;
        while (i < _length)
        {
            var span = ChunkFrom(i, out var consumed);
            span.CopyTo(destination.Slice(i));
            i = consumed;
        }
    }

    /// <inheritdoc/>
    public override readonly string ToString() => SequenceText.Materialize(_sequence, _absStart, _length);

    /// <summary>Returns the offset of the first occurrence of <paramref name="value"/>, or -1.</summary>
    /// <param name="value">The character to find.</param>
    /// <returns>The zero-based offset, or -1.</returns>
    public int IndexOf(char value)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOf(value);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <summary>Whether <paramref name="value"/> occurs in this window.</summary>
    /// <param name="value">The character to look for.</param>
    /// <returns><see langword="true"/> if it occurs.</returns>
    public bool Contains(char value) => IndexOf(value) >= 0;

    /// <summary>Returns the offset of the first occurrence of <paramref name="value"/>, or -1.</summary>
    /// <param name="value">The characters to find.</param>
    /// <returns>The zero-based offset, or -1.</returns>
    /// <remarks>
    /// Candidates lying wholly inside one chunk are found by the vectorized span search. Only the at most
    /// <c>value.Length - 1</c> candidates per boundary that straddle two chunks are stitched by hand, and
    /// those always start after every candidate the chunk search can see, so first-match order holds.
    /// </remarks>
    public int IndexOf(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return 0;
        }
        if (value.Length > _length)
        {
            return -1;
        }

        var limit = _length - value.Length;
        var i = 0;
        while (i <= limit)
        {
            var chunk = ChunkFrom(i, out var consumed);

            var found = chunk.IndexOf(value);
            if (found >= 0)
            {
                return i + found;
            }

            var straddleFrom = Math.Max(i, consumed - value.Length + 1);
            var straddleTo = Math.Min(limit, consumed - 1);
            for (var start = straddleFrom; start <= straddleTo; start++)
            {
                if (Slice(start).StartsWith(value))
                {
                    return start;
                }
            }

            i = consumed;
        }
        return -1;
    }

    /// <summary>Returns the offset of the first character equal to any of the arguments, or -1.</summary>
    /// <param name="value0">The first character to find.</param>
    /// <param name="value1">The second character to find.</param>
    /// <returns>The zero-based offset, or -1.</returns>
    public int IndexOfAny(char value0, char value1)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAny(value0, value1);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAny(char, char)"/>
    /// <param name="value0">The first character to find.</param>
    /// <param name="value1">The second character to find.</param>
    /// <param name="value2">The third character to find.</param>
    public int IndexOfAny(char value0, char value1, char value2)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAny(value0, value1, value2);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAny(char, char)"/>
    /// <param name="values">The characters to find.</param>
    public int IndexOfAny(ReadOnlySpan<char> values)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAny(values);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAny(char, char)"/>
    /// <param name="values">The characters to find.</param>
    public int IndexOfAny(SearchValues<char> values)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAny(values);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <summary>Returns the offset of the earliest position where any string in <paramref name="values"/> matches, or -1.</summary>
    /// <param name="values">The strings to search for, with their comparison (ordinal or ordinal-ignore-case) baked in.</param>
    /// <param name="maxLength">The length of the longest string in <paramref name="values"/>, which bounds the boundary stitching.</param>
    /// <returns>The zero-based offset of the earliest match, or -1.</returns>
    /// <remarks>
    /// The multi-string analogue of <see cref="IndexOf(ReadOnlySpan{char})"/>. Per-chunk vectorized search finds
    /// candidates lying wholly inside a chunk; a bounded window rebuilt contiguously at each boundary and searched with
    /// the same <paramref name="values"/> catches the straddlers, so the comparison stays correct without ever needing
    /// the needle strings out here. Unlike the single-string case, the earliest wholly-internal hit cannot short-circuit:
    /// a longer string straddling a boundary can start before a shorter one's in-chunk hit, so both are taken and the
    /// earlier wins.
    /// </remarks>
    public int IndexOfAny(SearchValues<string> values, int maxLength)
    {
        if (TryGetContiguous(out var contiguous))
        {
            return contiguous.IndexOfAny(values);
        }
        if (maxLength <= 0)
        {
            return -1;
        }

        Span<char> scratch = stackalloc char[256];
        var i = 0;
        while (i < _length)
        {
            var chunk = ChunkFrom(i, out var consumed);

            var internalHit = chunk.IndexOfAny(values);
            var earliest = internalHit >= 0 ? i + internalHit : int.MaxValue;

            // Straddlers start in [consumed - maxLength + 1, consumed - 1] and finish past the boundary. Rebuild that
            // region plus the maxLength-1 chars a straddler can reach into, contiguously, and search it with `values`.
            var windowStart = Math.Max(i, consumed - maxLength + 1);
            if (windowStart < consumed && windowStart < earliest)
            {
                var windowLength = Math.Min(_length - windowStart, (consumed - windowStart) + maxLength - 1);
                char[] rented = null;
                var window = (windowLength <= scratch.Length ? scratch : rented = ArrayPool<char>.Shared.Rent(windowLength)).Slice(0, windowLength);
                Slice(windowStart, windowLength).CopyTo(window);

                var windowHit = ((ReadOnlySpan<char>)window).IndexOfAny(values);
                if (rented is not null)
                {
                    ArrayPool<char>.Shared.Return(rented);
                }

                // IndexOfAny gives the earliest in the window; if that start is before the boundary it is the earliest
                // straddler, otherwise no match starts before the boundary at all.
                if (windowHit >= 0 && windowStart + windowHit < consumed && windowStart + windowHit < earliest)
                {
                    earliest = windowStart + windowHit;
                }
            }

            if (earliest != int.MaxValue)
            {
                return earliest;
            }
            i = consumed;
        }
        return -1;
    }

    /// <summary>Returns the offset of the first character not equal to <paramref name="value"/>, or -1.</summary>
    /// <param name="value">The character to skip.</param>
    /// <returns>The zero-based offset, or -1.</returns>
    public int IndexOfAnyExcept(char value)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyExcept(value);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAnyExcept(char)"/>
    /// <param name="value0">The first character to skip.</param>
    /// <param name="value1">The second character to skip.</param>
    public int IndexOfAnyExcept(char value0, char value1)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyExcept(value0, value1);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAnyExcept(char)"/>
    /// <param name="value0">The first character to skip.</param>
    /// <param name="value1">The second character to skip.</param>
    /// <param name="value2">The third character to skip.</param>
    public int IndexOfAnyExcept(char value0, char value1, char value2)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyExcept(value0, value1, value2);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAnyExcept(char)"/>
    /// <param name="values">The characters to skip.</param>
    public int IndexOfAnyExcept(ReadOnlySpan<char> values)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyExcept(values);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <inheritdoc cref="IndexOfAnyExcept(char)"/>
    /// <param name="values">The characters to skip.</param>
    public int IndexOfAnyExcept(SearchValues<char> values)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyExcept(values);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <summary>Returns the offset of the first character within the inclusive range, or -1.</summary>
    /// <param name="lowInclusive">The lowest character in range.</param>
    /// <param name="highInclusive">The highest character in range.</param>
    /// <returns>The zero-based offset, or -1.</returns>
    public int IndexOfAnyInRange(char lowInclusive, char highInclusive)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyInRange(lowInclusive, highInclusive);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <summary>Returns the offset of the first character outside the inclusive range, or -1.</summary>
    /// <param name="lowInclusive">The lowest character in range.</param>
    /// <param name="highInclusive">The highest character in range.</param>
    /// <returns>The zero-based offset, or -1.</returns>
    public int IndexOfAnyExceptInRange(char lowInclusive, char highInclusive)
    {
        var i = 0;
        while (i < _length)
        {
            var found = ChunkFrom(i, out var consumed).IndexOfAnyExceptInRange(lowInclusive, highInclusive);
            if (found >= 0)
            {
                return i + found;
            }
            i = consumed;
        }
        return -1;
    }

    /// <summary>Whether this window begins with <paramref name="value"/>.</summary>
    /// <param name="value">The prefix to test for.</param>
    /// <returns><see langword="true"/> if it does.</returns>
    public bool StartsWith(ReadOnlySpan<char> value)
    {
        if (value.Length > _length)
        {
            return false;
        }

        var i = 0;
        while (i < value.Length)
        {
            var span = ChunkFrom(i, out _);
            var take = Math.Min(span.Length, value.Length - i);
            if (!span.Slice(0, take).SequenceEqual(value.Slice(i, take)))
            {
                return false;
            }
            i += take;
        }
        return true;
    }

    /// <summary>Whether this window begins with <paramref name="value"/> under the given comparison.</summary>
    /// <param name="value">The prefix to test for.</param>
    /// <param name="comparisonType">
    /// <see cref="StringComparison.Ordinal"/> or <see cref="StringComparison.OrdinalIgnoreCase"/>. Culture-sensitive
    /// comparisons cannot be evaluated chunk-by-chunk and are rejected; the generated matcher only ever emits ordinal ones.
    /// </param>
    /// <returns><see langword="true"/> if it does.</returns>
    public bool StartsWith(ReadOnlySpan<char> value, StringComparison comparisonType)
    {
        if (comparisonType == StringComparison.Ordinal)
        {
            return StartsWith(value);
        }
        if (comparisonType != StringComparison.OrdinalIgnoreCase)
        {
            throw new ArgumentOutOfRangeException(nameof(comparisonType), "Only ordinal comparisons are supported.");
        }

        if (value.Length > _length)
        {
            return false;
        }

        var i = 0;
        while (i < value.Length)
        {
            var span = ChunkFrom(i, out _);
            var take = Math.Min(span.Length, value.Length - i);
            if (!span.Slice(0, take).Equals(value.Slice(i, take), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            i += take;
        }
        return true;
    }

    /// <summary>Returns the offset of the first occurrence of <paramref name="value"/> under the given comparison, or -1.</summary>
    /// <param name="value">The characters to find.</param>
    /// <param name="comparisonType">
    /// <see cref="StringComparison.Ordinal"/> or <see cref="StringComparison.OrdinalIgnoreCase"/>. Culture-sensitive
    /// comparisons cannot be evaluated chunk-by-chunk and are rejected; the generated matcher only ever emits ordinal ones.
    /// </param>
    /// <returns>The zero-based offset, or -1.</returns>
    /// <remarks>Same per-chunk search plus boundary stitching as the ordinal overload; see <see cref="IndexOf(ReadOnlySpan{char})"/>.</remarks>
    public int IndexOf(ReadOnlySpan<char> value, StringComparison comparisonType)
    {
        if (comparisonType == StringComparison.Ordinal)
        {
            return IndexOf(value);
        }
        if (comparisonType != StringComparison.OrdinalIgnoreCase)
        {
            throw new ArgumentOutOfRangeException(nameof(comparisonType), "Only ordinal comparisons are supported.");
        }

        if (value.IsEmpty)
        {
            return 0;
        }
        if (value.Length > _length)
        {
            return -1;
        }

        var limit = _length - value.Length;
        var i = 0;
        while (i <= limit)
        {
            var chunk = ChunkFrom(i, out var consumed);

            var found = chunk.IndexOf(value, StringComparison.OrdinalIgnoreCase);
            if (found >= 0)
            {
                return i + found;
            }

            var straddleFrom = Math.Max(i, consumed - value.Length + 1);
            var straddleTo = Math.Min(limit, consumed - 1);
            for (var start = straddleFrom; start <= straddleTo; start++)
            {
                if (Slice(start).StartsWith(value, StringComparison.OrdinalIgnoreCase))
                {
                    return start;
                }
            }

            i = consumed;
        }
        return -1;
    }

    /// <summary>Whether this window equals <paramref name="other"/> character for character.</summary>
    /// <param name="other">The span to compare against.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public bool SequenceEqual(ReadOnlySpan<char> other) => other.Length == _length && StartsWith(other);

    /// <summary>Whether this window equals <paramref name="other"/> character for character.</summary>
    /// <param name="other">The window to compare against. May cover a different range of the same or another sequence.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    /// <remarks>The emitted backreference check compares two windows of the same subject, hence this overload.</remarks>
    public bool SequenceEqual(SegmentedSpan other)
    {
        if (other._length != _length)
        {
            return false;
        }

        // Lockstep walk: each iteration compares the longest run that is contiguous in both windows,
        // so two identically-chunked windows collapse to full-chunk vectorized compares.
        var i = 0;
        while (i < _length)
        {
            var mine = ChunkFrom(i, out _);
            var theirs = other.ChunkFrom(i, out _);
            var take = Math.Min(mine.Length, theirs.Length);
            if (!mine.Slice(0, take).SequenceEqual(theirs.Slice(0, take)))
            {
                return false;
            }
            i += take;
        }
        return true;
    }

    /// <summary>
    /// Returns the run of the window starting at <paramref name="i"/> that lives in one chunk, and the
    /// window offset just past it. The single-chunk case returns everything in one go.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> ChunkFrom(int i, out int consumed)
    {
        var abs = _absStart + i;
        var chunk = ChunkContaining(abs, out var chunkAbsStart);
        var local = abs - chunkAbsStart;

        var available = chunk.Length - local;
        var remaining = _length - i;
        var take = available < remaining ? available : remaining;

        consumed = i + take;
        return chunk.Slice(local, take);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> ChunkContaining(int abs, out int chunkAbsStart)
    {
        var local = abs - _chunkAbsStart;
        if ((uint)local < (uint)_chunk.Length)
        {
            chunkAbsStart = _chunkAbsStart;
            return _chunk;
        }
        return SeekTo(abs, out chunkAbsStart);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ReadOnlySpan<char> SeekTo(int abs, out int chunkAbsStart)
    {
        // Only a backwards seek needs to start over; forward scanning resumes where the cache left off,
        // which is what makes a left-to-right match amortized linear rather than quadratic.
        if (abs < _chunkAbsStart)
        {
            _next = _sequence.Start;
            _chunkAbsStart = 0;
            _chunk = default;
        }

        while (_sequence.TryGet(ref _next, out var memory, advance: true))
        {
            var start = _chunkAbsStart + _chunk.Length;
            _chunkAbsStart = start;
            _chunk = memory.Span;

            // Empty segments leave start where it was, so the loop walks past them on its own.
            if (abs < start + memory.Length)
            {
                chunkAbsStart = start;
                return _chunk;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(abs));
    }
}
