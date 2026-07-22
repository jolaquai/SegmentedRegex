using BenchmarkDotNet.Attributes;
using SegmentedRegex;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// Target 3: the fallback engine's materialization cost curve. It copies the whole subject into a
/// string before delegating to <see cref="Regex"/>, so its cost should grow with subject length
/// independently of how much of the subject the match actually needs to look at.
/// </summary>
[MultiLaunchJob]
[MemoryDiagnoser]
[HideColumns("Median", "Job")]
public class FallbackCostBenchmarks
{
    [Params(256, 4096, 65536)]
    public int Length { get; set; }

    /// <summary>Materializing a multi-chunk sequence has to stitch; a single chunk can shortcut.</summary>
    [Params(1, 64)]
    public int Segments { get; set; }

    private ReadOnlySequence<char> _sequence;
    private SegEx _generated;
    private SegEx _fallback;

    [GlobalSetup]
    public void Setup()
    {
        var subject = Corpus.Subject("Digits", Length);
        _sequence = Corpus.Segmented(subject, Segments);
        _generated = Corpus.SegDigits();
        _fallback = SegEx.Create(@"\d+");

        if (!_generated.IsMatch(_sequence) || !_fallback.IsMatch(_sequence))
        {
            throw new InvalidOperationException("expected a match from both engines.");
        }
    }

    [Benchmark(Baseline = true, Description = "generated (segment-native)")]
    public bool Generated() => _generated.IsMatch(_sequence);

    [Benchmark(Description = "fallback (materialize + Regex)")]
    public bool Fallback() => _fallback.IsMatch(_sequence);
}
