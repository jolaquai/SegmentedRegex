namespace SegmentedRegex;

/// <summary>
/// A single substring captured by a matching operation, addressed as a flat offset into the subject.
/// </summary>
public class SegExCapture
{
    private readonly ReadOnlySequence<char> _subject;
    private string _value;

    internal SegExCapture(in ReadOnlySequence<char> subject, int index, int length)
    {
        _subject = subject;
        Index = index;
        Length = length;
    }

    /// <summary>The zero-based offset of this capture from the start of the subject.</summary>
    public int Index { get; }

    /// <summary>The length of this capture, in characters.</summary>
    public int Length { get; }

    /// <summary>
    /// The captured text. Materialized from the subject on first access and cached thereafter, so a match
    /// whose text is never read never pays for the copy. The subject must not be mutated in between.
    /// </summary>
    public string Value => _value ??= SequenceText.Materialize(_subject, Index, Length);

    /// <inheritdoc/>
    public override string ToString() => Value;
}
