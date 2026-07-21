using BenchmarkDotNet.Running;

namespace SegmentedRegex.Benchmarks;

internal static class Program
{
    /// <summary>
    /// Run everything with <c>dotnet run -c Release</c>, or a subset with
    /// <c>dotnet run -c Release -- --filter *ContiguousParity*</c>.
    /// </summary>
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
