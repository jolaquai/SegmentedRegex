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

## Matching a `StringBuilder`

A `StringBuilder` already stores its text as a chain of chunks, so it is matched in place - no copy:

```csharp
var sb = new StringBuilder();
// ... build up a large document ...

var match = MyPattern().Match(sb);   // matched over the builder's own chunks
match.Groups["year"].Value;
```

`Regex` has no non-contiguous input, so the same thing costs a full materialization first. And
`Regex.Match`/`Matches` are string-only - the span overloads are `IsMatch`, `Count` and
`EnumerateMatches`, none of which give you capture groups - so getting groups out of a builder forces
`sb.ToString()`.

Over a 256K-character builder that works out to roughly 9-14x faster with ~240x less allocation than
`ToString()`, and still faster than renting a buffer and copying into it by hand. Below ~16K
characters the copy is cheap enough that a rented buffer wins instead; see
[the benchmarks](SegmentedRegex.Benchmarks/README.md) for the full picture, including where this
engine is slower.

The sequence views the builder's live buffers, so it is valid only while the builder is not modified.

## Contribution

Contact me on Discord @ `eyeoftheenemy` or open an issue here if you have any questions, suggestions or want to contribute!
