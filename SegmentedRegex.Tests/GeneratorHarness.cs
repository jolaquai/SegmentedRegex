using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions.Generator;

namespace SegmentedRegex.Tests;

/// <summary>
/// Runs the forked generator over a source snippet through a <see cref="GeneratorDriver"/>, so the
/// emitted text can be inspected, compiled, and executed in-process.
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
        var (_, output, generatorDiagnostics, updated) = RunCore(source);

        // Only diagnostics from the generated trees matter; the snippet itself is intentionally
        // incomplete in some tests (a partial method with no body, for instance).
        var compileDiagnostics = updated.GetDiagnostics()
            .Where(static d => d.Location.SourceTree is null || d.Location.SourceTree.FilePath.Contains(".g.cs", StringComparison.Ordinal))
            .ToImmutableArray();

        return new Result(output, generatorDiagnostics, compileDiagnostics);
    }

    /// <summary>
    /// Generates, compiles to a real assembly, loads it, and returns the requested type. Throws with
    /// the full generated text on any compile error, since that is the only way to debug the emitter.
    /// </summary>
    internal static Type CompileAndLoadType(string source, string typeName)
    {
        var (_, output, _, updated) = RunCore(source);

        using var ms = new MemoryStream();
        var emitted = updated.Emit(ms);
        if (!emitted.Success)
        {
            var errors = string.Join(Environment.NewLine, emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"""
                Generated code failed to compile:
                {errors}
                === generated ===
                {output}
                """);
        }

        var assembly = Assembly.Load(ms.ToArray());
        return assembly.GetType(typeName, throwOnError: true);
    }

    private static (CSharpCompilation Original, string Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, Compilation Updated) RunCore(string source)
    {
        var compilation = CSharpCompilation.Create(
            $"GeneratorTestAssembly_{Guid.NewGuid():N}",
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

        return (compilation, output, generatorDiagnostics, updated);
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
