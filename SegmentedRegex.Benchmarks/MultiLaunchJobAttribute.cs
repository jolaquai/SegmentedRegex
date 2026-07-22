using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// The default job, run across three processes instead of one.
/// </summary>
/// <remarks>
/// <para>
/// Everything except the launch count is left at BenchmarkDotNet's default on purpose, because those
/// defaults are adaptive: warmup runs until the timings settle and the iteration count grows until the
/// confidence interval is tight enough. Pinning them to constants would replace a convergence
/// criterion with a guess.
/// </para>
/// <para>
/// The launch count is the one thing worth overriding, and the default of 1 is what the old
/// <c>[ShortRunJob]</c> setup got wrong. The failure mode here is per-process: a process settles into a
/// tiering/PGO state and holds it, so every iteration it produces agrees with every other. Adaptive
/// iteration cannot detect that - it converges, confidently, on whatever that process is doing. Only a
/// second and third process can, and disagreement between them then surfaces as a large <c>StdDev</c>
/// rather than hiding inside a clean mean. The unchanged <c>Digits</c> case measured 126 ns, 299 ns,
/// 354 ns and 398 ns across four single-launch runs, which was misread as an effect of a change under
/// test.
/// </para>
/// </remarks>
internal sealed class MultiLaunchJobAttribute : JobConfigBaseAttribute
{
    internal MultiLaunchJobAttribute() : base(Job.Default.WithLaunchCount(3))
    {
    }
}
