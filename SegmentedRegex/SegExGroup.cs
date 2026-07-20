namespace SegmentedRegex;

/// <summary>
/// The result of a single capturing group. <see cref="SegExCapture.Index"/>, <see cref="SegExCapture.Length"/>
/// and <see cref="SegExCapture.Value"/> describe the group's last capture; earlier ones are in <see cref="Captures"/>.
/// </summary>
public class SegExGroup : SegExCapture
{
    internal SegExGroup(
        in ReadOnlySequence<char> subject,
        int index,
        int length,
        bool success,
        int number,
        string name,
        SegExCapture[] captures)
        : base(subject, index, length)
    {
        Success = success;
        Number = number;
        Name = name;
        Captures = new SegExCaptureCollection(captures);
    }

    /// <summary>Whether this group participated in the match.</summary>
    public bool Success { get; }

    /// <summary>The group's number within the pattern.</summary>
    public int Number { get; }

    /// <summary>The group's name, or its number as text when the group is unnamed.</summary>
    public string Name { get; }

    /// <summary>Every capture this group made, in the order they were captured.</summary>
    public SegExCaptureCollection Captures { get; }

    internal static SegExGroup Failed(in ReadOnlySequence<char> subject, int number, string name) => new SegExGroup(subject, 0, 0, false, number, name, []);
}
