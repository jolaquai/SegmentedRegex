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
$vendorDir = Join-Path $root 'vendor\dotnet-runtime'
$pinFile = Join-Path $vendorDir 'UPSTREAM_SHA.txt'
$cacheClone = Join-Path $env:TEMP 'dotnet-runtime-sparse-cache'

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
    'src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices'
)

# the exact files that get copied into vendor/ - everything else pulled by the sparse
# checkout above is scaffolding we don't need but that cone mode can't exclude at file level.
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
)

function Ensure-SparseClone {
    if (-not (Test-Path $cacheClone)) {
        Write-Host "Creating blobless partial clone (one-time, cheap - no blobs downloaded yet)..."
        git clone --filter=blob:none --no-checkout --sparse $repoUrl $cacheClone
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
    if (Test-Path $pinFile) { return (Get-Content $pinFile -Raw).Trim() }
    return $null
}

if ($Mode -eq 'Check') {
    # network/git failures here are a DIFFERENT outcome from "drift found" - a build integration
    # should be able to tell "I don't know" apart from "confirmed problem". exit 2, not 1.
    try {
        Ensure-SparseClone
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
        $latest = (git rev-parse origin/main).Trim()
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
            Write-Host "$f : error REGEXGENSYNC001: changed upstream between pinned $pinned and latest $latest"
        }
        Write-Host "`n$($changed.Count) of $($files.Count) vendored file(s) drifted from the pin."
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
    Ensure-SparseClone
    Push-Location $cacheClone
    try {
        $target = if ($Sha) { $Sha } else { Get-CurrentPin }
        if (-not $target) { throw "No pin recorded yet and no -Sha given. Pass -Sha <commit> the first time." }

        git checkout $target --quiet
        New-Item -ItemType Directory -Force $vendorDir | Out-Null

        foreach ($f in $files) {
            $dest = Join-Path $vendorDir ($f -replace '^src/libraries/', '')
            New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
            Copy-Item (Join-Path $cacheClone $f) $dest -Force
        }

        Set-Content $pinFile $target -NoNewline
        Write-Host "Vendored $($files.Count) files from dotnet/runtime@$target into $vendorDir"
    }
    finally {
        Pop-Location
    }
}
