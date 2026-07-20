namespace SegmentedRegex.Tests;

/// <summary>
/// Checks the ported char-class matcher against the BCL's own, over every possible character.
/// This pins two things at once: that the port is faithful, and that the serialized set format the
/// vendored generator produces is still the format the runtime half expects.
/// </summary>
public class SegExCharClassTests
{
    /// <summary>
    /// Reaches <see cref="RegexRunner.CharInClass(char, string)"/>, which is protected static and so
    /// otherwise unreachable, by deriving from it. Never instantiated.
    /// </summary>
    private sealed class BclProbe : RegexRunner
    {
        internal static bool IsInClass(char ch, string set) => CharInClass(ch, set);

        protected override void Scan(ReadOnlySpan<char> text) => throw new NotSupportedException();
    }

    /// <summary>
    /// Builds a set string from raw code points. Written this way rather than as string literals
    /// because these are dense runs of control characters, where a literal is unreadable and at the
    /// mercy of file encoding.
    /// </summary>
    private static string Set(params int[] codePoints)
    {
        var chars = new char[codePoints.Length];
        for (var i = 0; i < codePoints.Length; i++)
        {
            chars[i] = (char)codePoints[i];
        }
        return new string(chars);
    }

    /// <summary>
    /// The well-known class strings, transcribed from the vendored RegexCharClass constants, plus a few
    /// hand-built plain ranges. Layout is [flags][setLength][categoryLength][set ranges][categories].
    /// </summary>
    public static TheoryData<string, string> Classes => new()
    {
        { @"\s", Set(0, 0, 1, 0x64) },
        { @"\S", Set(0, 0, 1, 0xFF9C) },
        { @"[^\s]", Set(1, 0, 1, 0x64) },
        { @"\w", Set(0, 0, 0x0A, 0, 2, 4, 5, 3, 1, 6, 9, 0x13, 0) },
        { @"\W", Set(0, 0, 0x0A, 0, 0xFFFE, 0xFFFC, 0xFFFB, 0xFFFD, 0xFFFF, 0xFFFA, 0xFFF7, 0xFFED, 0) },
        { @"[^\w]", Set(1, 0, 0x0A, 0, 2, 4, 5, 3, 1, 6, 9, 0x13, 0) },
        { @"\d", Set(0, 0, 1, 9) },
        { @"\D", Set(0, 0, 1, 0xFFF7) },
        { @"[^\d]", Set(1, 0, 1, 9) },
        { @"\p{Cc}", Set(0, 0, 1, 0x0F) },
        { @"\P{Cc}", Set(0, 0, 1, 0xFFF1) },
        { @"\p{L}", Set(0, 0, 7, 0, 2, 4, 5, 3, 1, 0) },
        { @"\P{L}", Set(0, 0, 7, 0, 0xFFFE, 0xFFFC, 0xFFFB, 0xFFFD, 0xFFFF, 0) },
        { @"[\p{L}\d]", Set(0, 0, 8, 0, 2, 4, 5, 3, 1, 0, 9) },
        { @"[^\p{L}\d]", Set(1, 0, 8, 0, 2, 4, 5, 3, 1, 0, 9) },
        { @"\p{Ll}", Set(0, 0, 1, 2) },
        { @"\P{Ll}", Set(0, 0, 1, 0xFFFE) },
        { @"\p{Lu}", Set(0, 0, 1, 1) },
        { @"\P{Lu}", Set(0, 0, 1, 0xFFFF) },
        { @"\p{N}", Set(0, 0, 5, 0, 9, 0x0A, 0x0B, 0) },
        { @"\P{N}", Set(0, 0, 5, 0, 0xFFF7, 0xFFF6, 0xFFF5, 0) },
        { @"\p{P}", Set(0, 0, 9, 0, 0x13, 0x14, 0x16, 0x19, 0x15, 0x18, 0x17, 0) },
        { @"\P{P}", Set(0, 0, 9, 0, 0xFFED, 0xFFEC, 0xFFEA, 0xFFE7, 0xFFEB, 0xFFE8, 0xFFE9, 0) },
        { @"\p{Z}", Set(0, 0, 5, 0, 0x0D, 0x0E, 0x0C, 0) },
        { @"\P{Z}", Set(0, 0, 5, 0, 0xFFF3, 0xFFF2, 0xFFF4, 0) },
        { @"\p{S}", Set(0, 0, 6, 0, 0x1B, 0x1C, 0x1A, 0x1D, 0) },
        { @"\P{S}", Set(0, 0, 6, 0, 0xFFE5, 0xFFE4, 0xFFE6, 0xFFE3, 0) },

        // Plain ranges: each range is an inclusive start and an exclusive end.
        { "[a-z]", Set(0, 2, 0, 'a', 'z' + 1) },
        { "[^a-z]", Set(1, 2, 0, 'a', 'z' + 1) },
        { "[a-c]", Set(0, 2, 0, 'a', 'c' + 1) },
        { "[a-cx-z]", Set(0, 4, 0, 'a', 'c' + 1, 'x', 'z' + 1) },
        { "[0-9a-fA-F]", Set(0, 6, 0, '0', '9' + 1, 'A', 'F' + 1, 'a', 'f' + 1) },
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public void AgreesWithTheBclForEveryCharacter(string name, string set)
    {
        for (var i = 0; i <= char.MaxValue; i++)
        {
            var ch = (char)i;
            var expected = BclProbe.IsInClass(ch, set);
            var actual = SegExCharClass.CharInClass(ch, set);

            if (expected != actual)
            {
                Assert.Fail($"Class {name} disagreed at U+{i:X4}: BCL said {expected}, port said {actual}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Classes))]
    public void AsciiCacheOverloadAgreesWithTheUncachedOne(string name, string set)
    {
        uint[] cache = null;

        // Twice over, so both the cold path that populates the cache and the warm path that reads it
        // back are compared against the uncached answer.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i <= char.MaxValue; i++)
            {
                var ch = (char)i;
                var expected = SegExCharClass.CharInClass(ch, set);
                var actual = SegExCharClass.CharInClass(ch, set, ref cache);

                if (expected != actual)
                {
                    Assert.Fail($"Class {name} cache pass {pass} disagreed at U+{i:X4}: expected {expected}, got {actual}.");
                }
            }
        }
    }
}
