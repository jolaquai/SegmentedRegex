using BenchmarkDotNet.Attributes;
using SegmentedRegex;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// Target 1: the generated path on a single-segment subject should be within noise of upstream
/// <c>[GeneratedRegex]</c>. This is the load-bearing claim of the whole library - a contiguous subject
/// is supposed to collapse to the same span operations the BCL runs.
/// </summary>
[MultiLaunchJob]
[MemoryDiagnoser]
[HideColumns("Median", "Job")]
public class ContiguousParityBenchmarks
{
    [Params("Literal", "Digits", "Alternation", "Email", "Backreference")]
    public string Pattern { get; set; }

    [Params(4096)]
    public int Length { get; set; }

    private string _subject;
    private ReadOnlySequence<char> _sequence;
    private Regex _bcl;
    private SegEx _seg;

    [GlobalSetup]
    public void Setup()
    {
        _subject = Corpus.Subject(Pattern, Length);
        _sequence = new ReadOnlySequence<char>(_subject.AsMemory());
        _bcl = Corpus.Bcl(Pattern);
        _seg = Corpus.Seg(Pattern);

        // Both engines must find the same match, otherwise the two sides are not doing equal work and
        // the ratio is meaningless.
        var bclMatch = _bcl.Match(_subject);
        var segMatch = _seg.Match(_sequence);
        if (!bclMatch.Success || !segMatch.Success || bclMatch.Index != segMatch.Index || bclMatch.Length != segMatch.Length)
        {
            throw new InvalidOperationException(
                $"{Pattern}: engines disagree - BCL {bclMatch.Success}@{bclMatch.Index}+{bclMatch.Length}, SegEx {segMatch.Success}@{segMatch.Index}+{segMatch.Length}.");
        }
    }

    [Benchmark(Baseline = true, Description = "Regex.IsMatch")]
    public bool BclIsMatch() => _bcl.IsMatch(_subject);

    [Benchmark(Description = "SegEx.IsMatch")]
    public bool SegIsMatch() => _seg.IsMatch(_sequence);

    [Benchmark(Description = "Regex.Match+Value")]
    public string BclMatch() => _bcl.Match(_subject).Value;

    [Benchmark(Description = "SegEx.Match+Value")]
    public string SegMatch() => _seg.Match(_sequence).Value;
}
