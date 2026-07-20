namespace SegmentedRegex.Tests;

/// <summary>
/// Builds <see cref="ReadOnlySequence{T}"/> subjects out of explicit segment lists, and enumerates the
/// segmentations every pattern gets tested against.
/// </summary>
internal sealed class Segmentation : ReadOnlySequenceSegment<char>
{
    private Segmentation(ReadOnlyMemory<char> memory) => Memory = memory;

    /// <summary>Chains <paramref name="parts"/> into one sequence, preserving empty parts as empty segments.</summary>
    internal static ReadOnlySequence<char> Build(params string[] parts)
    {
        if (parts.Length == 0)
        {
            return ReadOnlySequence<char>.Empty;
        }

        var first = new Segmentation(parts[0].AsMemory());
        var last = first;
        for (var i = 1; i < parts.Length; i++)
        {
            var next = new Segmentation(parts[i].AsMemory())
            {
                RunningIndex = last.RunningIndex + last.Memory.Length
            };
            last.Next = next;
            last = next;
        }
        return new ReadOnlySequence<char>(first, 0, last, last.Memory.Length);
    }

    /// <summary>
    /// Every segmentation of <paramref name="subject"/> worth testing: contiguous, every single and double
    /// split point, one segment per character, and variants padded with empty segments.
    /// </summary>
    internal static IEnumerable<(string Label, ReadOnlySequence<char> Sequence)> All(string subject)
    {
        var n = subject.Length;

        // The genuinely contiguous construction, which the reader is meant to collapse to plain span ops.
        yield return ("contiguous", new ReadOnlySequence<char>(subject.AsMemory()));
        yield return ("one-segment", Build(subject));

        for (var i = 0; i <= n; i++)
        {
            yield return ($"split@{i}", Build(subject[..i], subject[i..]));
        }

        // Quadratic, so only for subjects short enough that it stays cheap.
        if (n <= 12)
        {
            for (var i = 0; i <= n; i++)
            {
                for (var j = i; j <= n; j++)
                {
                    yield return ($"split@{i},{j}", Build(subject[..i], subject[i..j], subject[j..]));
                }
            }
        }

        if (n > 0)
        {
            var perChar = new string[n];
            for (var i = 0; i < n; i++)
            {
                perChar[i] = subject[i].ToString();
            }
            yield return ("per-char", Build(perChar));
        }

        // Empty segments are the classic thing a chunked reader gets wrong.
        for (var i = 0; i <= n; i++)
        {
            yield return ($"empties@{i}", Build("", subject[..i], "", subject[i..], ""));
        }
    }
}
