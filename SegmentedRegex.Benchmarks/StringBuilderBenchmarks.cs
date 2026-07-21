using System.Buffers;
using BenchmarkDotNet.Attributes;
using SegmentedRegex;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// The scenario the library exists for. A <see cref="StringBuilder"/> already stores text as a chain
/// of chunks, so this engine matches it in place. <see cref="Regex"/> has no non-contiguous input, so
/// it must first flatten the builder - either into a string it allocates, or into a buffer the caller
/// rents and copies into by hand.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "Job", "RatioSD")]
public class StringBuilderBenchmarks
{
    /// <summary>Literal is near parity contiguous; Digits pays more per character.</summary>
    [Params("Literal", "Digits")]
    public string Pattern { get; set; }

    [Params(1024, 16384, 262144)]
    public int Length { get; set; }

    private StringBuilder _builder;
    private Regex _bcl;
    private SegEx _seg;

    [GlobalSetup]
    public void Setup()
    {
        var subject = Corpus.Subject(Pattern, Length);
        _bcl = Corpus.Bcl(Pattern);
        _seg = Corpus.Seg(Pattern);

        // Appended in pieces, as a builder is actually filled, so it is genuinely chunked.
        _builder = new StringBuilder();
        for (var i = 0; i < subject.Length; i += 512)
        {
            _builder.Append(subject.AsSpan(i, Math.Min(512, subject.Length - i)));
        }

        if (!BclToString() || !BclRented() || !SegBuilder())
        {
            throw new InvalidOperationException($"{Pattern}: all three approaches must match, or the comparison is not equal work.");
        }
    }

    [Benchmark(Baseline = true, Description = "Regex over sb.ToString()")]
    public bool BclToString() => _bcl.IsMatch(_builder.ToString());

    /// <summary>
    /// What a caller writes to avoid the string allocation: rent, copy, match, return. It also gives up
    /// capture groups - there is no span-based <see cref="Regex.Match(string)"/>, only
    /// <see cref="Regex.EnumerateMatches(ReadOnlySpan{char})"/>, which yields positions and lengths only.
    /// </summary>
    [Benchmark(Description = "Regex over rented+copied buffer")]
    public bool BclRented()
    {
        var length = _builder.Length;
        var buffer = ArrayPool<char>.Shared.Rent(length);
        try
        {
            _builder.CopyTo(0, buffer.AsSpan(0, length), length);
            return _bcl.IsMatch(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    [Benchmark(Description = "SegEx over builder chunks")]
    public bool SegBuilder() => _seg.IsMatch(_builder);
}
