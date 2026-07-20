namespace SegmentedRegex.Tests;

/// <summary>
/// Checks <see cref="SegEx"/> against <see cref="Regex"/> on the contiguous subject, for every
/// segmentation of that subject. The oracle comparison covers the whole result structure, not just
/// whether a match was found.
/// </summary>
public class DifferentialTests
{
    /// <summary>Pattern, subject, options. One entry per emitter construct the plan calls out.</summary>
    public static TheoryData<string, string, RegexOptions> Corpus => new()
    {
        // literals and sets
        { "abc", "xxabcxxabc", RegexOptions.None },
        { "[a-f]+", "zzabcfzzdef", RegexOptions.None },
        { "[^0-9]+", "12ab34cd", RegexOptions.None },
        { @"\p{Lu}+", "abCDefGH", RegexOptions.None },

        // loops: greedy, lazy, atomic, bounded
        { "a+", "baaabaa", RegexOptions.None },
        { "a+?b", "aaab", RegexOptions.None },
        { "(?>a+)b", "aaab", RegexOptions.None },
        { "a{2,4}", "aaaaaa", RegexOptions.None },
        { "a*", "bbb", RegexOptions.None },
        { "a*", "", RegexOptions.None },

        // alternation
        { "cat|dog|bird", "a dog and a cat", RegexOptions.None },
        { "(a)|(b)", "ba", RegexOptions.None },

        // backreferences
        { @"(\w)\1", "aabbcd", RegexOptions.None },
        { @"(?<c>\w)\k<c>", "xxyyz", RegexOptions.None },

        // anchors and boundaries
        { "^abc$", "abc", RegexOptions.None },
        { @"^\w+$", "ab\ncd\nef", RegexOptions.Multiline },
        { @"\bcat\b", "cat concat cat", RegexOptions.None },
        { @"\b", "hi there", RegexOptions.None },
        { @"\Aab", "abab", RegexOptions.None },

        // lookarounds
        { "foo(?=bar)", "foobar fooqux", RegexOptions.None },
        { "foo(?!bar)", "foobar fooqux", RegexOptions.None },
        { "(?<=a)b", "ab cb", RegexOptions.None },
        { "(?<!a)b", "ab cb", RegexOptions.None },

        // captures
        { @"(\d)+", "abc123", RegexOptions.None },
        { "((a)(b))+", "abab", RegexOptions.None },
        { "(a)?b", "b", RegexOptions.None },
        { @"(?<y>\d{4})-(?<m>\d{2})", "on 2024-05 x", RegexOptions.None },
        // explicitly numbered groups: the one shape where group numbers are not 0..n-1
        { "(?<2>a)(?<4>b)", "ab", RegexOptions.None },
        { "(a)(?<n>b)", "ab", RegexOptions.ExplicitCapture },

        // options
        { "ABC", "xxabcxx", RegexOptions.IgnoreCase },
        { "a.c", "abc a\nc", RegexOptions.None },
        { "a.c", "a\nc", RegexOptions.Singleline },
        { @"\d+ \s+ \d+", "12  34", RegexOptions.IgnorePatternWhitespace },

        // longer subject, past the quadratic-segmentation cutoff
        { @"(\w+)@(\w+)\.com", "mail bob@example.com and eve@test.com end", RegexOptions.None },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void SegmentedResultsMatchTheOracle(string pattern, string subject, RegexOptions options)
    {
        var expected = Oracle.Describe(new Regex(pattern, options), subject);
        var segex = SegEx.Create(pattern, options);

        foreach (var (label, sequence) in Segmentation.All(subject))
        {
            var actual = Oracle.Describe(segex, sequence);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Assert.Fail($"""
                    Pattern {pattern} on subject {subject} diverged from the oracle for segmentation '{label}'.
                    --- expected ---
                    {expected}
                    --- actual ---
                    {actual}
                    """);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ContiguousOverloadsAgree(string pattern, string subject, RegexOptions options)
    {
        var segex = SegEx.Create(pattern, options);
        var expected = Oracle.Describe(segex, new ReadOnlySequence<char>(subject.AsMemory()));

        Assert.Equal(expected, Oracle.Describe(segex, new ReadOnlySequence<char>(subject.ToCharArray())));

        // The overloads that do not take a sequence route through the same engine.
        Assert.Equal(segex.IsMatch(new ReadOnlySequence<char>(subject.AsMemory())), segex.IsMatch(subject));
        Assert.Equal(segex.IsMatch(subject), segex.IsMatch(subject.ToCharArray()));
        Assert.Equal(segex.IsMatch(subject), segex.IsMatch(subject.AsMemory()));
        Assert.Equal(segex.IsMatch(subject), segex.IsMatch(subject.AsSpan()));

        Assert.Equal(segex.Match(subject).Value, segex.Match(subject.AsSpan()).Value);
        Assert.Equal(segex.Matches(subject).Count, segex.Matches(subject.AsSpan()).Count);
    }
}
