# Benchmarks

Phase 4 of [CLAUDE.v1-plan.md](../CLAUDE.v1-plan.md). BenchmarkDotNet console app, in the solution so
CI compile-checks it, but `IsTestProject=false` so `dotnet test` skips it.

```powershell
dotnet run -c Release --project SegmentedRegex.Benchmarks                                  # everything
dotnet run -c Release --project SegmentedRegex.Benchmarks -- --filter *ContiguousParity*    # one class
```

Release is required - BenchmarkDotNet refuses to produce meaningful numbers from a Debug build.

The classes map onto the plan's three targets:

- `ContiguousParityBenchmarks` - target 1: is the generated path on a single-segment subject within
  noise of upstream `[GeneratedRegex]`? Both engines are generated from the same pattern, and setup
  asserts they find the **same** match, so the ratio compares equal work.
- `SegmentationOverheadBenchmarks` - target 2: same pattern and characters, only the chunk count
  varies, so the delta is purely the reader's boundary handling.
- `FallbackCostBenchmarks` - target 3: the fallback's materialize-and-delegate cost against subject
  length and chunk count.

Filler text deliberately contains no character that starts any of the patterns' matches. Otherwise the
benchmark measures how well each engine rejects false starts rather than how fast it scans.

## Baseline (2026-07-21)

ShortRun, AMD Ryzen 9 7900X, .NET 10.0.10, X64 RyuJIT x86-64-v4. Absolute numbers are machine
specific; the ratios are the point. **This is a first measurement, not a target that has been met.**

### Target 1: contiguous vs upstream `[GeneratedRegex]`, 4096-char subject

| Pattern | `Regex.IsMatch` | `SegEx.IsMatch` | Ratio |
|---|---:|---:|---:|
| `needle` | 151 ns | 174 ns | **1.15x** |
| `(\w+)@(\w+)\.com` | 1,926 ns | 3,354 ns | 1.74x |
| `\d+` | 91 ns | 311 ns | 3.40x |
| `(\w)\1` | 34,435 ns | 387,422 ns | **11.25x** |
| `cat\|dog\|bird` | 6,229 ns | 249 ns | **0.04x** |

Parity holds only for the leading-literal case, which is dominated by one vectorized `IndexOf`. Cost
tracks how much work happens **per character**: patterns that walk the subject one character at a time
through the reader's indexer (`(\w)\1`, `\d+`) pay the most, because every character goes through a
bounds check and chunk-cache probe instead of a raw span index.

`cat|dog|bird` being 25x *faster* is real, not a measurement error - the fairness check confirms both
engines find the same match. Our fork routes multi-string alternation through
`IndexOfAny(SearchValues<string>)`; the BCL's generated matcher for this pattern evidently does not.

### Target 2: segmentation overhead, 4096-char subject, `IsMatch`

| Pattern | 1 chunk | 2 | 16 | 256 |
|---|---:|---:|---:|---:|
| `needle` | 181 ns | 237 ns | 917 ns | 13,473 ns |
| `cat\|dog\|bird` | 258 ns | 309 ns | 1,034 ns | 13,794 ns |
| `\d+` | 284 ns | 309 ns | 627 ns | 5,310 ns |
| `(\w+)@(\w+)\.com` | 3,504 ns | 3,502 ns | 4,270 ns | 108,359 ns |
| `(\w)\1` | 401 us | 395 us | 694 us | 6,258 us |

Overhead tracks boundary crossings rather than segmentation as such. Splitting in two is nearly free.
At 16 chunks (256 chars each) the cost is 1.2x-5x. At 256 chunks the chunks are 16 characters - below
the width the vectorized primitives need to amortise - and every operation degenerates, costing
16x-75x. The practical read: chunk size matters far more than chunk count, and chunks should stay well
above vector width.

### Target 3: fallback materialization curve, `\d+`

| Length | Chunks | Generated | Fallback | Ratio | Fallback alloc |
|---|---:|---:|---:|---:|---:|
| 256 | 1 | 97 ns | 224 ns | 2.32x | 0 B |
| 256 | 64 | 1,066 ns | 483 ns | **0.45x** | 536 B |
| 4,096 | 1 | 309 ns | 2,918 ns | 9.45x | 0 B |
| 4,096 | 64 | 1,885 ns | 3,615 ns | 1.92x | 8,216 B |
| 65,536 | 1 | 4,412 ns | 45,210 ns | 10.25x | 0 B |
| 65,536 | 64 | 5,039 ns | 85,289 ns | 16.93x | 131,138 B |

Two things worth noting. The contiguous rows allocate **nothing**: a single segment spanning a whole
string is handed back by `MemoryMarshal.TryGetString` with no copy, so `SequenceText.Materialize`
short-circuits. And at 256 chars in 64 chunks the fallback is *faster* than the segment-native path -
copying 256 characters is trivial next to a reader degenerating on 4-character chunks.

The fallback's cost is also not mostly copying: `SegEx.Create` builds an **interpreted** `Regex`, so
even the zero-copy contiguous rows run ~10x slower than a source-generated matcher.

### Matching a `StringBuilder`

The scenario the library exists for. A builder already stores its text as a chain of chunks, so
`sb.AsSequence()` views them with no copying; `Regex` has no non-contiguous input and must flatten the
builder first. Three ways to do it, `IsMatch`, same content:

| Pattern | Length | `sb.ToString()` | rent + `CopyTo` | `SegEx` over chunks |
|---|---:|---:|---:|---:|
| `\d+` | 1,024 | 94 ns / 2,072 B | **56 ns / 0 B** | 136 ns / 144 B |
| `\d+` | 16,384 | 1,211 ns / 32,792 B | 706 ns / 0 B | **572 ns / 384 B** |
| `\d+` | 262,144 | 90,377 ns / 524,480 B | 12,530 ns / 0 B | **6,584 ns / 2,184 B** |
| `needle` | 1,024 | 107 ns / 2,072 B | **69 ns / 0 B** | 189 ns / 144 B |
| `needle` | 16,384 | 1,413 ns / 32,792 B | **910 ns / 0 B** | 1,018 ns / 384 B |
| `needle` | 262,144 | 94,298 ns / 524,480 B | 17,305 ns / 0 B | **10,805 ns / 2,184 B** |

The crossover is around 16K characters. Below it the copy is too cheap to matter and our per-character
overhead dominates, so a rented buffer wins. Above it not copying wins outright: at 256K we are 8.7x
to 13.7x faster than `ToString()`, and still **1.6x to 1.9x faster than a hand-rolled rent-and-copy**,
which is the interesting part - the copy costs more than our entire scan disadvantage.

Allocation is not zero: `AsSequence()` creates one small link object per builder chunk, which is the
144 B - 2,184 B above (240x below `ToString()`, but above a pooled buffer's zero). A caller that holds
the sequence and matches repeatedly against an unchanged builder pays it once instead of per call.

There is also a capability difference, not just a speed one. Confirmed against the shipped surface:
`Regex.Match` and `Regex.Matches` are **string-only**; every span overload is `IsMatch`, `Count` or
`EnumerateMatches`, and `ValueMatch` carries just an index and a length. So the zero-allocation rented
path cannot give you capture groups at all - needing groups from a builder forces the `ToString()`
row, at 90 us and half a megabyte. `SegEx` returns full groups and captures straight off the chunks:

```csharp
// Regex: must materialize the whole builder to get groups
var m = MyRegex().Match(sb.ToString());

// SegEx: matched in place
var m = MySegEx().Match(sb);
```

## What this says about the next optimization pass

In rough order of value:

1. The per-character read path (`SegmentedSpan` indexer and `Slice`) is what makes `(\w)\1` 11x and
   `\d+` 3.4x. That is the single biggest lever.
2. Small-chunk degeneration - worth checking whether operations can consume a chunk at a time rather
   than re-probing the cache per character.
3. `RegexOptions.Compiled` for the fallback engine would recover much of its ~10x, at the cost of JIT
   time on first use. A judgement call, not an obvious win.
