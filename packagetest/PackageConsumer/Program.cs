using System.Buffers;
using System.Text.RegularExpressions;
using SegmentedRegex;

namespace PackageConsumer;

/// <summary>
/// Consumes SegmentedRegex as a real NuGet package and checks that the source generator ran from
/// analyzers/dotnet/cs/, produced segment-native matchers, and that they work over a genuinely
/// non-contiguous subject.
/// </summary>
internal static partial class Program
{
    [GeneratedSegEx(@"(?<y>\d{4})-(?<m>\d{2})")]
    private static partial SegEx DatePattern();

    [GeneratedSegEx("cat|dog|bird")]
    private static partial SegEx Animals();

    [GeneratedSegEx("ABC", RegexOptions.IgnoreCase)]
    private static partial SegEx CaseInsensitive();

    // RightToLeft cannot be code-generated, so this must land on the fallback engine and still work.
    [GeneratedSegEx("ab", RegexOptions.RightToLeft)]
    private static partial SegEx RightToLeft();

    private static int Main()
    {
        var failures = new List<string>();

        void Check(string name, bool condition, string detail = null)
        {
            if (condition)
            {
                Console.WriteLine($"  PASS  {name}");
            }
            else
            {
                Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $" ({detail})")}");
                failures.Add(name);
            }
        }

        Console.WriteLine("SegmentedRegex package consumption test");
        Console.WriteLine();

        // The partial methods compiling at all proves the packaged analyzer ran.
        var date = DatePattern();
        Check("generator ran from the package", date is not null);
        Check("date pattern is segment-native", date is GeneratedSegEx, date?.GetType().BaseType?.Name);
        Check("multi-string alternation is segment-native", Animals() is GeneratedSegEx);
        Check("ignore-case literal is segment-native", CaseInsensitive() is GeneratedSegEx);
        Check("right-to-left routes to the fallback", RightToLeft() is not GeneratedSegEx);

        // Split so the match, and the groups inside it, straddle segment boundaries.
        var segmented = Segments("on 20", "", "24-0", "5 x");
        var match = date.Match(segmented);
        Check("match across segment boundaries", match.Success);
        Check("value materializes across boundaries", match.Value == "2024-05", match.Value);
        Check("named group y", match.Groups["y"].Value == "2024", match.Groups["y"].Value);
        Check("named group m", match.Groups["m"].Value == "05", match.Groups["m"].Value);
        Check("index is a flat offset", match.Index == 3, match.Index.ToString());

        var animals = Animals().Matches(Segments("a do", "g and a c", "at"));
        Check("alternation finds both matches", animals.Count == 2, animals.Count.ToString());
        Check("alternation values", animals.Count == 2 && animals[0].Value == "dog" && animals[1].Value == "cat");

        Check("ignore-case matches across boundaries", CaseInsensitive().IsMatch(Segments("xxa", "bc", "xx")));
        Check("fallback engine still matches", RightToLeft().Match(Segments("ab x ", "ab")).Index == 5);

        // The runtime-constructed path, which never involves the generator.
        Check("runtime-constructed SegEx works", SegEx.Create(@"\w+").Match(Segments("  he", "llo  ")).Value == "hello");

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("ALL CHECKS PASSED");
            return 0;
        }

        Console.WriteLine($"{failures.Count} CHECK(S) FAILED: {string.Join(", ", failures)}");
        return 1;
    }

    /// <summary>Chains the parts into one non-contiguous sequence, preserving empty segments.</summary>
    private static ReadOnlySequence<char> Segments(params string[] parts)
    {
        var first = new Segment(parts[0].AsMemory());
        var last = first;
        for (var i = 1; i < parts.Length; i++)
        {
            last = last.Append(parts[i].AsMemory());
        }
        return new ReadOnlySequence<char>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<char>
    {
        internal Segment(ReadOnlyMemory<char> memory) => Memory = memory;

        internal Segment Append(ReadOnlyMemory<char> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
