namespace SegmentedRegex.Tests;

/// <summary>
/// The <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> AppContext default must reach the fallback engine
/// (<see cref="SegEx.Create(string)"/>) exactly as it reaches the generated path, so two patterns in
/// one project cannot silently disagree about the process-wide default timeout.
/// </summary>
/// <remarks>
/// This class is the only place that writes the <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> switch. Nothing
/// else reads the default (the assertion that once lived in <c>ApiTests</c> was moved here), so its
/// set/restore windows cannot race another test. Methods within a class run serially in xUnit, so the
/// windows do not race each other either.
/// </remarks>
public class TimeoutDefaultTests
{
    private const string Key = "REGEX_DEFAULT_MATCH_TIMEOUT";

    private static void WithDefault(TimeSpan value, Action body)
    {
        var previous = AppContext.GetData(Key);
        AppContext.SetData(Key, value);
        try
        {
            body();
        }
        finally
        {
            AppContext.SetData(Key, previous);
        }
    }

    [Fact]
    public void UnsetSwitchLeavesTheFallbackTimeoutInfinite()
    {
        var previous = AppContext.GetData(Key);
        AppContext.SetData(Key, null);
        try
        {
            Assert.Equal(Regex.InfiniteMatchTimeout, SegEx.Create("a+").MatchTimeout);
            Assert.Equal(Regex.InfiniteMatchTimeout, SegEx.Create("a+", RegexOptions.None).MatchTimeout);
        }
        finally
        {
            AppContext.SetData(Key, previous);
        }
    }

    [Fact]
    public void FallbackHonorsTheDefaultSwitch()
    {
        var expected = TimeSpan.FromSeconds(7);
        WithDefault(expected, () =>
        {
            Assert.Equal(expected, SegEx.Create("a+").MatchTimeout);
            Assert.Equal(expected, SegEx.Create("a+", RegexOptions.IgnoreCase).MatchTimeout);
        });
    }

    [Fact]
    public void ExplicitTimeoutWinsOverTheDefaultSwitch()
    {
        var explicitTimeout = TimeSpan.FromMilliseconds(250);
        WithDefault(TimeSpan.FromSeconds(7), () => Assert.Equal(explicitTimeout, SegEx.Create("a+", RegexOptions.None, explicitTimeout).MatchTimeout));
    }

    [Fact]
    public void GeneratedPathAndFallbackAgreeOnTheDefaultSwitch()
    {
        var expected = TimeSpan.FromSeconds(9);
        WithDefault(expected, () =>
        {
            // Fresh assembly so the emitted Utilities.s_defaultTimeout reads the switch at first access,
            // which happens when the partial method builds the singleton below.
            var type = GeneratorHarness.CompileAndLoadType("""
                using SegmentedRegex;

                public static partial class Defaulted
                {
                    [GeneratedSegEx("a+")]
                    public static partial SegEx Matcher();
                }
                """, "Defaulted");
            var generated = (SegEx)type.GetMethod("Matcher").Invoke(null, null);

            Assert.True(generated is GeneratedSegEx, "expected the generated path");
            Assert.Equal(expected, generated.MatchTimeout);
            Assert.Equal(generated.MatchTimeout, SegEx.Create("a+").MatchTimeout);
        });
    }

    [Fact]
    public void GeneratedExplicitTimeoutIgnoresTheDefaultSwitch()
    {
        WithDefault(TimeSpan.FromSeconds(9), () =>
        {
            var type = GeneratorHarness.CompileAndLoadType("""
                using SegmentedRegex;
                using System.Text.RegularExpressions;

                public static partial class Fixed
                {
                    [GeneratedSegEx("a+", RegexOptions.None, 250)]
                    public static partial SegEx Matcher();
                }
                """, "Fixed");
            var generated = (SegEx)type.GetMethod("Matcher").Invoke(null, null);

            Assert.Equal(TimeSpan.FromMilliseconds(250), generated.MatchTimeout);
        });
    }
}
