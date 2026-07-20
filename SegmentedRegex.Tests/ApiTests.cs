namespace SegmentedRegex.Tests;

/// <summary>
/// Behaviour of the public surface that the differential suite does not pin down on its own.
/// </summary>
public class ApiTests
{
    [Fact]
    public void ExposesTheConstructionParameters()
    {
        var timeout = TimeSpan.FromSeconds(5);
        var segex = SegEx.Create("a+", RegexOptions.IgnoreCase, timeout);

        Assert.Equal("a+", segex.Pattern);
        Assert.Equal(RegexOptions.IgnoreCase, segex.Options);
        Assert.Equal(timeout, segex.MatchTimeout);
        Assert.Equal("a+", segex.ToString());
        Assert.Equal(Regex.InfiniteMatchTimeout, SegEx.Create("a+").MatchTimeout);
    }

    [Fact]
    public void MaterializesValuesAcrossSegmentBoundaries()
    {
        // "2024-05" straddles three segments, and every group and capture must still read back whole.
        var sequence = Segmentation.Build("on 20", "", "24-0", "5 x");
        var match = SegEx.Create(@"(?<y>\d{4})-(?<m>\d{2})").Match(sequence);

        Assert.True(match.Success);
        Assert.Equal("2024-05", match.Value);
        Assert.Equal(3, match.Index);
        Assert.Equal("2024", match.Groups["y"].Value);
        Assert.Equal("05", match.Groups["m"].Value);
        Assert.Equal(3, match.Groups["y"].Index);
        Assert.Equal(8, match.Groups["m"].Index);
    }

    [Fact]
    public void LooksGroupsUpByNumberAndName()
    {
        var match = SegEx.Create("(?<first>a)(b)").Match("ab");

        Assert.Equal(3, match.Groups.Count);
        Assert.Equal("ab", match.Groups[0].Value);
        Assert.Equal("b", match.Groups[1].Value);
        Assert.Equal("a", match.Groups["first"].Value);
        Assert.Equal("a", match.Groups[2].Value);
    }

    [Fact]
    public void ResolvesExplicitlyNumberedGroups()
    {
        var match = SegEx.Create("(?<2>a)(?<4>b)").Match("ab");

        Assert.Equal("a", match.Groups[2].Value);
        Assert.Equal("b", match.Groups[4].Value);
        Assert.False(match.Groups[1].Success);
        Assert.False(match.Groups[3].Success);
    }

    [Fact]
    public void ReturnsUnsuccessfulGroupsForUnknownKeys()
    {
        var match = SegEx.Create("(a)").Match("a");

        Assert.False(match.Groups["nope"].Success);
        Assert.Equal(string.Empty, match.Groups["nope"].Value);
        Assert.False(match.Groups[42].Success);
    }

    [Fact]
    public void ReportsFailureRatherThanNullWhenNothingMatches()
    {
        var match = SegEx.Create("z+").Match("abc");

        Assert.False(match.Success);
        Assert.Equal(string.Empty, match.Value);
        Assert.Equal(0, match.Index);
        Assert.Equal(0, match.Length);
        Assert.Empty(SegEx.Create("z+").Matches("abc"));
    }

    [Fact]
    public void HandlesEmptyAndAllEmptySegmentSubjects()
    {
        var segex = SegEx.Create("^$");

        Assert.True(segex.IsMatch(ReadOnlySequence<char>.Empty));
        Assert.True(segex.IsMatch(Segmentation.Build("", "", "")));
        Assert.True(segex.IsMatch(string.Empty));
    }

    [Fact]
    public void CollectsEveryCaptureOfARepeatedGroup()
    {
        var match = SegEx.Create(@"(\d)+").Match(Segmentation.Build("ab", "c1", "23"));

        var captures = match.Groups[1].Captures;
        Assert.Equal(3, captures.Count);
        Assert.Equal(["1", "2", "3"], captures.Select(static c => c.Value));
        // The group itself reports its last capture, as System.Text.RegularExpressions does.
        Assert.Equal("3", match.Groups[1].Value);
    }

    [Fact]
    public void RejectsNullInput()
    {
        var segex = SegEx.Create("a");

        Assert.Throws<ArgumentNullException>(() => segex.IsMatch((string)null));
        Assert.Throws<ArgumentNullException>(() => segex.IsMatch((char[])null));
        Assert.Throws<ArgumentNullException>(() => segex.Match((string)null));
        Assert.Throws<ArgumentNullException>(() => segex.Matches((string)null));
        Assert.Throws<ArgumentNullException>(() => SegEx.Create(null));
        Assert.Throws<ArgumentNullException>(() => segex.Match("a").Groups[null]);
    }

    [Fact]
    public void SurfacesTimeouts()
    {
        // Catastrophic backtracking against a subject that cannot match.
        var segex = SegEx.Create(@"^(\w+\s?)*$", RegexOptions.None, TimeSpan.FromMilliseconds(50));

        _ = Assert.Throws<RegexMatchTimeoutException>(() => segex.IsMatch(Segmentation.Build("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "!")));
    }
}
