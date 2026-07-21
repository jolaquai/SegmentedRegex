namespace SegmentedRegex.Tests;

/// <summary>
/// The segmentation matrix underpins every differential test, so it gets its own checks: a bug that
/// produced wrong or degenerate layouts would quietly weaken all of them.
/// </summary>
public class SegmentationTests
{
    private const string LongSubject = "mail bob@example.com and eve@test.com end";

    public static TheoryData<string> Subjects =>
    [
        "",
        "a",
        "abcabc",
        "0123456789ab",
        LongSubject,
    ];

    [Theory]
    [MemberData(nameof(Subjects))]
    public void EverySegmentationReconstructsTheSubject(string subject)
    {
        foreach (var (label, sequence) in Segmentation.All(subject))
        {
            Assert.True(sequence.Length == subject.Length, $"[{label}] length {sequence.Length} != {subject.Length}");
            Assert.True(new string(sequence.ToArray()) == subject, $"[{label}] content mismatch");
        }
    }

    [Fact]
    public void LongSubjectsGetIrregularManyChunkLayouts()
    {
        // Without these, a long subject only ever sees single splits, one-char-per-segment, and
        // empty-padded variants - never an irregular multi-chunk layout.
        var randomized = Segmentation.All(LongSubject).Where(static s => s.Label.StartsWith("random", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(randomized);
        Assert.Contains(randomized, static s => SegmentCount(s.Sequence) > 3);
    }

    [Fact]
    public void ShortSubjectsSkipTheRandomizedLayouts()
    {
        // Short subjects are covered exhaustively instead, so the randomized ones would be redundant.
        Assert.DoesNotContain(Segmentation.All("abcabc"), static s => s.Label.StartsWith("random", StringComparison.Ordinal));
    }

    [Fact]
    public void SegmentationsAreReproducible()
    {
        // Seeded per subject rather than randomly: a segmentation that fails must fail again on the
        // next run, otherwise the failure cannot be investigated.
        static List<string> Shapes(string subject) => Segmentation.All(subject)
            .Select(static s => $"{s.Label}:{string.Join(",", Lengths(s.Sequence))}")
            .ToList();

        Assert.Equal(Shapes(LongSubject), Shapes(LongSubject));
    }

    [Fact]
    public void EmptySegmentsArePresent()
    {
        var hasEmpty = Segmentation.All(LongSubject).Any(static s => Lengths(s.Sequence).Any(static l => l == 0));
        Assert.True(hasEmpty, "no segmentation contained an empty segment");
    }

    private static int SegmentCount(ReadOnlySequence<char> sequence)
    {
        var count = 0;
        foreach (var _ in sequence)
        {
            count++;
        }
        return count;
    }

    private static List<int> Lengths(ReadOnlySequence<char> sequence)
    {
        var lengths = new List<int>();
        foreach (var segment in sequence)
        {
            lengths.Add(segment.Length);
        }
        return lengths;
    }
}
