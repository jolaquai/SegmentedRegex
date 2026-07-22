<#
.SYNOPSIS
  Vendors the .NET regex source generator + its shared source files from dotnet/runtime,
  pinned to a specific commit, with a diff-before-bump review step.

.PARAMETER Mode
  Check  - fetch latest upstream, diff the tracked files against the pinned SHA, report drift.
           Does not touch vendor/ or the pin file. Exit code: 0 = clean, 1 = drift found,
           2 = could not perform the check (network/git failure) - distinct from drift on
           purpose, so a build integration can treat "couldn't check" differently from
           "confirmed drift".
  Apply  - checkout the SHA in vendor/UPSTREAM_SHA.txt (or -Sha if given), copy the tracked
           files into vendor/dotnet-runtime/, update the pin file.

.PARAMETER Sha
  Only used with -Mode Apply. Moves the pin to this commit instead of the currently pinned one.
  Always run -Mode Check against this SHA first and read the diff before applying it.

.NOTES
  Uses a blobless partial clone (--filter=blob:none) + cone-mode sparse-checkout, so this pulls
  commit/tree metadata for the whole repo (cheap) but only the blobs for the paths below
  (also cheap) - not a multi-GB full clone, and not a submodule dragging the entire history in.

  Check mode also prints one MSBuild-canonical-format line per changed file
  ("<path> : error REGEXGENSYNC001: ...") so `Exec`'s own error-line scraping picks each one
  up individually in a build log, in addition to the exit code a wrapping MSBuild target
  should act on directly.
#>
param(
    [ValidateSet('Check', 'Apply')]
    [string]$Mode = 'Check',
    [string]$Sha
)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/dotnet/runtime.git'
$root = Split-Path -Parent $PSScriptRoot
$vendorDir = Join-Path $root 'vendor' 'dotnet-runtime'
$pinFile = Join-Path $vendorDir 'UPSTREAM_SHA.txt'
# $env:TEMP is a Windows-ism and is empty on Linux/macOS runners; GetTempPath() resolves
# TMPDIR/-tmp- correctly on every OS pwsh runs on.
$cacheClone = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-runtime-sparse-cache'

# cone-mode sparse-checkout works at directory granularity; this pulls a few extra files
# alongside the ones we vendor (e.g. Common/src/System has other unrelated helpers) - fine,
# we only copy the specific files listed below out of the sparse checkout.
$sparsePaths = @(
    'src/libraries/System.Text.RegularExpressions/gen'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions'
    'src/libraries/System.Text.RegularExpressions/src/System/Threading'
    'src/libraries/System.Text.RegularExpressions/src/System/Collections'
    'src/libraries/Common/src/Roslyn'
    'src/libraries/Common/src/System'
    'src/libraries/Common/src/Polyfills'
    'src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices'
    'src/libraries/System.Private.CoreLib/src/System/Diagnostics/CodeAnalysis'
)

# Files we have FORKED for the segmented retarget. They live in forked/, not vendor/, and are
# deliberately never copied over by -Mode Apply, because our edits to them are the whole point of
# the fork. They stay in $files below so -Mode Check still diffs them and tells us when the upstream
# original moves - that is a signal to re-merge by hand, not something the script can do for us.
$forked = @(
    'src/libraries/System.Text.RegularExpressions/gen/RegexGenerator.cs'
    'src/libraries/System.Text.RegularExpressions/gen/RegexGenerator.Parser.cs'
    'src/libraries/System.Text.RegularExpressions/gen/RegexGenerator.Emitter.cs'
)

# the exact files that get copied into vendor/ - everything else pulled by the sparse
# checkout above is scaffolding we don't need but that cone mode can't exclude at file level.
# Entries also listed in $forked are checked but not copied.
$files = @(
    'src/libraries/System.Text.RegularExpressions/gen/RegexGenerator.cs'
    'src/libraries/System.Text.RegularExpressions/gen/RegexGenerator.Parser.cs'
    'src/libraries/System.Text.RegularExpressions/gen/RegexGenerator.Emitter.cs'
    'src/libraries/System.Text.RegularExpressions/gen/DiagnosticDescriptors.cs'
    'src/libraries/System.Text.RegularExpressions/gen/Stubs.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexNode.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexNodeKind.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexTree.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexTreeAnalyzer.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexPrefixAnalyzer.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexFindOptimizations.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCharClass.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCharClass.Tables.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCaseBehavior.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCaseEquivalences.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexCaseEquivalences.Data.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexOpcode.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexOptions.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParseError.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParseException.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Threading/StackHelper.cs'
    'src/libraries/System.Text.RegularExpressions/src/System/Collections/HashtableExtensions.cs'
    'src/libraries/Common/src/Roslyn/DiagnosticDescriptorHelper.cs'
    'src/libraries/Common/src/Roslyn/GetBestTypeByMetadataName.cs'
    'src/libraries/Common/src/System/HexConverter.cs'
    'src/libraries/Common/src/System/Obsoletions.cs'
    'src/libraries/Common/src/System/Text/ValueStringBuilder.cs'
    'src/libraries/Common/src/System/Collections/Generic/ValueListBuilder.cs'
    'src/libraries/Common/src/System/Collections/Generic/ValueListBuilder.Pop.cs'
    'src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/IsExternalInit.cs'
    # Directory.Build.targets in the real repo auto-injects these into every non-.NETCoreApp
    # project (netstandard2.0 here included); outside the repo we have to vendor + wire them
    # ourselves. See the ItemGroups gated on SkipIncludeNullableAttributes/IncludeSpanPolyfills
    # in src/libraries/Directory.Build.targets.
    'src/libraries/System.Private.CoreLib/src/System/Diagnostics/CodeAnalysis/NullableAttributes.cs'
    'src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/CallerArgumentExpressionAttribute.cs'
    'src/libraries/Common/src/Polyfills/ExceptionPolyfills.cs'
    'src/libraries/Common/src/Polyfills/MemoryExtensionsPolyfills.cs'
    'src/libraries/Common/src/System/StringPolyfills.cs'
    'src/libraries/Common/src/System/Buffers/SearchValuesPolyfills.cs'
    'src/libraries/Common/src/System/Text/AsciiPolyfills.cs'
    'src/libraries/Common/src/Polyfills/EncodingPolyfills.cs'
)

function Test-CloneUsable {
    # The cache lives under %TEMP%, which cleanup tools purge - often leaving the directory tree behind
    # without a working repository. Testing for the directory alone would then take the fetch path below
    # and quietly operate on a non-repo, so probe the repository itself.
    if (-not (Test-Path $cacheClone)) { return $false }
    Push-Location $cacheClone
    try {
        git rev-parse --git-dir 2>$null | Out-Null
        return $LASTEXITCODE -eq 0
    }
    finally { Pop-Location }
}

# SourceGen is referenced (as an analyzer) by multiple TFM builds/projects, so MSBuild can invoke
# this script concurrently from separate processes. They'd otherwise race on the single shared
# $cacheClone path (two `git clone`s into the same not-yet-existing directory - the loser gets
# "destination path already exists" instead of a clean clone). A named Mutex serializes them.
$cacheLockName = 'SegmentedRegex-dotnet-runtime-sparse-cache-lock'

function Invoke-WithCacheLock {
    param([scriptblock]$Action)

    $mutex = New-Object System.Threading.Mutex($false, $cacheLockName)
    $acquired = $false
    try {
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(10))
        }
        catch [System.Threading.AbandonedMutexException] {
            # Previous holder crashed mid-clone without releasing - we still got ownership.
            # Test-CloneUsable (called from Ensure-SparseClone) detects and repairs a half-finished
            # clone, so it's safe to just proceed as the new owner.
            $acquired = $true
        }
        if (-not $acquired) {
            throw "timed out waiting for the dotnet-runtime-sparse-cache lock (held >10 min by another process)."
        }
        & $Action
    }
    finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Ensure-SparseClone {
    if (-not (Test-CloneUsable)) {
        if (Test-Path $cacheClone) {
            Write-Host "Cache at $cacheClone exists but is not a usable clone (temp cleanup?); recreating it."
            Remove-Item -Recurse -Force $cacheClone
        }
        Write-Host "Creating blobless partial clone (one-time, cheap - no blobs downloaded yet)..."
        git clone --filter=blob:none --no-checkout --sparse $repoUrl $cacheClone
        if ($LASTEXITCODE -ne 0) { throw "git clone of $repoUrl failed (exit $LASTEXITCODE)." }
        Push-Location $cacheClone
        git sparse-checkout init --cone
        Pop-Location
    }
    else {
        Push-Location $cacheClone
        git fetch origin main --quiet
        Pop-Location
    }
    # reassert every time (idempotent, cheap) so adding a file to $sparsePaths later just works
    # against an existing cache instead of silently missing it.
    Push-Location $cacheClone
    git sparse-checkout set @sparsePaths
    Pop-Location
}

function Get-CurrentPin {
    if (Test-Path $pinFile) {
        # -Raw yields $null for an empty file, so guard before trimming.
        $raw = Get-Content $pinFile -Raw
        if (-not [string]::IsNullOrWhiteSpace($raw)) { return $raw.Trim() }
    }
    return $null
}

function Get-UpstreamHead {
    $head = git rev-parse origin/main
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
        throw "could not resolve origin/main in the clone cache at $cacheClone. Delete that directory and re-run to re-clone."
    }
    return $head.Trim()
}

if ($Mode -eq 'Check') {
    # network/git failures here are a DIFFERENT outcome from "drift found" - a build integration
    # should be able to tell "I don't know" apart from "confirmed problem". exit 2, not 1.
    try {
        Invoke-WithCacheLock { Ensure-SparseClone }
    }
    catch {
        Write-Host "vendor/dotnet-runtime : error REGEXGENSYNC000: could not reach upstream to check for drift ($($_.Exception.Message))"
        exit 2
    }

    Push-Location $cacheClone
    try {
        $pinned = Get-CurrentPin
        if (-not $pinned) {
            Write-Host "$pinFile : error REGEXGENSYNC002: no pin recorded yet - run -Mode Apply -Sha <commit> first."
            exit 2
        }
        $latest = Get-UpstreamHead
        Write-Host "Pinned:  $pinned"
        Write-Host "Latest:  $latest"

        if ($pinned -eq $latest) {
            Write-Host "Up to date."
            exit 0
        }

        $changed = @()
        foreach ($f in $files) {
            $diff = git diff --stat "$pinned..$latest" -- $f
            if ($diff) { $changed += $f }
        }

        if ($changed.Count -eq 0) {
            Write-Host "Upstream moved ($pinned -> $latest) but none of the $($files.Count) tracked files changed."
            exit 0
        }

        foreach ($f in $changed) {
            # MSBuild canonical error format ("origin : error CODE: message") - Exec's own
            # error-line scraping surfaces each of these individually in the build log.
            if ($forked -contains $f) {
                Write-Host "$f : error REGEXGENSYNC003: changed upstream between pinned $pinned and latest $latest, and this file is FORKED in forked/ - -Mode Apply will not overwrite it, so re-merge the change by hand."
            }
            else {
                Write-Host "$f : error REGEXGENSYNC001: changed upstream between pinned $pinned and latest $latest"
            }
        }

        $forkedChanged = @($changed | Where-Object { $forked -contains $_ })
        Write-Host "`n$($changed.Count) of $($files.Count) tracked file(s) drifted from the pin."
        if ($forkedChanged.Count -gt 0) {
            Write-Host "$($forkedChanged.Count) of those is forked - -Mode Apply skips it, so the retarget must be re-merged manually."
        }
        Write-Host "Review: git -C `"$cacheClone`" diff $pinned..$latest -- <path>"
        Write-Host "Then:   scripts\Sync-RegexGenUpstream.ps1 -Mode Apply -Sha $latest"
        exit 1
    }
    catch {
        Write-Host "vendor/dotnet-runtime : error REGEXGENSYNC000: drift check failed ($($_.Exception.Message))"
        exit 2
    }
    finally {
        Pop-Location
    }
}
else {
    Invoke-WithCacheLock { Ensure-SparseClone }
    Push-Location $cacheClone
    try {
        $target = if ($Sha) { $Sha } else { Get-CurrentPin }
        if (-not $target) { throw "No pin recorded yet and no -Sha given. Pass -Sha <commit> the first time." }

        git checkout $target --quiet
        New-Item -ItemType Directory -Force $vendorDir | Out-Null

        $copied = 0
        foreach ($f in $files) {
            # Never clobber a forked file: -Mode Apply would silently destroy the retarget, and
            # -Mode Check compares upstream to upstream so it would not have warned us either.
            if ($forked -contains $f) { continue }

            $dest = Join-Path $vendorDir ($f -replace '^src/libraries/', '')
            New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
            Copy-Item (Join-Path $cacheClone $f) $dest -Force
            $copied++
        }

        Set-Content $pinFile $target -NoNewline
        Write-Host "Vendored $copied files from dotnet/runtime@$target into $vendorDir"
        Write-Host "Skipped $($forked.Count) forked file(s) in forked/ - re-merge those by hand if -Mode Check flagged them."
    }
    finally {
        Pop-Location
    }
}
