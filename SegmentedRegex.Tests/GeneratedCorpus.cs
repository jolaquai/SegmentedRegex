using System.Reflection;

namespace SegmentedRegex.Tests;

/// <summary>
/// Compiles every distinct (pattern, options) pair of the differential corpus through the forked
/// generator into a single in-memory assembly, once, and hands out the resulting matchers. One
/// batched compilation keeps the corpus-wide generator tests fast enough to run on every build.
/// </summary>
internal static class GeneratedCorpus
{
    private static readonly Lazy<Dictionary<(string Pattern, RegexOptions Options), SegEx>> s_matchers = new(Create);

    internal static SegEx Get(string pattern, RegexOptions options) => s_matchers.Value[(pattern, options)];

    private static Dictionary<(string, RegexOptions), SegEx> Create()
    {
        var (pairs, source) = BuildSource();
        var type = GeneratorHarness.CompileAndLoadType(source, "CorpusMatchers");

        var matchers = new Dictionary<(string, RegexOptions), SegEx>();
        for (var i = 0; i < pairs.Count; i++)
        {
            matchers[pairs[i]] = (SegEx)type.GetMethod($"M{i}", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }
        return matchers;
    }

    internal static (List<(string Pattern, RegexOptions Options)> Pairs, string Source) BuildSource()
    {
        var pairs = new List<(string Pattern, RegexOptions Options)>();
        foreach (ITheoryDataRow row in DifferentialTests.Corpus)
        {
            var data = row.GetData();
            var pair = ((string)data[0], (RegexOptions)data[2]);
            if (!pairs.Contains(pair))
            {
                pairs.Add(pair);
            }
        }

        var sb = new StringBuilder()
            .AppendLine("using SegmentedRegex;")
            .AppendLine("using System.Text.RegularExpressions;")
            .AppendLine()
            .AppendLine("public static partial class CorpusMatchers")
            .AppendLine("{");
        for (var i = 0; i < pairs.Count; i++)
        {
            sb.Append("    [GeneratedSegEx(@\"").Append(pairs[i].Pattern.Replace("\"", "\"\"")).Append("\", (RegexOptions)").Append((int)pairs[i].Options).AppendLine(")]");
            sb.Append("    public static partial SegEx M").Append(i).AppendLine("();");
            sb.AppendLine();
        }
        sb.AppendLine("}");
        return (pairs, sb.ToString());
    }
}
