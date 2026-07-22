using BenchmarkDotNet.Attributes;
using SegmentedRegex;

namespace SegmentedRegex.Benchmarks;

/// <summary>
/// Isolates the individual <see cref="SegmentedSpan"/> operations the emitted matcher leans on, each
/// against its <see cref="ReadOnlySpan{T}"/> equivalent over the same contiguous characters. The
/// end-to-end numbers say cost tracks per-character work; these say which primitive is responsible, so
/// an optimization pass can be aimed rather than guessed.
/// </summary>
[MultiLaunchJob]
[MemoryDiagnoser]
[HideColumns("Median", "Job", "Gen0", "Allocated", "Alloc Ratio")]
public class ReaderPrimitiveBenchmarks
{
    private const int Length = 4096;

    private string _text;
    private ReadOnlySequence<char> _sequence;

    [GlobalSetup]
    public void Setup()
    {
        _text = Corpus.Subject("Digits", Length);
        _sequence = new ReadOnlySequence<char>(_text.AsMemory());
    }

    // --- per-character indexing: what a \d+ style loop does ---

    [Benchmark(Baseline = true, Description = "span: index every char")]
    public int SpanIndex()
    {
        var span = _text.AsSpan();
        var total = 0;
        for (var i = 0; i < span.Length; i++)
        {
            total += span[i];
        }
        return total;
    }

    [Benchmark(Description = "SegmentedSpan: index every char")]
    public int SegmentedIndex()
    {
        var reader = new SegmentedSpan(_sequence);
        var total = 0;
        for (var i = 0; i < reader.Length; i++)
        {
            total += reader[i];
        }
        return total;
    }

    // --- slicing: the emitted body re-slices after nearly every step ---

    [Benchmark(Description = "span: slice per position")]
    public int SpanSlice()
    {
        var span = _text.AsSpan();
        var total = 0;
        for (var i = 0; i < span.Length; i++)
        {
            total += span.Slice(i).Length;
        }
        return total;
    }

    [Benchmark(Description = "SegmentedSpan: slice per position")]
    public int SegmentedSlice()
    {
        var reader = new SegmentedSpan(_sequence);
        var total = 0;
        for (var i = 0; i < reader.Length; i++)
        {
            total += reader.Slice(i).Length;
        }
        return total;
    }

    // --- short SequenceEqual: exactly what a backreference does, once per candidate position ---

    [Benchmark(Description = "span: 1-char SequenceEqual")]
    public int SpanShortEqual()
    {
        var span = _text.AsSpan();
        var matches = 0;
        for (var i = 1; i < span.Length; i++)
        {
            if (span.Slice(i - 1, 1).SequenceEqual(span.Slice(i, 1)))
            {
                matches++;
            }
        }
        return matches;
    }

    [Benchmark(Description = "SegmentedSpan: 1-char SequenceEqual")]
    public int SegmentedShortEqual()
    {
        var reader = new SegmentedSpan(_sequence);
        var matches = 0;
        for (var i = 1; i < reader.Length; i++)
        {
            if (reader.Slice(i - 1, 1).SequenceEqual(reader.Slice(i, 1)))
            {
                matches++;
            }
        }
        return matches;
    }
}
