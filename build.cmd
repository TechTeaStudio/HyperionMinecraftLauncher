@echo off
rem One-click local build for HyperionMinecraftLauncher (Windows).
rem
rem Does:
rem   1. Calls scripts\build-release.ps1 with the default -Rids win-x64.
rem   2. Produces a true single-file HyperionMinecraftLauncher.exe at the repo root.
rem   3. Also writes the full release artefact set under outputs\ (publish folder + .zip)
rem      that mirrors what the GitHub Releases CI pipeline uploads on a v*.*.* tag push.
rem
rem Prerequisites:
rem   - .NET SDK 10.0+ on PATH (`dotnet --info` to verify).
rem   - PowerShell 5.1+ (every supported Windows ships it). PowerShell 7 (`pwsh`) is used
rem     when present, otherwise we fall back to Windows PowerShell (`powershell.exe`).
rem
rem Pass-through:
rem   - Any extra arguments are forwarded to build-release.ps1, e.g.
rem       build.cmd -Rids All           Full CI matrix (win + linux + osx-x64 + osx-arm64)
rem       build.cmd -Clean              Wipe outputs\ before publishing.
rem       build.cmd -Rids win-x64 -Clean
rem
rem Behaviour on failure:
rem   The script exits with the PowerShell exit code and pauses so the message stays
rem   on screen when launched via double-click.

setlocal

set "REPO_ROOT=%~dp0"
set "BUILD_SCRIPT=%REPO_ROOT%scripts\build-release.ps1"

if not exist "%BUILD_SCRIPT%" (
    echo.
    echo [error] Expected build script not found:
    echo         %BUILD_SCRIPT%
    echo.
    pause
    exit /b 1
)

rem Prefer PowerShell 7 ("pwsh") for cleaner output and modern -File semantics; fall back
rem to Windows PowerShell on a clean Win10/11 install where pwsh is not preinstalled.
where pwsh >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%BUILD_SCRIPT%" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%BUILD_SCRIPT%" %*
)

set "PS_EXIT=%ERRORLEVEL%"

if %PS_EXIT% NEQ 0 (
    echo.
    echo Build failed with exit code %PS_EXIT%.
    pause
    exit /b %PS_EXIT%
)

echo.
pause
exit /b 0
