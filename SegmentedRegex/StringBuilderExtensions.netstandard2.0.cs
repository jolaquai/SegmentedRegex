namespace SegmentedRegex;

/// <summary>
/// Bridges <see cref="StringBuilder"/> onto the sequence API.
/// </summary>
public static class StringBuilderExtensions
{
    /// <summary>
    /// Views the builder's contents as a <see cref="ReadOnlySequence{T}"/>.
    /// </summary>
    /// <param name="builder">The builder to view.</param>
    /// <returns>A single-segment sequence over the builder's contents.</returns>
    /// <remarks>
    /// netstandard2.0 has no <c>StringBuilder.GetChunks()</c>, so this copies. The modern target
    /// framework views the builder's existing chunks without copying; this overload exists so the API
    /// is uniform, not because it is cheap. netstandard2.0 gets the fallback matching engine anyway,
    /// which materializes the subject regardless.
    /// </remarks>
    public static ReadOnlySequence<char> AsSequence(this StringBuilder builder) => builder is null
        ? throw new ArgumentNullException(nameof(builder))
        : builder.Length == 0
            ? ReadOnlySequence<char>.Empty
            : new ReadOnlySequence<char>(builder.ToString().AsMemory());
}
