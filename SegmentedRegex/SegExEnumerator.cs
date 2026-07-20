using System.Collections;

namespace SegmentedRegex;

/// <summary>
/// Allocation-free enumerator over the arrays backing the result collections.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public struct SegExEnumerator<T> : IEnumerator<T>
{
    private readonly T[] _items;
    private int _index;

    internal SegExEnumerator(T[] items)
    {
        _items = items;
        _index = -1;
    }

    /// <summary>The element at the current position.</summary>
    public readonly T Current => _items[_index];

    readonly object IEnumerator.Current => Current;

    /// <summary>Advances to the next element.</summary>
    /// <returns><see langword="true"/> if there was one.</returns>
    public bool MoveNext() => ++_index < _items.Length;

    /// <summary>Resets the enumerator to before the first element.</summary>
    public void Reset() => _index = -1;

    /// <summary>Does nothing; present to satisfy <see cref="IDisposable"/>.</summary>
    public readonly void Dispose()
    {
    }
}
