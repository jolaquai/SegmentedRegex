# SegEx.SourceGen: vendored regex generator

This project is a fork of .NET's own `[GeneratedRegex]` source generator
(`System.Text.RegularExpressions.Generator`, MIT-licensed, from
[dotnet/runtime](https://github.com/dotnet/runtime)). Forked rather than referenced because the
goal is to retarget its emission templates from `ReadOnlySpan<char>` to a chunked/segmented reader,
which requires editing the emitter itself, not consuming it as a NuGet package.

## Why fork the generator instead of the compiled package

- `System.Text.RegularExpressions.Regex` has exactly one input path
  (`RegexRunner.Scan(ReadOnlySpan<char>)`) with no non-contiguous overload anywhere on the public or
  internal surface, and no runner-extension point that helps (`RegexRunnerFactory` replaces the
  *matcher*, not the input model). Confirmed via reflection against the shipped assembly.
- The generator's own `.csproj` compiles the actual parser/AST (`RegexParser`, `RegexNode`,
  `RegexTree`, `RegexTreeAnalyzer`, ...) directly into itself as **linked source**, not a binary
  reference — so it never goes through reflection, and forking it inherits full compile-time AST
  access for free.
- The emitter (`RegexGenerator.Emitter.cs`, ~5900 lines) touches the input through a small, closed
  set of operations (`.Slice()`, indexer, `.Length`, `.IsEmpty`, `.IndexOf()`/`.IndexOfAny()`/
  `.IndexOfAnyExcept()`/`.StartsWith()`) — roughly 49 call sites. Retargeting those onto a chunked
  reader is a mechanical, one-time change to the template, not a per-pattern hand-port.

## Layout

```
SegEx.SourceGen/
  SegEx.SourceGen.csproj
  scripts/
    Sync-RegexGenUpstream.ps1   <- vendoring + drift-check tool (see below)
  vendor/
    dotnet-runtime/
      UPSTREAM_SHA.txt          <- the pinned commit; source of truth for what "vendored" means
      System.Text.RegularExpressions/gen/...   <- the generator itself
      System.Text.RegularExpressions/src/...   <- parser/AST/char-class/etc.
      Common/src/...                            <- shared helpers + polyfills
      System.Private.CoreLib/src/...            <- nullable attrs, CallerArgumentExpression, IsExternalInit
```

39 files total, all real vendored source at the pinned commit, plus two **hand-written** files that
are not vendored (see "Hand-written files" below).

## Forked files vs vendored files

Two different things live here, and the distinction is load-bearing:

- **`vendor/`** is a pristine mirror of dotnet/runtime at the pinned commit. Never hand-edit anything
  in here (except the two documented hand-written files below). `-Mode Apply` overwrites all of it.
- **`forked/`** holds the three files the segmented retarget actually modifies:
  `RegexGenerator.cs`, `RegexGenerator.Parser.cs`, `RegexGenerator.Emitter.cs`. These are *ours*. Edit
  them freely.

`-Mode Apply` **never** copies over anything in `forked/`. Without that carve-out it would silently
destroy the entire retarget, and `-Mode Check` would not have warned you either, because Check
compares upstream-at-the-pin against upstream-at-main - it never looks at the local copies at all.

`-Mode Check` still diffs the forked files against upstream, and reports them as `REGEXGENSYNC003`
rather than `001`, meaning: upstream changed a file you have forked, so re-merge that change into
`forked/` by hand. Nothing automates that, by design.

Both the move into `forked/` and the retarget edits are separate commits, so
`git log --follow forked/<file>` shows exactly what diverges from pristine upstream.

## Staying in sync with upstream

`scripts/Sync-RegexGenUpstream.ps1` is the only supported way to touch `vendor/`. It never does a
full clone of dotnet/runtime (multi-GB monorepo) — it uses a blobless partial clone
(`--filter=blob:none`) plus cone-mode sparse-checkout, cached once in `%TEMP%\dotnet-runtime-sparse-cache`
and reused across runs.

```powershell
# see what changed upstream since the pin, without touching anything
.\scripts\Sync-RegexGenUpstream.ps1 -Mode Check

# after reviewing the diff, move the pin and re-vendor
.\scripts\Sync-RegexGenUpstream.ps1 -Mode Apply -Sha <new-commit-sha>
```

The clone cache lives under `%TEMP%`, so cleanup tools purge it periodically - often leaving the
directory behind without a working repository. The script probes the repo (not just the directory)
and re-clones when it finds a husk, so that heals itself; it used to surface as a confusing
`REGEXGENSYNC000: You cannot call a method on a null-valued expression`.

`-Mode Check` diffs only the ~39 tracked files (not "did anything in the repo change") between the
pinned SHA and `origin/main`, and exits:
- **0** — clean, either no upstream movement or upstream moved but none of the tracked files did.
- **1** — confirmed drift. Prints one `<path> : error REGEXGENSYNC001: ...` line per changed file
  (MSBuild-canonical format, see below) plus a summary and the exact `git diff`/`-Mode Apply`
  commands to review and apply it.
- **2** — couldn't check at all (network/git failure). Deliberately distinct from drift so a build
  integration can treat "don't know" differently from "confirmed problem".

To add a newly-needed vendored file: add its path to both `$sparsePaths` (directory-level, cone
mode) and `$files` (the exact file list that gets copied) near the top of the script, then re-run
`-Mode Apply` at the *current* pin (no `-Sha` needed) to backfill it without moving the pin.

## Build integration: drift fails the build

`SegEx.SourceGen.csproj` has a target, `_UpstreamSourceUpToDateCheck`, that runs `-Mode Check`
automatically and fails the build on confirmed drift, **before** `CoreCompile` wastes time
compiling against sources that may have already changed upstream.

**Gotcha worth knowing if you ever touch this target**: it's hooked with
`BeforeTargets="CoreCompile"`, not `BeforeTargets="Build"`. `BeforeTargets="Build"` was tried first
and verified, empirically, to never fire for this SDK's `dotnet build` invocation (an unconditional
`<Message>` under it never printed, while the identical hook on `CoreCompile` did) — root cause not
fully chased down, but `CoreCompile` is arguably the better anchor anyway since it's exactly the
point right before wasted compilation would happen.

Properties:
- `-p:SkipUpstreamDriftCheck=true` — bypass entirely (fully offline work).
- `-p:UpstreamDriftCheckStrict=true|false` — controls whether "couldn't check" (exit code 2) is a
  hard error or a warning. Defaults to strict only when `$(ContinuousIntegrationBuild)` is true, so
  a flaky local connection warns instead of blocking the inner dev loop, while CI (which should
  always have network) fails loud if the check itself breaks.

All three behaviors (clean/drift/skip) were verified against real `dotnet build` runs, not just
reviewed as XML.

## Polyfills: why 8 extra files exist that aren't "the generator"

Inside dotnet/runtime, a repo-wide `src/libraries/Directory.Build.targets` auto-injects certain
files into every project that isn't targeting modern `.NETCoreApp` — invisible if you only look at
the generator's own `.csproj`. Outside the repo, netstandard2.0 doesn't have these types at all
(`SearchValues<T>`) or only has internal, cross-assembly-inaccessible versions of them
(`NotNullWhenAttribute`, `DisallowNullAttribute`, `MemberNotNullWhenAttribute` — hence the original
`CS0122: inaccessible due to protection level` errors). Vendored and wired in, gated the same way
upstream gates them (`Condition="'$(TargetFrameworkIdentifier)' != '.NETCoreApp'"`, so this stays
correct if a modern TFM is ever added alongside netstandard2.0):

- `NullableAttributes.cs` (from `System.Private.CoreLib`)
- `CallerArgumentExpressionAttribute.cs` (from `System.Private.CoreLib`)
- `ExceptionPolyfills.cs`, `MemoryExtensionsPolyfills.cs`, `StringPolyfills.cs`,
  `SearchValuesPolyfills.cs`, `AsciiPolyfills.cs`, `EncodingPolyfills.cs` (all from `Common/src`)

These 8 **are** tracked by the sync script and covered by drift-check like everything else.

## Hand-written files (NOT vendored, NOT covered by drift-check)

`vendor/dotnet-runtime/System.Text.RegularExpressions/gen/Resources/SR.cs` is hand-written, not
vendored, because the real `SR.cs` is emitted at *dotnet/runtime's own build time* by their
repo-internal `GenerateResxSourceTask` — that tooling itself isn't something worth vendoring for one
resource file. What's actually vendored: the real `Strings.resx` (embedded as a resource,
`LogicalName="SegEx.SourceGen.Strings.resources"`), so all 52 message strings are byte-faithful to
upstream. `SR.cs` just wraps `ResourceManager.GetString(key)` per resx entry plus the standard
`SR.Format(...)` overloads. It also defines a trivial marker type,
`FxResources.System.Text.RegularExpressions.Generator.SR`, that `DiagnosticDescriptors.cs` passes
via `typeof(...)` to `LocalizableResourceString` purely to identify which assembly the resources
live in — it does no work itself.

**Consequence**: if upstream ever renames or adds an `SR.XXX` key, `-Mode Check` will *not* catch
it (these two files aren't in `$files`). You'd find out at compile time instead — a clear, direct
`CS0117`-style error, not silent breakage, but the automated safety net doesn't extend here. If
`Strings.resx` itself changes, that *is* covered (it's a real vendored file); only new/renamed keys
referenced from vendored `.cs` files against an unchanged `SR.cs` wrapper would slip through.

## Known warnings (all benign, matched against upstream's own behavior)

- **`CS0436` (RegexOptions conflict)** — the vendored `RegexOptions.cs` conflicts with the
  `RegexOptions` already in netstandard2.0's own reference assembly (`netstandard.library`). Checked
  upstream's own `.csproj`: their `DefaultReferenceExclusion` for `System.Text.RegularExpressions`
  only applies to their modern-TFM build, not netstandard2.0 — meaning the real generator project
  accepts this exact same warning for netstandard2.0 too. Not a bug, not fixed here on purpose.
- **`CS8632`** (nullable annotations outside a `#nullable` context) — this project has
  `<Nullable>disable</Nullable>`; the vendored source assumes nullable is enabled. Cosmetic only.
  Real fix would be flipping the project to `<Nullable>enable</Nullable>`, not done here since that
  was an explicit existing project choice, not something to change as a side effect.
- **`CS8500`** — expected, given `AllowUnsafeBlocks` is already on.
- **`RS1036`** — genuine, cheap, not yet applied: add
  `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>` for analyzer/source-generator
  projects. Not added yet because it hasn't been verified to not surface a new round of diagnostics.

## Current state

- Pin: see `vendor/dotnet-runtime/UPSTREAM_SHA.txt`.
- Build: 0 errors, ~860 warnings (all four categories above, all benign/expected).
- Not yet started: retargeting the emitter's ~49 input-touching call sites onto a chunked reader
  (the actual point of this fork). Everything above is the vendoring/build-hygiene foundation for
  that work, not the work itself.
