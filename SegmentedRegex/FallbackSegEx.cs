namespace SegmentedRegex;

/// <summary>
/// Matches by materializing the subject into a contiguous string and delegating to <see cref="Regex"/>,
/// then translating the results back onto the sequence.
/// </summary>
/// <remarks>
/// Correct for every pattern, which makes it both the engine for runtime-constructed instances and the
/// escape hatch for patterns the generator cannot emit a segment-native matcher for. It gives up the point
/// of the library - not copying the subject - so the generated path should handle anything it can.
/// </remarks>
internal sealed class FallbackSegEx : SegEx
{
    private readonly Regex _regex;
    // Group carries neither its number (never public) nor, on netstandard2.0, its name, so the numbering is
    // resolved once off the Regex rather than per match.
    private readonly int[] _groupNumbers;
    private readonly string[] _groupNames;

    internal FallbackSegEx(string pattern, RegexOptions options, TimeSpan matchTimeout)
        : base(pattern, options, matchTimeout)
    {
        _regex = new Regex(pattern, options, matchTimeout);

        _groupNumbers = _regex.GetGroupNumbers();
        Array.Sort(_groupNumbers);
        _groupNames = new string[_groupNumbers.Length];
        for (var i = 0; i < _groupNumbers.Length; i++)
        {
            _groupNames[i] = _regex.GroupNameFromNumber(_groupNumbers[i]);
        }
    }

    public override bool IsMatch(in ReadOnlySequence<char> input) => _regex.IsMatch(SequenceText.Materialize(input));

    public override SegExMatch Match(in ReadOnlySequence<char> input) => Translate(_regex.Match(SequenceText.Materialize(input)), input);

    public override SegExMatchCollection Matches(in ReadOnlySequence<char> input)
    {
        var matches = _regex.Matches(SequenceText.Materialize(input));
        if (matches.Count == 0)
        {
            return SegExMatchCollection.Empty;
        }

        var translated = new SegExMatch[matches.Count];
        var i = 0;
        foreach (Match match in matches)
        {
            translated[i++] = Translate(match, input);
        }
        return new SegExMatchCollection(translated);
    }

    // Offsets carry over unchanged: the materialized string is a faithful copy of the sequence, so the
    // results can point back at the sequence and materialize their text lazily from it.
    private SegExMatch Translate(Match match, in ReadOnlySequence<char> subject)
    {
        if (!match.Success)
        {
            return SegExMatch.Failed(subject);
        }

        var bclGroups = match.Groups;
        var groups = new SegExGroup[_groupNumbers.Length];
        for (var i = 0; i < _groupNumbers.Length; i++)
        {
            var number = _groupNumbers[i];
            var group = bclGroups[number];
            groups[i] = new SegExGroup(subject, group.Index, group.Length, group.Success, number, _groupNames[i], TranslateCaptures(group, subject));
        }

        return new SegExMatch(subject, match.Index, match.Length, true, TranslateCaptures(match, subject), groups);
    }

    private static SegExCapture[] TranslateCaptures(Group group, in ReadOnlySequence<char> subject)
    {
        var captures = group.Captures;
        if (captures.Count == 0)
        {
            return [];
        }

        var translated = new SegExCapture[captures.Count];
        for (var i = 0; i < captures.Count; i++)
        {
            var capture = captures[i];
            translated[i] = new SegExCapture(subject, capture.Index, capture.Length);
        }
        return translated;
    }
}
