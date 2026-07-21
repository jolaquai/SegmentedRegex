# Package consumption test

Consumes `SegmentedRegex` as a **real NuGet package** rather than a `ProjectReference`, so the source
generator loads from `analyzers/dotnet/cs/` the way a third party gets it. The main test suite drives
the generator in-process through a `GeneratorDriver`, which never exercises that path.

The references here are deliberately `PackageReference` and must stay that way - switching to
`ProjectReference` would defeat the entire point.

This is its own `.slnx` and is **not** part of `SegmentedRegex.slnx`, so `dotnet build`/`dotnet test`
at the repo root ignore it and CI is unaffected. It could not run in CI anyway: it needs a package
that has already been packed to a local feed.

## Layout

- `PackageConsumer` - net10.0 console app. Asserts the generator ran, that patterns come out
  segment-native (`GeneratedSegEx`), that RightToLeft routes to the fallback, and that matching, group
  capture and lazy value materialization all work across real segment boundaries. Exits non-zero on
  failure.
- `NetStandardConsumer` - netstandard2.0 class library, compile-only. That TFM gets the fallback engine
  (the segment-native types live in `*.net.cs` and are excluded from its lib leg), so this proves the
  generator notices and emits fallback boilerplate instead of a matcher that cannot compile there.
  `LangVersion latest` is load-bearing: netstandard2.0 otherwise defaults to C# 7.3, the generator
  declines on language version alone, and the TFM handling goes untested.
- `nuget.config` - adds the repo's gitignored `artifacts/` directory as a feed without clearing
  machine-level sources, so an already-registered local feed keeps working too.

## Running it

Pack from the repo root, then run both consumers:

```powershell
dotnet pack SegmentedRegex/SegmentedRegex.csproj -c Release -o artifacts -p:Version="0.1.0-e2e$(Get-Date -Format yyyyMMddHHmmss)"
dotnet build packagetest/NetStandardConsumer -c Release        # must succeed
dotnet run --project packagetest/PackageConsumer -c Release    # must print ALL CHECKS PASSED, exit 0
```

The `PackageReference`s float on `0.1.0-*`, so a freshly packed build is picked up without editing
anything; pin an exact one with `-p:SegmentedRegexPackageVersion=<version>`.

**Always pack a version you have not used before.** The global package cache is keyed by version and
will otherwise serve a stale copy, so you would be testing the wrong bits. `dotnet pack -c Debug`
self-versions through the build counter (`0.1.0-alphaNNN`) and is an alternative to passing
`-p:Version`. Release deliberately packs a clean `$(CoreVersion)` so the release workflow keeps
tagging exact versions.

## What this caught

Two bugs that in-process testing structurally could not find:

1. **netstandard2.0 consumers got a broken build.** The analyzer runs regardless of the consumer's
   target framework, so it emitted code referencing `GeneratedSegEx`, `SegExRunner`, `SegmentedSpan`
   and `SearchValues<>` - none of which exist in that lib leg - producing a wall of `CS0246` in a
   package that advertises netstandard2.0 support. Guarded now by
   `GeneratorTriggerTests.TargetFrameworksWithoutTheSegmentNativeRuntimeGetTheFallback`.
2. **The pack target shipped a stale generator.** `_PackSourceGen.targets` used `GetTargetPath`, which
   reports where an assembly would be without building it, so two packs straddling a generator fix
   produced byte-identical analyzer DLLs. It now runs `Build` first.
