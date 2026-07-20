# `SegmentedRegex`

Regex matching with familiar perf on segmented subjects.

(WIP)

```csharp
using SegmentedRegex;

// Any ReadOnlySequence<char> - here one spanning three chunks.
var subject = /* ReadOnlySequence<char> over "on 20" + "24-0" + "5 x" */;

var segex = SegEx.Create(@"(?<y>\d{4})-(?<m>\d{2})");
var match = segex.Match(subject);

match.Value;            // "2024-05", materialized lazily across the chunk boundaries
match.Groups["y"].Value; // "2024"
match.Index;             // 3, a flat offset into the sequence
```

`string`, `char[]`, and `ReadOnlyMemory<char>` overloads wrap as a single segment.

## Contribution

Contact me on Discord @ `eyeoftheenemy` or open an issue here if you have any questions, suggestions or want to contribute!
