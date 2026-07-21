namespace SegmentedRegex.Tests;

/// <summary>
/// A <see cref="StringBuilder"/> already stores text as a chain of chunks, so it maps onto the
/// sequence API with no copying. These check that the mapping is faithful - order, content and
/// matching - against the BCL oracle over the same text.
/// </summary>
public class StringBuilderTests
{
    /// <summary>Builds a builder whose contents are <paramref name="text"/>, appended in small pieces.</summary>
    private static StringBuilder Build(string text, int pieceSize = 7)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i += pieceSize)
        {
            sb.Append(text.AsSpan(i, Math.Min(pieceSize, text.Length - i)));
        }
        return sb;
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc123def")]
    [InlineData("on 2024-05 and 2025-11 x")]
    public void SequenceRoundTripsTheBuilderContents(string text)
    {
        var sb = Build(text);
        var sequence = sb.AsSequence();

        Assert.Equal(text.Length, (int)sequence.Length);
        Assert.Equal(text, new string(sequence.ToArray()));
    }

    [Fact]
    public void LargeBuildersProduceGenuinelyMultipleChunks()
    {
        // The zero-copy claim only means anything if a real builder is actually chunked; if it always
        // came back as one segment this would silently be testing the contiguous path.
        var sb = Build(new string('x', 100_000), pieceSize: 997);
        var sequence = sb.AsSequence();

        var segments = 0;
        foreach (var _ in sequence)
        {
            segments++;
        }

        Assert.True(segments > 1, $"expected a chunked builder, got {segments} segment(s)");
        Assert.Equal(100_000, (int)sequence.Length);
    }

    [Fact]
    public void SequenceViewsTheBuildersOwnBuffersWithoutCopying()
    {
        // Proven by observing the aliasing: mutating the builder in place is visible through a
        // sequence taken beforehand. This is also exactly why the API documents that the sequence is
        // only valid while the builder is not modified.
        var sb = Build("aaaaaaaaaaaaaaaaaaaa");
        var sequence = sb.AsSequence();
        sb[0] = 'b';

        Assert.Equal('b', sequence.First.Span[0]);
    }

    [Theory]
    [InlineData(@"(?<y>\d{4})-(?<m>\d{2})", "on 2024-05 and 2025-11 x")]
    [InlineData(@"\w+@\w+\.com", "mail bob@example.com and eve@test.com end")]
    [InlineData("cat|dog|bird", "a dog and a cat and a bird")]
    [InlineData(@"(\w)\1", "aabbcd")]
    public void MatchingABuilderAgreesWithTheOracle(string pattern, string text)
    {
        var expected = Oracle.Describe(new Regex(pattern), text);

        // Several piece sizes, so the chunk boundaries land in different places relative to the match.
        foreach (var pieceSize in new[] { 1, 2, 3, 7, 13, text.Length })
        {
            var sb = Build(text, pieceSize);
            var actual = Oracle.Describe(SegEx.Create(pattern), sb.AsSequence());
            Assert.True(expected == actual, $"piece size {pieceSize} diverged from the oracle");
        }
    }

    [Fact]
    public void ConvenienceOverloadsTakeABuilderDirectly()
    {
        var sb = Build("on 2024-05 x");
        var segex = SegEx.Create(@"(?<y>\d{4})-(?<m>\d{2})");

        Assert.True(segex.IsMatch(sb));
        Assert.Equal("2024-05", segex.Match(sb).Value);
        Assert.Equal("2024", segex.Match(sb).Groups["y"].Value);
        Assert.Single(segex.Matches(sb));
    }

    [Fact]
    public void RejectsANullBuilder()
    {
        Assert.Throws<ArgumentNullException>(static () => ((StringBuilder)null).AsSequence());
        Assert.Throws<ArgumentNullException>(static () => SegEx.Create("a").IsMatch((StringBuilder)null));
    }
}
