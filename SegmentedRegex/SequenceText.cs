namespace SegmentedRegex;

/// <summary>
/// Turns a <see cref="ReadOnlySequence{T}"/> of characters, or a range within one, into a <see cref="string"/>.
/// </summary>
internal static class SequenceText
{
    /// <summary>
    /// Returns the sequence length as an <see cref="int"/>, rejecting subjects that cannot be addressed
    /// by the flat <see cref="int"/> offsets the public API exposes.
    /// </summary>
    internal static int CheckedLength(in ReadOnlySequence<char> sequence, string paramName)
    {
        var length = sequence.Length;
        return length > int.MaxValue
            ? throw new ArgumentOutOfRangeException(paramName, $"Subjects longer than {int.MaxValue} characters are not supported.")
            : (int)length;
    }

    /// <summary>Materializes the whole <paramref name="sequence"/>.</summary>
    internal static string Materialize(in ReadOnlySequence<char> sequence)
    {
        var length = sequence.Length;
        if (length == 0)
        {
            return string.Empty;
        }
        if (length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), $"Subjects longer than {int.MaxValue} characters are not supported.");
        }

        if (sequence.IsSingleSegment)
        {
            return Materialize(sequence.First);
        }

        var total = (int)length;
        var buffer = ArrayPool<char>.Shared.Rent(total);
        try
        {
            var written = 0;
            foreach (var segment in sequence)
            {
                segment.Span.CopyTo(buffer.AsSpan(written));
                written += segment.Length;
            }
            return FromSpan(buffer.AsSpan(0, total));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>Materializes <paramref name="length"/> characters starting at <paramref name="start"/>.</summary>
    internal static string Materialize(in ReadOnlySequence<char> sequence, int start, int length) => length == 0 ? string.Empty : Materialize(sequence.Slice(start, length));

    private static string Materialize(ReadOnlyMemory<char> memory)
    {
        // A segment that spans an entire backing string hands that string back with no copy at all.
        return MemoryMarshal.TryGetString(memory, out var text, out var start, out var length) && start == 0 && length == text.Length
            ? text
            : FromSpan(memory.Span);
    }

    // new string(ReadOnlySpan<char>) does not exist on netstandard2.0; the char* overload does, on both TFMs.
    internal static unsafe string FromSpan(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return string.Empty;
        }
        fixed (char* p = span)
        {
            return new string(p, 0, span.Length);
        }
    }
}
