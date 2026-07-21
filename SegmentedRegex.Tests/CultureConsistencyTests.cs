using System.Globalization;

namespace SegmentedRegex.Tests;

/// <summary>
/// Whether a pattern is code-generated or routed to the fallback engine must not change how it folds
/// case. The generated path resolves <see cref="RegexOptions.IgnoreCase"/> against the invariant culture
/// at generation time, so the fallback has to fold invariantly too.
/// </summary>
/// <remarks>
/// Turkish is the standard probe: invariant lowercases 'I' to 'i', Turkish lowercases it to dotless
/// 'ı'. So a case-insensitive "I" matches "i" under invariant folding but not under Turkish folding.
/// <see cref="CultureInfo.CurrentCulture"/> is thread-local, so these tests do not race parallel ones.
/// </remarks>
public class CultureConsistencyTests
{
    private static void InTurkish(Action body)
    {
        var turkish = new CultureInfo("tr-TR");
        if (!string.Equals("I".ToLower(turkish), "ı", StringComparison.Ordinal))
        {
            Assert.Skip("Turkish casing is unavailable (globalization-invariant mode).");
        }

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = turkish;
        try
        {
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EngineChoiceDoesNotChangeCaseFolding()
    {
        InTurkish(static () =>
        {
            // Compiled and loaded while the thread is Turkish, so the fallback's Regex - built when its
            // singleton initializes - would pick up Turkish folding if it were not pinned to invariant.
            var type = GeneratorHarness.CompileAndLoadType("""
                using SegmentedRegex;
                using System.Text.RegularExpressions;

                public static partial class Casing
                {
                    [GeneratedSegEx("I", RegexOptions.IgnoreCase)]
                    public static partial SegEx Generated();

                    // RightToLeft cannot be code-generated, so this one routes to the fallback engine.
                    [GeneratedSegEx("I", RegexOptions.IgnoreCase | RegexOptions.RightToLeft)]
                    public static partial SegEx Fallback();
                }
                """, "Casing");

            var generated = (SegEx)type.GetMethod("Generated").Invoke(null, null);
            var fallback = (SegEx)type.GetMethod("Fallback").Invoke(null, null);

            Assert.True(generated is GeneratedSegEx, "expected the segment-native engine");
            Assert.False(fallback is GeneratedSegEx, "expected the fallback engine");

            Assert.True(generated.IsMatch("i"), "generated path should fold invariantly");
            Assert.True(fallback.IsMatch("i"), "fallback path should fold invariantly, matching the generated path");
        });
    }

    [Fact]
    public void RuntimeConstructedInstancesKeepRegexSemantics()
    {
        // SegEx.Create is the runtime analogue of new Regex(...), so it deliberately folds with the
        // current culture rather than being pinned to invariant. Turkish 'I' does not match dotted 'i'.
        InTurkish(static () =>
        {
            Assert.False(SegEx.Create("I", RegexOptions.IgnoreCase).IsMatch("i"));
            Assert.True(SegEx.Create("I", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).IsMatch("i"));
        });
    }

    [Fact]
    public void GeneratorAcceptsAnExplicitCulture()
    {
        var type = GeneratorHarness.CompileAndLoadType("""
            using SegmentedRegex;
            using System.Text.RegularExpressions;

            public static partial class Named
            {
                [GeneratedSegEx("I", RegexOptions.IgnoreCase, "tr-TR")]
                public static partial SegEx Turkish();
            }
            """, "Named");

        var turkish = (SegEx)type.GetMethod("Turkish").Invoke(null, null);

        Assert.True(turkish is GeneratedSegEx);
        // Folding was resolved against tr-TR at generation time, so dotted 'i' is not a match, and this
        // holds regardless of the culture the process is running in now.
        Assert.False(turkish.IsMatch("i"));
        Assert.True(turkish.IsMatch("ı"));
    }
}
