using System.Collections;

namespace SegmentedRegex;

/// <summary>
/// The matches produced by a single scan of a subject, in the order they were found.
/// </summary>
public sealed class SegExMatchCollection : IReadOnlyList<SegExMatch>
{
    internal static readonly SegExMatchCollection Empty = new SegExMatchCollection([]);

    private readonly SegExMatch[] _matches;

    internal SegExMatchCollection(SegExMatch[] matches) => _matches = matches;

    /// <summary>The number of matches.</summary>
    public int Count => _matches.Length;

    /// <summary>Gets the match at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based position in the collection.</param>
    /// <returns>The match at that position.</returns>
    public SegExMatch this[int index] => _matches[index];

    /// <summary>Returns an allocation-free enumerator over the matches.</summary>
    /// <returns>The enumerator.</returns>
    public SegExEnumerator<SegExMatch> GetEnumerator() => new SegExEnumerator<SegExMatch>(_matches);

    IEnumerator<SegExMatch> IEnumerable<SegExMatch>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
