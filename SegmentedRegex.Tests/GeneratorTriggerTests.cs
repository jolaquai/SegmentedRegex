using Microsoft.CodeAnalysis;

namespace SegmentedRegex.Tests;

/// <summary>
/// The generator must fire for <see cref="GeneratedSegExAttribute"/> and stay completely out of the
/// way of the BCL's own <c>[GeneratedRegex]</c> generator, since both can be loaded in one project.
/// </summary>
public class GeneratorTriggerTests
{
    [Fact]
    public void FiresForGeneratedSegEx()
    {
        var result = GeneratorHarness.Run("""
            using SegmentedRegex;

            internal partial class C
            {
                [GeneratedSegEx(@"(\d+)")]
                internal static partial SegEx DigitRun();
            }
            """);

        Assert.True(result.Generated, "the generator produced no output for [GeneratedSegEx]");
        Assert.Contains("SegmentedRegex.Generated", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotFireForGeneratedRegex()
    {
        // The BCL generator owns this attribute. Firing here would double-generate in any project
        // that uses both.
        var result = GeneratorHarness.Run("""
            using System.Text.RegularExpressions;

            internal partial class C
            {
                [GeneratedRegex(@"(\d+)")]
                internal static partial Regex DigitRun();
            }
            """);

        Assert.False(result.Generated, $"the generator fired for [GeneratedRegex]:\n{result.Output}");
    }

    [Fact]
    public void EmitsIntoItsOwnNamespaceAndHintName()
    {
        var result = GeneratorHarness.Run("""
            using SegmentedRegex;

            internal partial class C
            {
                [GeneratedSegEx("abc")]
                internal static partial SegEx Literal();
            }
            """);

        Assert.Contains("namespace SegmentedRegex.Generated", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace System.Text.RegularExpressions.Generated", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetFrameworksWithoutTheSegmentNativeRuntimeGetTheFallback()
    {
        // Regression guard for a bug only a real package consumer surfaced: on netstandard2.0 the
        // segment-native types (GeneratedSegEx/SegExRunner/SegmentedSpan) are excluded from the lib leg,
        // but the analyzer still runs, so emitting a segment-native matcher there produced CS0246s in the
        // consumer's build. The generator has to notice and emit fallback boilerplate instead.
        var result = GeneratorHarness.RunWithoutSegmentNativeRuntime("""
            using System;
            using System.Text.RegularExpressions;

            namespace SegmentedRegex
            {
                public abstract class SegEx
                {
                    public static SegEx Create(string pattern, RegexOptions options) => null;
                    public static SegEx Create(string pattern, RegexOptions options, TimeSpan matchTimeout) => null;
                }

                [AttributeUsage(AttributeTargets.Method)]
                public sealed class GeneratedSegExAttribute : Attribute
                {
                    public GeneratedSegExAttribute(string pattern) { }
                    public GeneratedSegExAttribute(string pattern, RegexOptions options) { }
                }
            }

            internal partial class C
            {
                [SegmentedRegex.GeneratedSegEx(@"(\d+)")]
                internal static partial SegmentedRegex.SegEx DigitRun();
            }
            """);

        Assert.True(result.Generated, "the generator produced no output");
        Assert.Contains("SegEx.Create", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedSegEx\r\n", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(": GeneratedSegEx", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SegmentedSpan", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SegExRunner", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMemberReturningTheWrongType()
    {
        // Returning Regex rather than SegEx must not be silently accepted.
        var result = GeneratorHarness.Run("""
            using SegmentedRegex;
            using System.Text.RegularExpressions;

            internal partial class C
            {
                [GeneratedSegEx(@"(\d+)")]
                internal static partial Regex Wrong();
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }
}
