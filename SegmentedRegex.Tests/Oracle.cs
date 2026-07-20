using System.Globalization;

namespace SegmentedRegex.Tests;

/// <summary>
/// Renders the full result structure of a match operation as canonical text, once for
/// <see cref="Regex"/> and once for <see cref="SegEx"/>, so the two can be compared with a single
/// equality check that produces a readable diff when they diverge.
/// </summary>
internal static class Oracle
{
    internal static string Describe(Regex regex, string subject)
    {
        var numbers = regex.GetGroupNumbers();
        Array.Sort(numbers);

        var sb = new StringBuilder();
        sb.Append("ismatch=").Append(regex.IsMatch(subject)).AppendLine();

        sb.Append("match: ");
        AppendMatch(sb, regex.Match(subject), regex, numbers);

        var matches = regex.Matches(subject);
        sb.Append("matches: count=").Append(matches.Count).AppendLine();
        for (var i = 0; i < matches.Count; i++)
        {
            sb.Append('[').Append(i).Append("] ");
            AppendMatch(sb, matches[i], regex, numbers);
        }
        return sb.ToString();
    }

    internal static string Describe(SegEx segex, in ReadOnlySequence<char> subject)
    {
        var sb = new StringBuilder();
        sb.Append("ismatch=").Append(segex.IsMatch(subject)).AppendLine();

        sb.Append("match: ");
        AppendMatch(sb, segex.Match(subject));

        var matches = segex.Matches(subject);
        sb.Append("matches: count=").Append(matches.Count).AppendLine();
        for (var i = 0; i < matches.Count; i++)
        {
            sb.Append('[').Append(i).Append("] ");
            AppendMatch(sb, matches[i]);
        }
        return sb.ToString();
    }

    private static void AppendMatch(StringBuilder sb, Match match, Regex regex, int[] numbers)
    {
        sb.Append("success=").Append(match.Success);
        if (!match.Success)
        {
            sb.AppendLine();
            return;
        }

        AppendSpan(sb, match.Index, match.Length, match.Value);
        foreach (var number in numbers)
        {
            var group = match.Groups[number];
            sb.Append("  group ").Append(number).Append(' ').Append(Quote(regex.GroupNameFromNumber(number)))
                .Append(" success=").Append(group.Success);
            AppendSpan(sb, group.Index, group.Length, group.Value);

            foreach (Capture capture in group.Captures)
            {
                sb.Append("    capture");
                AppendSpan(sb, capture.Index, capture.Length, capture.Value);
            }
        }
    }

    private static void AppendMatch(StringBuilder sb, SegExMatch match)
    {
        sb.Append("success=").Append(match.Success);
        if (!match.Success)
        {
            sb.AppendLine();
            return;
        }

        AppendSpan(sb, match.Index, match.Length, match.Value);
        foreach (var group in match.Groups)
        {
            sb.Append("  group ").Append(group.Number).Append(' ').Append(Quote(group.Name))
                .Append(" success=").Append(group.Success);
            AppendSpan(sb, group.Index, group.Length, group.Value);

            foreach (var capture in group.Captures)
            {
                sb.Append("    capture");
                AppendSpan(sb, capture.Index, capture.Length, capture.Value);
            }
        }
    }

    private static void AppendSpan(StringBuilder sb, int index, int length, string value) => sb.Append(" index=").Append(index.ToString(CultureInfo.InvariantCulture))
            .Append(" len=").Append(length.ToString(CultureInfo.InvariantCulture))
            .Append(" value=").Append(Quote(value))
            .AppendLine();

    private static string Quote(string value)
    {
        if (value is null)
        {
            return "<null>";
        }

        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            _ = c switch
            {
                '"' => sb.Append("\\\""),
                '\\' => sb.Append("\\\\"),
                '\n' => sb.Append("\\n"),
                '\r' => sb.Append("\\r"),
                '\t' => sb.Append("\\t"),
                _ => c < ' ' ? sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)) : sb.Append(c)
            };
        }
        return sb.Append('"').ToString();
    }
}
