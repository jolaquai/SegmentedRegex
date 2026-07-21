namespace SegmentedRegex;

/// <summary>
/// A link in a <see cref="ReadOnlySequence{T}"/> chain built from existing buffers.
/// </summary>
internal sealed class MemorySegment : ReadOnlySequenceSegment<char>
{
    internal MemorySegment(ReadOnlyMemory<char> memory) => Memory = memory;

    internal MemorySegment Append(ReadOnlyMemory<char> memory)
    {
        var next = new MemorySegment(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = next;
        return next;
    }
}
