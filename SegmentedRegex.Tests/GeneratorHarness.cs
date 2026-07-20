using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions.Generator;

namespace SegmentedRegex.Tests;

/// <summary>
/// Runs the forked generator over a source snippet through a <see cref="GeneratorDriver"/>, so the
/// emitted text can be inspected and compiled in-process.
/// </summary>
internal static class GeneratorHarness
{
    internal sealed record Result(string Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompileDiagnostics)
    {
        internal bool Generated => !string.IsNullOrWhiteSpace(Output);

        internal IEnumerable<Diagnostic> Errors => CompileDiagnostics
            .Concat(GeneratorDiagnostics)
            .Where(static d => d.Severity == DiagnosticSeverity.Error);
    }

    internal static Result Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            ReferenceSet(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new RegexGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        var output = string.Join(
            Environment.NewLine,
            driver.GetRunResult().GeneratedTrees.Select(static t => t.ToString()));

        // Only diagnostics from the generated trees matter; the snippet itself is intentionally
        // incomplete in some tests (a partial method with no body, for instance).
        var compileDiagnostics = updated.GetDiagnostics()
            .Where(static d => d.Location.SourceTree is null || d.Location.SourceTree.FilePath.Contains(".g.cs", StringComparison.Ordinal))
            .ToImmutableArray();

        return new Result(output, generatorDiagnostics, compileDiagnostics);
    }

    private static List<MetadataReference> ReferenceSet()
    {
        var references = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        // SegmentedRegex itself may not be loaded yet when the first test runs, so make sure it is.
        var segex = typeof(SegEx).Assembly;
        if (!references.Any(r => string.Equals(r.Display, segex.Location, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromFile(segex.Location));
        }
        return references;
    }
}
