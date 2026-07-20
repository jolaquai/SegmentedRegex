namespace SegmentedRegex;

/// <summary>
/// The result of a single matching operation.
/// </summary>
public sealed class SegExMatch : SegExGroup
{
    internal SegExMatch(
        in ReadOnlySequence<char> subject,
        int index,
        int length,
        bool success,
        SegExCapture[] captures,
        SegExGroup[] groups)
        : base(subject, index, length, success, 0, "0", captures) => Groups = new SegExGroupCollection(subject, groups);

    /// <summary>The capturing groups of this match. Group 0 spans the match itself.</summary>
    public SegExGroupCollection Groups { get; }

    internal static SegExMatch Failed(in ReadOnlySequence<char> subject) => new SegExMatch(subject, 0, 0, false, [], []);
}
