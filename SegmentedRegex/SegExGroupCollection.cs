using System.Collections;
using System.Globalization;

namespace SegmentedRegex;

/// <summary>
/// The capturing groups of a match, ordered by group number.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="IReadOnlyList{T}"/>: the integer indexer looks a group up by its
/// <see cref="SegExGroup.Number"/> rather than by position, matching
/// <see cref="System.Text.RegularExpressions.GroupCollection"/>. The two coincide for every pattern that does
/// not renumber its groups explicitly, but they are not the same thing.
/// </remarks>
public sealed class SegExGroupCollection : IReadOnlyCollection<SegExGroup>
{
    private readonly SegExGroup[] _groups;
    private readonly ReadOnlySequence<char> _subject;
    // Group numbers are 0..n-1 unless the pattern renumbers explicitly, e.g. "(?<9>a)".
    private readonly bool _numbersAreDense;

    internal SegExGroupCollection(in ReadOnlySequence<char> subject, SegExGroup[] groups)
    {
        _subject = subject;
        _groups = groups;

        var dense = true;
        for (var i = 0; i < groups.Length; i++)
        {
            if (groups[i].Number != i)
            {
                dense = false;
                break;
            }
        }
        _numbersAreDense = dense;
    }

    /// <summary>The number of groups, including group 0 (the match itself).</summary>
    public int Count => _groups.Length;

    /// <summary>Gets the group with the given <paramref name="number"/>.</summary>
    /// <param name="number">The group number.</param>
    /// <returns>The group, or an unsuccessful group when the pattern has no such group.</returns>
    public SegExGroup this[int number]
    {
        get
        {
            if (_numbersAreDense)
            {
                return (uint)number < (uint)_groups.Length
                    ? _groups[number]
                    : SegExGroup.Failed(_subject, number, number.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var group in _groups)
            {
                if (group.Number == number)
                {
                    return group;
                }
            }
            return SegExGroup.Failed(_subject, number, number.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Gets the group with the given <paramref name="name"/>.</summary>
    /// <param name="name">The group name.</param>
    /// <returns>The group, or an unsuccessful group when the pattern has no such group.</returns>
    public SegExGroup this[string name]
    {
        get
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            foreach (var group in _groups)
            {
                if (string.Equals(group.Name, name, StringComparison.Ordinal))
                {
                    return group;
                }
            }
            return SegExGroup.Failed(_subject, 0, name);
        }
    }

    /// <summary>Returns an allocation-free enumerator over the groups, in group-number order.</summary>
    /// <returns>The enumerator.</returns>
    public SegExEnumerator<SegExGroup> GetEnumerator() => new SegExEnumerator<SegExGroup>(_groups);

    IEnumerator<SegExGroup> IEnumerable<SegExGroup>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
