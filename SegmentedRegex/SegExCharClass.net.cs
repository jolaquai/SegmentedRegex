using System.Globalization;

namespace SegmentedRegex;

/// <summary>
/// Decides whether a character belongs to a character class, given the class's serialized string form.
/// The generated matcher calls this the way upstream's generated code calls
/// <c>RegexRunner.CharInClass</c>, which is <see langword="protected"/> and so out of reach here.
/// </summary>
/// <remarks>
/// <para>
/// The bodies below are copied verbatim from the matching half of the vendored
/// <c>RegexCharClass.cs</c> (upstream lines 1227-1452 at the pinned SHA, plus the constants at 36-44
/// and <c>IsNegated</c> at 1149). Only the namespace, the class wrapper, and the nullable annotations
/// differ, so diffing against upstream after a drift report stays mechanical.
/// </para>
/// <para>
/// Copied rather than compiled from <c>vendor/</c> directly: the other three quarters of that file is
/// parse-side code that is dead at runtime, and it does not compile here. It needs <c>SR</c>,
/// <c>ValueStringBuilder</c> and <c>RegexParseException</c>'s internal constructor, and at line 1585
/// uses a <c>string.Create</c> overload with a ref struct <c>TState</c> that requires net9.0+ (CS9244),
/// which would mean giving up the net8.0 floor to compile code that never runs. The serialized set
/// format still stays pinned to the generator that produces it, which is the point of not calling the
/// BCL's own implementation.
/// </para>
/// </remarks>
public static class SegExCharClass
{
    internal const int FlagsIndex = 0;
    internal const int SetLengthIndex = 1;
    internal const int CategoryLengthIndex = 2;
    internal const int SetStartIndex = 3; // must be odd for subsequent logic to work

    internal const short SpaceConst = 100;
    private const short NotSpaceConst = -100;

    internal static bool IsNegated(string set, int setOffset) => set[FlagsIndex + setOffset] == 1;

    /// <summary>Determines a character's membership in a character class.</summary>
    /// <param name="ch">The character.</param>
    /// <param name="set">The string representation of the character class.</param>
    /// <returns><see langword="true"/> if the character is in the class.</returns>
    public static bool CharInClass(char ch, string set) => CharInClassIterative(ch, set, 0);

    /// <summary>Determines a character's membership in a character class.</summary>
    /// <param name="ch">The character.</param>
    /// <param name="set">The string representation of the character class.</param>
    /// <param name="asciiLazyCache">A lazily-populated cache for ASCII results stored in a 256-bit array.</param>
    /// <returns><see langword="true"/> if the character is in the class.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CharInClass(char ch, string set, ref uint[] asciiLazyCache)
    {
        // The uint[] contains 8 ints, or 256 bits.  These are laid out as pairs, where the first bit in the pair
        // says whether the second bit in the pair has already been computed.  Once a value is computed, it's never
        // changed, so since Int32s are written/read atomically, we can trust the value bit if we see that the known bit
        // has been set.  If the known bit hasn't been set, then we proceed to look it up, and then swap in the result.
        const int CacheArrayLength = 8;
        Debug.Assert(asciiLazyCache is null || asciiLazyCache.Length == CacheArrayLength, "set lookup should be able to store two bits for each of the first 128 characters");

        // If the value is ASCII and already has an answer for this value, use it.
        if (asciiLazyCache is uint[] cache)
        {
            var index = ch >> 4;
            if ((uint)index < (uint)cache.Length)
            {
                Debug.Assert(ch < 128);
                var current = cache[index];
                var bit = 1u << ((ch & 0xF) << 1);
                if ((current & bit) != 0)
                {
                    return (current & (bit << 1)) != 0;
                }
            }
        }

        // For ASCII, lazily initialize. For non-ASCII, just compute the value.
        return ch < 128 ?
            InitializeValue(ch, set, ref asciiLazyCache) :
            CharInClassIterative(ch, set, 0);

        static bool InitializeValue(char ch, string set, ref uint[] asciiLazyCache)
        {
            // (After warm-up, we should find ourselves rarely getting here.)
            Debug.Assert(ch < 128);

            // Compute the result and determine which bits to write back to the array and "or" the bits back in a thread-safe manner.
            var isInClass = CharInClass(ch, set);
            var bitsToSet = 1u << ((ch & 0xF) << 1);
            if (isInClass)
            {
                bitsToSet |= bitsToSet << 1;
            }

            var cache = asciiLazyCache ?? Interlocked.CompareExchange(ref asciiLazyCache, new uint[CacheArrayLength], null) ?? asciiLazyCache;
            Interlocked.Or(ref cache[ch >> 4], bitsToSet);

            // Return the computed value.
            return isInClass;
        }
    }

    private static bool CharInClassIterative(char ch, string set, int start)
    {
        var inClass = false;

        while (true)
        {
            int setLength = set[start + SetLengthIndex];
            int categoryLength = set[start + CategoryLengthIndex];
            var endPosition = start + SetStartIndex + setLength + categoryLength;

            if (CharInClassInternal(ch, set, start, setLength, categoryLength) == IsNegated(set, start))
            {
                break;
            }

            inClass = !inClass;

            if (set.Length <= endPosition)
            {
                break;
            }

            start = endPosition;
        }

        return inClass;
    }

    private static bool CharInClassInternal(char ch, string set, int start, int setLength, int categoryLength)
    {
        var min = start + SetStartIndex;
        var max = min + setLength;

        while (min != max)
        {
            var mid = (min + max) >> 1;
            if (ch < set[mid])
            {
                max = mid;
            }
            else
            {
                min = mid + 1;
            }
        }

        // The starting position of the set within the character class determines
        // whether what an odd or even ending position means.  If the start is odd,
        // an *even* ending position means the character was in the set.  With recursive
        // subtractions in the mix, the starting position = start+SetStartIndex.  Since we know that
        // SetStartIndex is odd, we can simplify it out of the equation.  But if it changes we need to
        // reverse this check.
        Debug.Assert((SetStartIndex & 0x1) == 1, "If SetStartIndex is not odd, the calculation below this will be reversed");
        if ((min & 0x1) == (start & 0x1))
        {
            return true;
        }

        return categoryLength != 0 && CharInCategory(ch, set.AsSpan(SetStartIndex + start + setLength, categoryLength));
    }

    private static bool CharInCategory(char ch, ReadOnlySpan<char> categorySetSegment)
    {
        var chcategory = char.GetUnicodeCategory(ch);

        for (var i = 0; i < categorySetSegment.Length; i++)
        {
            int curcat = (short)categorySetSegment[i];

            if (curcat == 0)
            {
                // zero is our marker for a group of categories - treated as a unit
                if (CharInCategoryGroup(chcategory, categorySetSegment, ref i))
                {
                    return true;
                }
            }
            else if (curcat > 0)
            {
                // greater than zero is a positive case

                if (curcat == SpaceConst)
                {
                    if (char.IsWhiteSpace(ch))
                    {
                        return true;
                    }
                }
                else if (chcategory == (UnicodeCategory)(curcat - 1))
                {
                    return true;
                }
            }
            else
            {
                // less than zero is a negative case
                if (curcat == NotSpaceConst)
                {
                    if (!char.IsWhiteSpace(ch))
                    {
                        return true;
                    }
                }
                else if (chcategory != (UnicodeCategory)(-1 - curcat))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// This is used for categories which are composed of other categories - L, N, Z, W...
    /// These groups need special treatment when they are negated
    /// </summary>
    private static bool CharInCategoryGroup(UnicodeCategory chcategory, ReadOnlySpan<char> category, ref int i)
    {
        var pos = i + 1;
        int curcat = (short)category[pos];
        bool result;

        if (curcat > 0)
        {
            // positive case - the character must be in ANY of the categories in the group
            result = false;
            do
            {
                result |= chcategory == (UnicodeCategory)(curcat - 1);
                curcat = (short)category[++pos];
            }
            while (curcat != 0);
        }
        else
        {
            // negative case - the character must be in NONE of the categories in the group
            Debug.Assert(curcat < 0);
            result = true;
            do
            {
                result &= chcategory != (UnicodeCategory)(-1 - curcat);
                curcat = (short)category[++pos];
            }
            while (curcat != 0);
        }

        i = pos;
        return result;
    }
}
