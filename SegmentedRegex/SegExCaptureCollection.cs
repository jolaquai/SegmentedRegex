using System.Collections;

namespace SegmentedRegex;

/// <summary>
/// The captures made by a single group, in capture order.
/// </summary>
public sealed class SegExCaptureCollection : IReadOnlyList<SegExCapture>
{
    private readonly SegExCapture[] _captures;

    internal SegExCaptureCollection(SegExCapture[] captures) => _captures = captures;

    /// <summary>The number of captures.</summary>
    public int Count => _captures.Length;

    /// <summary>Gets the capture at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based position in the collection.</param>
    /// <returns>The capture at that position.</returns>
    public SegExCapture this[int index] => _captures[index];

    /// <summary>Returns an allocation-free enumerator over the captures.</summary>
    /// <returns>The enumerator.</returns>
    public SegExEnumerator<SegExCapture> GetEnumerator() => new SegExEnumerator<SegExCapture>(_captures);

    IEnumerator<SegExCapture> IEnumerable<SegExCapture>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
