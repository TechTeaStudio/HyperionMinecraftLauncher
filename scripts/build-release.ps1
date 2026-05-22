#requires -Version 5.1
<#
.SYNOPSIS
    Local equivalent of the GitHub Releases pipeline. Publishes the launcher into
    outputs/ at the repo root.

.DESCRIPTION
    Mirrors what .github/workflows/release.yml produces on a `v*.*.*` tag push, with
    one platform caveat: Linux .AppImage wrapping and macOS .app + .dmg packaging
    only work on their native hosts (appimagetool / sips / iconutil / hdiutil are
    not on Windows). When this script runs on Windows it therefore:

      * Produces the full Windows artifact - self-contained single-file publish +
        the .zip CI would upload to the GitHub Release.
      * Cross-compiles the requested non-Windows RIDs to a raw self-contained
        publish folder. Those folders are runnable as-is on the matching OS but
        are not the polished .AppImage / .app.zip artifacts CI ships.

    Run on macOS or Linux (via pwsh) to additionally invoke the matching
    bash packaging scripts (build-appimage.sh / build-macos-app.sh /
    build-macos-dmg.sh) so the full CI shape is reproduced.

.PARAMETER Rids
    Which runtime identifiers to publish. Defaults to @('win-x64'). Special value
    'All' expands to the full CI matrix: win-x64, linux-x64, osx-x64, osx-arm64.

.PARAMETER Clean
    Wipe outputs/ before publishing. Default $false - subsequent runs reuse the
    incremental obj/ cache, only the destination folder is overwritten per-RID.

.EXAMPLE
    .\scripts\build-release.ps1
    Single-RID Windows build. Produces:
      outputs/HyperionMinecraftLauncher-<ver>-win-x64/
      outputs/HyperionMinecraftLauncher-<ver>-win-x64.zip

.EXAMPLE
    .\scripts\build-release.ps1 -Rids All -Clean
    Full CI matrix from a clean outputs/. ~500 MB of artifacts when complete.

.NOTES
    No publish-time signing is wired up - GitHub Actions doesn't sign either.
    The .zip CI uploads is a Compress-Archive of the single-file publish folder,
    which is exactly what this script also produces.
#>
param(
    [string[]] $Rids = @('win-x64'),
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'

# Resolve repo root from this script's location so the script works whether the
# user runs it via .\scripts\build-release.ps1 or via an absolute path.
$repoRoot = (Get-Item (Join-Path $PSScriptRoot '..')).FullName
$version  = (Get-Content -Path (Join-Path $repoRoot 'VERSION') -Raw).Trim()
$outputs  = Join-Path $repoRoot 'outputs'
$project  = Join-Path $repoRoot 'src/HyperionMinecraftLauncher.App'

if ($Rids -contains 'All') {
    $Rids = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')
}

Write-Host ""
Write-Host ">> Hyperion release build  version=$version  RIDs=$($Rids -join ', ')"
Write-Host ">> Repo root: $repoRoot"
Write-Host ">> Outputs:   $outputs"
Write-Host ""

if ($Clean -and (Test-Path $outputs)) {
    Write-Host ">> Cleaning $outputs ..."
    Remove-Item -Path $outputs -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputs | Out-Null

foreach ($rid in $Rids) {
    $outDir = Join-Path $outputs "HyperionMinecraftLauncher-$version-$rid"

    Write-Host ""
    Write-Host "================================================================"
    Write-Host ">> Publishing $rid -> $outDir"
    Write-Host "================================================================"

    # --self-contained -p:PublishSingleFile=true matches the CI Windows step in
    # release.yml line 78-91. Other RIDs are cross-compiled with the same flags
    # so what falls out is the same shape the matching CI runner would produce
    # before its OS-specific packaging step.
    dotnet publish $project `
        -r $rid `
        -c Release `
        --self-contained `
        -p:PublishSingleFile=true `
        -o $outDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit $LASTEXITCODE)."
    }

    # Windows: zip the publish folder. This is the .zip CI uploads as the release
    # asset, byte-for-byte equivalent of the Compress-Archive in release.yml:91.
    if ($rid -eq 'win-x64') {
        $zipPath = Join-Path $outputs "HyperionMinecraftLauncher-$version-win-x64.zip"
        if (Test-Path $zipPath) { Remove-Item -Path $zipPath -Force }
        Write-Host ""
        Write-Host ">> Packaging $zipPath ..."
        Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zipPath -Force
    }
    elseif ($rid -eq 'linux-x64' -and $IsLinux) {
        # If we're actually on Linux and appimagetool is on PATH, finish the job.
        # On Windows this branch never enters, so we silently leave the raw publish.
        if (Get-Command appimagetool -ErrorAction SilentlyContinue) {
            Write-Host ""
            Write-Host ">> Wrapping into .AppImage via scripts/build-appimage.sh ..."
            bash (Join-Path $repoRoot 'scripts/build-appimage.sh')
        } else {
            Write-Host ""
            Write-Host "[info] appimagetool not on PATH - skipping AppImage wrap. The raw"
            Write-Host "       publish folder is runnable; install appimagetool to get a .AppImage."
        }
    }
    elseif (($rid -eq 'osx-x64' -or $rid -eq 'osx-arm64') -and $IsMacOS) {
        # On macOS the .app + .dmg flow is two scripts (app first, then dmg uses the app).
        # Run both only after we've published both architectures - controlled at the end of
        # this foreach by checking that both osx-* outputs exist on disk.
        $haveX64   = Test-Path (Join-Path $outputs "HyperionMinecraftLauncher-$version-osx-x64")
        $haveArm64 = Test-Path (Join-Path $outputs "HyperionMinecraftLauncher-$version-osx-arm64")
        if ($haveX64 -and $haveArm64) {
            Write-Host ""
            Write-Host ">> Wrapping into .app + .dmg via scripts/build-macos-{app,dmg}.sh ..."
            bash (Join-Path $repoRoot 'scripts/build-macos-app.sh')
            bash (Join-Path $repoRoot 'scripts/build-macos-dmg.sh')
        }
    }
}

Write-Host ""
Write-Host "================================================================"
Write-Host ">> Done. Artifacts under $outputs :"
Write-Host "================================================================"
Get-ChildItem -Path $outputs | Sort-Object Name | Format-Table @{
    Label = 'Name'; Expression = { $_.Name }
}, @{
    Label = 'Size'; Expression = {
        if ($_.PSIsContainer) {
            $sz = (Get-ChildItem -Path $_.FullName -Recurse -File -ErrorAction SilentlyContinue |
                   Measure-Object -Property Length -Sum).Sum
        } else { $sz = $_.Length }
        if ($null -eq $sz) { '-' }
        elseif ($sz -ge 1MB) { '{0:N1} MB' -f ($sz / 1MB) }
        elseif ($sz -ge 1KB) { '{0:N1} KB' -f ($sz / 1KB) }
        else { "$sz B" }
    }
}, LastWriteTime

Write-Host ""
Write-Host ">> Done."
