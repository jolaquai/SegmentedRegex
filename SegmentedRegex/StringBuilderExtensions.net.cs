namespace SegmentedRegex;

/// <summary>
/// Bridges <see cref="StringBuilder"/> onto the sequence API.
/// </summary>
public static class StringBuilderExtensions
{
    /// <summary>
    /// Views the builder's contents as a <see cref="ReadOnlySequence{T}"/> without copying them.
    /// </summary>
    /// <param name="builder">The builder to view.</param>
    /// <returns>A sequence over the builder's existing chunks.</returns>
    /// <remarks>
    /// <para>
    /// A <see cref="StringBuilder"/> already stores its text as a chain of chunks, which is the shape
    /// this library matches over, so the two line up exactly: no character is copied and nothing is
    /// allocated beyond one small link object per chunk. Matching a builder with
    /// <see cref="Regex"/> instead requires materializing the whole thing first.
    /// </para>
    /// <para>
    /// The sequence views the builder's live buffers, so it - and any match retaining it - is only
    /// valid while the builder is not modified. Appending to or clearing the builder afterwards leaves
    /// the sequence pointing at buffers the builder may have replaced or reused, and any
    /// <see cref="SegExCapture.Value"/> not yet materialized would then read the wrong text.
    /// </para>
    /// </remarks>
    public static ReadOnlySequence<char> AsSequence(this StringBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }
        if (builder.Length == 0)
        {
            return ReadOnlySequence<char>.Empty;
        }

        MemorySegment first = null;
        MemorySegment last = null;
        foreach (var chunk in builder.GetChunks())
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            if (first is null)
            {
                first = new MemorySegment(chunk);
                last = first;
            }
            else
            {
                last = last.Append(chunk);
            }
        }

        return first is null ? ReadOnlySequence<char>.Empty : new ReadOnlySequence<char>(first, 0, last, last.Memory.Length);
    }
}
