using SegmentedRegex;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// The patterns under measurement, in matched pairs: a BCL <see cref="Regex"/> from
/// <c>[GeneratedRegex]</c> and a <see cref="SegEx"/> from <c>[GeneratedSegEx]</c> for the same pattern,
/// so the two engines can be compared on identical work. Both generators run in this project.
/// </summary>
internal static partial class Corpus
{
    // A leading literal, the bread-and-butter case: drives IndexOf(string) and its boundary stitching.
    [GeneratedRegex("needle")] internal static partial Regex BclLiteral();
    [GeneratedSegEx("needle")] internal static partial SegEx SegLiteral();

    // A character-class loop: single-char search ops, which never need stitching.
    [GeneratedRegex(@"\d+")] internal static partial Regex BclDigits();
    [GeneratedSegEx(@"\d+")] internal static partial SegEx SegDigits();

    // Multi-string alternation: the SearchValues<string> path and its window rebuild at boundaries.
    [GeneratedRegex("cat|dog|bird")] internal static partial Regex BclAlternation();
    [GeneratedSegEx("cat|dog|bird")] internal static partial SegEx SegAlternation();

    // Captures plus a fixed literal, closer to a real extraction pattern.
    [GeneratedRegex(@"(\w+)@(\w+)\.com")] internal static partial Regex BclEmail();
    [GeneratedSegEx(@"(\w+)@(\w+)\.com")] internal static partial SegEx SegEmail();

    // A backreference: compares two windows of the same subject via SequenceEqual.
    [GeneratedRegex(@"(\w)\1")] internal static partial Regex BclBackreference();
    [GeneratedSegEx(@"(\w)\1")] internal static partial SegEx SegBackreference();

    internal static Regex Bcl(string kind) => kind switch
    {
        "Literal" => BclLiteral(),
        "Digits" => BclDigits(),
        "Alternation" => BclAlternation(),
        "Email" => BclEmail(),
        "Backreference" => BclBackreference(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static SegEx Seg(string kind) => kind switch
    {
        "Literal" => SegLiteral(),
        "Digits" => SegDigits(),
        "Alternation" => SegAlternation(),
        "Email" => SegEmail(),
        "Backreference" => SegBackreference(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Deterministic filler text with a single match placed near the end, so the engine has to scan
    /// most of the subject rather than succeeding immediately.
    /// </summary>
    internal static string Subject(string kind, int length)
    {
        var match = kind switch
        {
            "Literal" => "needle",
            "Digits" => "12345",
            "Alternation" => "bird",
            "Email" => "someone@example.com",
            "Backreference" => "aa",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        // Filler must not create false starts for any pattern, or the comparison measures how well each
        // engine rejects them rather than how fast it scans. Excludes every first character of every
        // match above (n, 1, c, d, b, s, a) plus digits, '@' and '.'.
        const string Alphabet = "fghjklmpqrtvwxz";
        var sb = new StringBuilder(length);
        var rng = new Random(20260721);
        var previous = '\0';
        while (sb.Length < length - match.Length)
        {
            char c;
            do
            {
                c = Alphabet[rng.Next(Alphabet.Length)];
            }
            while (c == previous); // no adjacent repeats, so the backreference cannot match the filler
            sb.Append(c);
            previous = c;
        }
        sb.Append(match);
        return sb.ToString();
    }

    /// <summary>Splits <paramref name="subject"/> into <paramref name="segments"/> roughly equal chunks.</summary>
    internal static ReadOnlySequence<char> Segmented(string subject, int segments)
    {
        if (segments <= 1)
        {
            return new ReadOnlySequence<char>(subject.AsMemory());
        }

        var size = Math.Max(1, subject.Length / segments);
        var parts = new List<ReadOnlyMemory<char>>();
        for (var i = 0; i < subject.Length; i += size)
        {
            parts.Add(subject.AsMemory(i, Math.Min(size, subject.Length - i)));
        }

        var first = new Chunk(parts[0]);
        var last = first;
        for (var i = 1; i < parts.Count; i++)
        {
            last = last.Append(parts[i]);
        }
        return new ReadOnlySequence<char>(first, 0, last, last.Memory.Length);
    }

    private sealed class Chunk : ReadOnlySequenceSegment<char>
    {
        internal Chunk(ReadOnlyMemory<char> memory) => Memory = memory;

        internal Chunk Append(ReadOnlyMemory<char> memory)
        {
            var next = new Chunk(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
