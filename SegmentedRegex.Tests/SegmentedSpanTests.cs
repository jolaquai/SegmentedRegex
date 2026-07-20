namespace SegmentedRegex.Tests;

/// <summary>
/// Checks <see cref="SegmentedSpan"/> against <see cref="ReadOnlySpan{T}"/> over the same characters,
/// for every segmentation. Any divergence is a bug in the chunk cache, the seek, or the boundary
/// stitching, since the two are meant to be interchangeable.
/// </summary>
public class SegmentedSpanTests
{
    public static TheoryData<string> Subjects =>
    [
        "",
        "a",
        "ab",
        "abcabc",
        "aaabbb",
        "xyz123",
        "a\nb\tc",
        "AbCdEf",
        "  spaced  ",
    ];

    [Theory]
    [MemberData(nameof(Subjects))]
    public void BehavesLikeAContiguousSpan(string subject)
    {
        foreach (var (label, sequence) in Segmentation.All(subject))
        {
            AssertReadsAgree(subject, sequence, label);
            AssertSearchesAgree(subject, sequence, label);
            AssertSlicesAgree(subject, sequence, label);
        }
    }

    private static void AssertReadsAgree(string subject, in ReadOnlySequence<char> sequence, string label)
    {
        var span = subject.AsSpan();
        var seg = new SegmentedSpan(sequence);

        Assert.Equal(span.Length, seg.Length);
        Assert.Equal(span.IsEmpty, seg.IsEmpty);
        Assert.Equal(subject, seg.ToString());

        for (var i = 0; i < span.Length; i++)
        {
            Assert.True(span[i] == seg[i], $"[{label}] forward index {i}");
        }

        // Backwards, to force the rewind path rather than only ever scanning forward.
        for (var i = span.Length - 1; i >= 0; i--)
        {
            Assert.True(span[i] == seg[i], $"[{label}] reverse index {i}");
        }

        if (span.Length > 0)
        {
            var captured = sequence; // an 'in' parameter cannot be captured by the lambda
            var past = subject.Length;
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SegmentedSpan(captured)[past]);
        }

        var buffer = new char[span.Length];
        seg.CopyTo(buffer);
        Assert.Equal(subject, new string(buffer));

        if (seg.TryGetContiguous(out var contiguous))
        {
            Assert.Equal(subject, contiguous.ToString());
        }
    }

    private static void AssertSearchesAgree(string subject, in ReadOnlySequence<char> sequence, string label)
    {
        var span = subject.AsSpan();
        var seg = new SegmentedSpan(sequence);

        // Characters present in the subject, plus ones deliberately absent.
        const string Probes = "abcxyzAE1 \n\tQ";

        foreach (var c in Probes)
        {
            Assert.True(span.IndexOf(c) == seg.IndexOf(c), $"[{label}] IndexOf('{c}')");
            Assert.True(span.Contains(c) == seg.Contains(c), $"[{label}] Contains('{c}')");
            Assert.True(span.IndexOfAnyExcept(c) == seg.IndexOfAnyExcept(c), $"[{label}] IndexOfAnyExcept('{c}')");
        }

        foreach (var (a, b) in new[] { ('a', 'b'), ('x', 'Q'), ('z', 'a'), ('\n', '\t') })
        {
            Assert.True(span.IndexOfAny(a, b) == seg.IndexOfAny(a, b), $"[{label}] IndexOfAny('{a}','{b}')");
            Assert.True(span.IndexOfAnyExcept(a, b) == seg.IndexOfAnyExcept(a, b), $"[{label}] IndexOfAnyExcept('{a}','{b}')");
        }

        foreach (var (a, b, c) in new[] { ('a', 'b', 'c'), ('x', 'y', 'Q'), ('1', '2', '3') })
        {
            Assert.True(span.IndexOfAny(a, b, c) == seg.IndexOfAny(a, b, c), $"[{label}] IndexOfAny('{a}','{b}','{c}')");
            Assert.True(span.IndexOfAnyExcept(a, b, c) == seg.IndexOfAnyExcept(a, b, c), $"[{label}] IndexOfAnyExcept3");
        }

        foreach (var set in new[] { "abc", "xyz", "Q", "", " \t\n", "abcdefghijklmnop" })
        {
            Assert.True(span.IndexOfAny(set) == seg.IndexOfAny(set), $"[{label}] IndexOfAny(\"{set}\")");
            Assert.True(span.IndexOfAnyExcept(set) == seg.IndexOfAnyExcept(set), $"[{label}] IndexOfAnyExcept(\"{set}\")");

            var values = SearchValues.Create(set);
            Assert.True(span.IndexOfAny(values) == seg.IndexOfAny(values), $"[{label}] IndexOfAny(SearchValues \"{set}\")");
            Assert.True(span.IndexOfAnyExcept(values) == seg.IndexOfAnyExcept(values), $"[{label}] IndexOfAnyExcept(SearchValues \"{set}\")");
        }

        foreach (var (lo, hi) in new[] { ('a', 'f'), ('0', '9'), ('A', 'Z'), ('a', 'a') })
        {
            Assert.True(span.IndexOfAnyInRange(lo, hi) == seg.IndexOfAnyInRange(lo, hi), $"[{label}] IndexOfAnyInRange('{lo}','{hi}')");
            Assert.True(span.IndexOfAnyExceptInRange(lo, hi) == seg.IndexOfAnyExceptInRange(lo, hi), $"[{label}] IndexOfAnyExceptInRange('{lo}','{hi}')");
        }

        // Every prefix of the subject must match, and a mutated one must not.
        for (var i = 0; i <= subject.Length; i++)
        {
            var prefix = subject[..i];
            Assert.True(seg.StartsWith(prefix), $"[{label}] StartsWith(\"{prefix}\")");
            Assert.True(span.SequenceEqual(prefix) == seg.SequenceEqual(prefix), $"[{label}] SequenceEqual(\"{prefix}\")");
        }

        // Multi-char search: every substring of the subject must be found where the span finds it, and
        // needles that straddle or exceed chunk boundaries are the whole point.
        foreach (var needle in Needles(subject))
        {
            Assert.True(span.IndexOf(needle) == seg.IndexOf(needle), $"[{label}] IndexOf(\"{needle}\")");
        }

        Assert.True(seg.SequenceEqual(subject), $"[{label}] SequenceEqual(self)");
        Assert.False(seg.StartsWith(subject + "!"), $"[{label}] StartsWith(longer)");
        if (subject.Length > 0)
        {
            Assert.False(seg.StartsWith("" + subject[1..]), $"[{label}] StartsWith(mutated)");
        }
    }

    /// <summary>Every substring of the subject, plus needles that cannot match, plus the empty needle.</summary>
    private static IEnumerable<string> Needles(string subject)
    {
        yield return "";
        yield return "q";
        yield return "zz";
        yield return subject + "!";

        for (var start = 0; start < subject.Length; start++)
        {
            for (var length = 1; length <= subject.Length - start; length++)
            {
                yield return subject.Substring(start, length);
            }
        }
    }

    private static void AssertSlicesAgree(string subject, in ReadOnlySequence<char> sequence, string label)
    {
        for (var start = 0; start <= subject.Length; start++)
        {
            var expected = subject[start..];
            var sliced = new SegmentedSpan(sequence).Slice(start);

            Assert.True(sliced.Length == expected.Length, $"[{label}] Slice({start}).Length");
            Assert.True(sliced.SequenceEqual(expected), $"[{label}] Slice({start}) content");
            Assert.Equal(expected, sliced.ToString());

            for (var length = 0; length <= subject.Length - start; length++)
            {
                var window = subject.Substring(start, length);
                // Sliced off a reader that has already cached a chunk, so a stale carried-over cache shows up here.
                var warm = new SegmentedSpan(sequence);
                _ = warm.IndexOf('z');
                var ranged = warm.Slice(start, length);

                Assert.True(ranged.Length == length, $"[{label}] Slice({start},{length}).Length");
                Assert.True(ranged.SequenceEqual(window), $"[{label}] Slice({start},{length}) content");
                Assert.True(ranged.IndexOf('a') == window.AsSpan().IndexOf('a'), $"[{label}] Slice({start},{length}).IndexOf('a')");
                Assert.True(ranged.IndexOfAny("bc") == window.AsSpan().IndexOfAny("bc"), $"[{label}] Slice({start},{length}).IndexOfAny");
            }
        }
    }
}
