namespace SegmentedRegex.Tests;

/// <summary>
/// Exercises the drive loop and result construction with hand-written runners standing in for the
/// generator, which does not exist yet. The runners implement patterns simple enough to be obviously
/// correct, so a divergence from the BCL points at the drive loop rather than at them.
/// </summary>
public class GeneratedSegExTests
{
    private sealed class HandWritten(string pattern, int[] numbers, string[] names, Func<SegExRunner> factory)
        : GeneratedSegEx(pattern, RegexOptions.None, Regex.InfiniteMatchTimeout, numbers, names)
    {
        protected override SegExRunner CreateRunner() => factory();
    }

    /// <summary>Matches <c>(\d+)</c>: the first run of digits at or after the current position.</summary>
    private sealed class DigitRunRunner : SegExRunner
    {
        protected override void Scan(SegmentedSpan inputSpan)
        {
            var length = inputSpan.Length;
            var pos = runtextpos;

            while (pos < length && !char.IsAsciiDigit(inputSpan[pos]))
            {
                pos++;
            }
            if (pos >= length)
            {
                runtextpos = length;
                return;
            }

            var start = pos;
            while (pos < length && char.IsAsciiDigit(inputSpan[pos]))
            {
                pos++;
            }

            Capture(0, start, pos);
            Capture(1, start, pos);
            runtextpos = pos;
        }
    }

    /// <summary>Matches <c>(\d*)</c>: always succeeds, possibly empty. Exercises empty-match advancement.</summary>
    private sealed class OptionalDigitsRunner : SegExRunner
    {
        protected override void Scan(SegmentedSpan inputSpan)
        {
            var length = inputSpan.Length;
            var start = runtextpos;
            var pos = start;

            while (pos < length && char.IsAsciiDigit(inputSpan[pos]))
            {
                pos++;
            }

            Capture(0, start, pos);
            Capture(1, start, pos);
            runtextpos = pos;
        }
    }

    /// <summary>Matches <c>(a)|(b)</c>. Exercises groups that do not participate.</summary>
    private sealed class AlternationRunner : SegExRunner
    {
        protected override void Scan(SegmentedSpan inputSpan)
        {
            var length = inputSpan.Length;
            var pos = runtextpos;

            while (pos < length && inputSpan[pos] is not ('a' or 'b'))
            {
                pos++;
            }
            if (pos >= length)
            {
                runtextpos = length;
                return;
            }

            Capture(0, pos, pos + 1);
            Capture(inputSpan[pos] == 'a' ? 1 : 2, pos, pos + 1);
            runtextpos = pos + 1;
        }
    }

    private static SegEx DigitRun() => new HandWritten(@"(\d+)", [0, 1], ["0", "1"], static () => new DigitRunRunner());
    private static SegEx OptionalDigits() => new HandWritten(@"(\d*)", [0, 1], ["0", "1"], static () => new OptionalDigitsRunner());
    private static SegEx Alternation() => new HandWritten("(a)|(b)", [0, 1, 2], ["0", "1", "2"], static () => new AlternationRunner());

    public static TheoryData<string, string> Cases => new()
    {
        { @"(\d+)", "ab123cd45e" },
        { @"(\d+)", "123" },
        { @"(\d+)", "no digits" },
        { @"(\d+)", "" },
        { @"(\d*)", "a12b" },
        { @"(\d*)", "" },
        { @"(\d*)", "12" },
        { @"(\d*)", "abc" },
        { "(a)|(b)", "xaybz" },
        { "(a)|(b)", "bbb" },
        { "(a)|(b)", "none here" },
    };

    private static SegEx ForPattern(string pattern) => pattern switch
    {
        @"(\d+)" => DigitRun(),
        @"(\d*)" => OptionalDigits(),
        "(a)|(b)" => Alternation(),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void DrivenResultsMatchTheOracleAcrossSegmentations(string pattern, string subject)
    {
        var expected = Oracle.Describe(new Regex(pattern), subject);
        var segex = ForPattern(pattern);

        foreach (var (label, sequence) in Segmentation.All(subject))
        {
            var actual = Oracle.Describe(segex, sequence);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Assert.Fail($"""
                    Pattern {pattern} on subject {subject} diverged from the oracle for segmentation '{label}'.
                    --- expected ---
                    {expected}
                    --- actual ---
                    {actual}
                    """);
            }
        }
    }

    [Fact]
    public void OrdersGroupsByNumberEvenWhenSlotsAreNotInOrder()
    {
        // Slot order 0,1,2 carrying group numbers 0,4,2: the groups must still come out 0,2,4, which
        // is what a pattern renumbering explicitly (e.g. "(?<4>a)(?<2>b)") produces.
        var segex = new HandWritten("(a)|(b)", [0, 4, 2], ["0", "4", "2"], static () => new AlternationRunner());
        var match = segex.Match("b");

        Assert.True(match.Success);
        Assert.Equal(3, match.Groups.Count);
        Assert.Equal([0, 2, 4], match.Groups.Select(static g => g.Number));

        // The alternation runner captures slot 2 for 'b', which carries group number 2.
        Assert.True(match.Groups[2].Success);
        Assert.Equal("b", match.Groups[2].Value);
        Assert.False(match.Groups[4].Success);
    }

    [Fact]
    public void RejectsMalformedGroupMetadata()
    {
        Assert.Throws<ArgumentNullException>(static () => new HandWritten("x", null, ["0"], static () => new DigitRunRunner()));
        Assert.Throws<ArgumentNullException>(static () => new HandWritten("x", [0], null, static () => new DigitRunRunner()));
        Assert.Throws<ArgumentException>(static () => new HandWritten("x", [0, 1], ["0"], static () => new DigitRunRunner()));
        Assert.Throws<ArgumentException>(static () => new HandWritten("x", [], [], static () => new DigitRunRunner()));
    }

    [Fact]
    public void PooledRunnersDoNotLeakStateAcrossConcurrentCallers()
    {
        var segex = DigitRun();
        var subjects = new[] { "a1b", "cc22dd", "333", "x4444y", "no" };

        Parallel.For(0, 2000, i =>
        {
            var subject = subjects[i % subjects.Length];
            var expected = new Regex(@"(\d+)").Match(subject);
            var actual = segex.Match(subject);

            Assert.Equal(expected.Success, actual.Success);
            if (expected.Success)
            {
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(expected.Index, actual.Index);
            }
        });
    }
}
