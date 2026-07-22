using BenchmarkDotNet.Attributes;
using SegmentedRegex;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// Target 2: quantify what segmentation costs, per construct. Same pattern and characters throughout;
/// only the number of chunks changes, so the delta is purely the reader's boundary handling.
/// </summary>
[MultiLaunchJob]
[MemoryDiagnoser]
[HideColumns("Median", "Job")]
public class SegmentationOverheadBenchmarks
{
    [Params("Literal", "Digits", "Alternation", "Email", "Backreference")]
    public string Pattern { get; set; }

    /// <summary>1 is the contiguous baseline; higher values chop the same text into more chunks.</summary>
    [Params(1, 2, 16, 256)]
    public int Segments { get; set; }

    private ReadOnlySequence<char> _sequence;
    private SegEx _seg;

    [GlobalSetup]
    public void Setup()
    {
        var subject = Corpus.Subject(Pattern, 4096);
        _sequence = Corpus.Segmented(subject, Segments);
        _seg = Corpus.Seg(Pattern);

        if (!_seg.IsMatch(_sequence))
        {
            throw new InvalidOperationException($"{Pattern}: expected a match, or the benchmark measures a failed scan.");
        }
    }

    [Benchmark]
    public bool IsMatch() => _seg.IsMatch(_sequence);
}
