namespace SegmentedRegex.Tests;

/// <summary>
/// The same differential matrix as <see cref="DifferentialTests"/>, but through matchers produced by
/// the forked generator rather than the fallback engine. Any divergence from the BCL oracle on any
/// segmentation is a bug in the emitter retarget, the reader, or the runner.
/// </summary>
public class GeneratedEngineDifferentialTests
{
    public static TheoryData<string, string, RegexOptions> Corpus => DifferentialTests.Corpus;

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GeneratedResultsMatchTheOracle(string pattern, string subject, RegexOptions options)
    {
        var expected = Oracle.Describe(new Regex(pattern, options), subject);
        var segex = GeneratedCorpus.Get(pattern, options);

        foreach (var (label, sequence) in Segmentation.All(subject))
        {
            var actual = Oracle.Describe(segex, sequence);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Assert.Fail($"""
                    Pattern {pattern} on subject {subject} diverged from the oracle for segmentation '{label}' ({segex.GetType().Name}).
                    --- expected ---
                    {expected}
                    --- actual ---
                    {actual}
                    """);
            }
        }
    }

    [Fact]
    public void OnlyTheKnownMultiStringPatternTakesTheFallback()
    {
        // The differential theory passes whether a pattern is generated or fallback, since both are
        // correct against the oracle. Without this guard, a pattern silently dropping to the fallback
        // (e.g. a SupportsCodeGeneration regression) would leave the generated path untested with no
        // failure. So: assert exactly which corpus patterns are NOT segment-native. Today that is only
        // the multi-string LeadingStrings_* alternation, which is deliberately routed to the fallback.
        var fellBack = new List<string>();
        foreach (ITheoryDataRow row in Corpus)
        {
            var data = row.GetData();
            var pattern = (string)data[0];
            var options = (RegexOptions)data[2];
            if (GeneratedCorpus.Get(pattern, options) is not GeneratedSegEx)
            {
                fellBack.Add($"{pattern} [{options}]");
            }
        }

        Assert.Equal(["cat|dog|bird [None]"], fellBack.Distinct().Order());
    }

    [Fact]
    public void RightToLeftRoutesToTheFallbackAndStillMatches()
    {
        var type = GeneratorHarness.CompileAndLoadType("""
            using SegmentedRegex;
            using System.Text.RegularExpressions;

            public static partial class Rtl
            {
                [GeneratedSegEx("ab", RegexOptions.RightToLeft)]
                public static partial SegEx Matcher();
            }
            """, "Rtl");
        var segex = (SegEx)type.GetMethod("Matcher").Invoke(null, null);

        Assert.False(segex is GeneratedSegEx);

        // RTL semantics come from the BCL through the fallback engine; the last match is found first.
        var match = segex.Match(Segmentation.Build("ab x ", "ab"));
        Assert.True(match.Success);
        Assert.Equal(5, match.Index);
    }

    [Fact]
    public void GeneratedTimeoutSurfaces()
    {
        var type = GeneratorHarness.CompileAndLoadType("""
            using SegmentedRegex;
            using System.Text.RegularExpressions;

            public static partial class Timing
            {
                [GeneratedSegEx(@"^(\w+\s?)*$", RegexOptions.None, 50)]
                public static partial SegEx Catastrophic();
            }
            """, "Timing");
        var segex = (SegEx)type.GetMethod("Catastrophic").Invoke(null, null);

        Assert.True(segex is GeneratedSegEx, "expected the generated path so the emitted CheckTimeout calls are what is under test");
        _ = Assert.Throws<RegexMatchTimeoutException>(() => segex.IsMatch(Segmentation.Build("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "!")));
    }
}
