using System.Text.RegularExpressions;
using SegmentedRegex;

namespace NetStandardConsumer;

/// <summary>
/// netstandard2.0 gets the fallback engine only: the segment-native runtime types live in *.net.cs and
/// are excluded from that lib leg. The generator still fires here, so it has to notice and emit the
/// fallback boilerplate rather than a GeneratedSegEx-derived type that cannot compile.
/// </summary>
public static partial class Usage
{
    [GeneratedSegEx(@"(\d+)")]
    public static partial SegEx DigitRun();

    public static bool Works() => DigitRun().IsMatch("abc123");
}
