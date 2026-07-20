# SegEx v1 implementation plan

Grounded in a survey of the vendored emitter (2026-07-20). Key structural fact: the emitted shell
is `file sealed class X : Regex` + `Runner : RegexRunner`, and the emitted body depends on runner
machinery (`base.runtextpos`/`runtextstart`, `Capture`/`Uncapture`/`TransferCapture`/`Crawlpos`/
`IsMatched`/`MatchIndex`/`MatchLength`, `base.runstack`, `CheckTimeout`) plus 7 static
`RegexRunner.CharInClass` call sites. `RegexRunner.Scan` is span-only and `Match` is string-backed,
so a segmented runner cannot subclass `Regex`/`RegexRunner`. The `SegEx` project therefore ships a
parallel runtime shell, and the generator is retargeted onto it.

## Locked decisions (2026-07-20)

- Subject type: `ReadOnlySequence<char>` is the public currency. `string`/`ReadOnlySpan<char>`/
  `char[]` convenience overloads wrap as a single segment.
- v1 API scope: `IsMatch` / `Match` / `Matches` with full group+capture info. `EnumerateMatches`,
  `Replace`, `Split`, `Count` deferred.
- Fallback: patterns the generator can't emit get a materialize-and-delegate engine (rent buffer,
  copy sequence, run standard `Regex`, translate results). Runtime-constructed `SegEx` (no source
  gen) exists in v1 and always uses this engine.
- `RegexOptions.RightToLeft`: descoped; routes to the fallback engine. Removes the
  `LastIndexOf`/backwards-iteration reader surface from v1.
- Positions: flat `int` offsets into the sequence. Subjects longer than `int.MaxValue` chars are
  rejected. `long`/`SequencePosition`-native addressing deferred.
- TFM support line: the generated fast path requires net8.0+ consumers (emitted code uses
  `SearchValues` etc., same as upstream's own floor). netstandard2.0 consumers get the fallback
  engine only. Fix `SegEx.csproj`'s `net8.0-windows` leftover to plain `net8.0` (consider adding
  `net10.0` later).

## The input-op surface to retarget

Everything the emitted code does to the input (from the emitter survey):
`Length`, `IsEmpty`, indexer, `Slice(start[, length])`, `IndexOf(char|string)`, `IndexOfAny`,
`IndexOfAnyExcept`, `IndexOfAnyInRange`, `IndexOfAnyExceptInRange`, `StartsWith`, `SequenceEqual`
(backreferences), `SearchValues<char>`/`SearchValues<string>` helpers, plus the
`Utilities.*` helpers that take `inputSpan` (`IsBoundary`, `IsWordChar`, custom `IndexOfAny`
helpers, `IndexOfStrings_LeftToRight`). RTL-only ops (`LastIndexOf`, `IndexOfString_RightToLeft`)
are out of scope per the RTL descope.

## Phase 1: SegEx runtime core (no vendor edits, standalone buildable)

1. Reader ref struct (working name `SegmentedSpan`) over `ReadOnlySequence<char>`:
   - Mirrors the op surface above 1:1 so the emitter retarget stays mechanical type substitution
     (keeps upstream merges cheap - the whole point of the drift machinery).
   - Cached current-chunk span + its flat start offset; indexer/ops hit the cached chunk fast path
     and only re-seek on boundary cross.
   - Single-segment subjects collapse to genuine span ops (contiguous perf parity target).
   - `Slice` returns an adjusted-bounds copy (cheap struct copy, no re-walk from origin).
   - Multi-char ops (`IndexOf(string)`, `StartsWith`, `SequenceEqual`) run per-chunk vectorized with
     boundary stitching; single-char ops (`IndexOfAny*`) never need stitching.
2. `SegExRunner` base: port the capture/crawl stack machinery from `RegexRunner` (self-contained
   int-array logic, input-independent), `runtextpos`/`runtextstart` state, timeout plumbing
   (`CheckTimeout`, `REGEX_DEFAULT_MATCH_TIMEOUT` AppContext handling stays in emitted helpers).
3. `CharInClass`: needed at runtime; `RegexRunner.CharInClass` is protected so not reachable from
   outside a `RegexRunner` subclass. Options (resolve during P1): (a) compile-include the already
   vendored `RegexCharClass.cs`+`Tables` into the runtime project (stays drift-checked for free;
   may need trimming of parse-side code), (b) hand-port only the matching subset.
4. Public types: `GeneratedSegExAttribute(pattern, options, timeoutMs)`; abstract `SegEx` base with
   `IsMatch`/`Match`/`Matches` over `ReadOnlySequence<char>` + convenience overloads; own
   `Match`/`Group`/`Capture` result types with flat `int` `Index`/`Length` and lazy text
   materialization. Naming collisions with `System.Text.RegularExpressions` and the
   namespace-equals-type-name question (`SegEx.SegEx`) to be settled at the start of P1.
5. Fallback engine: `ArrayPool<char>` rent + copy + standard `Regex` + result translation. This is
   also the implementation behind runtime-constructed `SegEx` and the differential-test oracle.

Exit criterion: package is usable and correct for any pattern (all via fallback), tested.

## Phase 2: generator retarget (vendor edits begin)

1. Trigger: recognize `SegEx.GeneratedSegExAttribute` only. Must coexist with the real BCL
   `[GeneratedRegex]` generator in the same consuming project: distinct generated namespace, helper
   class name, and hint names; never double-fire.
2. Parser (`RegexGenerator.Parser.cs`): attribute lookup, return type `SegEx`.
3. Shell emission: `EmitRegexPartialMethod` (return type), `EmitRegexLimitedBoilerplate` (emit a
   fallback-engine `SegEx` instead of `new Regex(...)`), `EmitRegexDerivedImplementation` +
   `EmitRegexDerivedTypeRunnerFactory` (base types `SegEx`/`SegExRunner`, capture-map init moved to
   the new base).
4. Body retarget: `ReadOnlySpan<char> inputSpan` -> reader type across `EmitScan`,
   `EmitTryFindNextPossibleStartingPosition`, `EmitTryMatchAtCurrentPosition`, and the
   `inputSpan`-taking `Utilities` helpers. Keep variable names and code shape identical to upstream
   wherever possible to minimize textual drift.
5. Unsupported-in-v1 detection (RTL, anything else discovered) routes to limited-boilerplate
   fallback emission, not a hard diagnostic.

Exit criterion: generated path compiles and passes the same differential suite as the fallback.

## Phase 3: differential test suite

- New `SegEx.Tests` project: xunit.v3, MTP (`dotnet test`), no VSTest flags.
- Oracle: standard `Regex` on the contiguous subject. Compare full match/group/capture structure.
- Matrix: pattern corpus (hand-picked constructs covering every emitter path: literals, sets,
  loops lazy/greedy/atomic, alternation, backrefs, anchors, boundaries, lookarounds, captures,
  case-insensitivity, timeout) x subjects x segmentations (exhaustive split points for short
  subjects, randomized multi-split for long ones, empty segments included).
- Generator-level tests: Roslyn `GeneratorDriver` snapshot/compile tests that build the emitted
  code and run it in-process.
- CI already runs `dotnet test` on the solution; no workflow changes needed.

## Phase 4: perf

- `SegEx.Benchmarks` project (BenchmarkDotNet, excluded from `dotnet test`).
- Targets, in order: (1) single-segment generated path within noise of upstream `[GeneratedRegex]`,
  (2) multi-segment overhead quantified per construct, (3) fallback materialization cost curve.
- Only then micro-optimize the reader (boundary-cross paths, `[SkipLocalsInit]`, etc.).

## Deferred (explicitly not v1)

`EnumerateMatches` (allocation-free result model), `Replace`/`Split`/`Count`, RTL,
`long`/`SequencePosition` addressing, `RegexOptions.NonBacktracking` (upstream generator doesn't
support it either), polyfilled generated path for netstandard2.0 consumers.
