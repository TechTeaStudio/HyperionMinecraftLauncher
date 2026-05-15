# Changelog

All notable changes to this project are documented here.
Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-05-15

### Added
- `IMinecraftLauncherService` contract with `ListVersionsAsync`, `AuthenticateAsync`, `LaunchAsync`.
- `CmlLibMinecraftLauncherService` - CmlLib.Core 4.0.6 adapter; offline auth via `MSession.CreateOfflineSession`, version listing + install + process start.
- `IUnderlyingLauncher` test shim and `CmlLibUnderlyingLauncher` production implementation so unit tests never touch the network, real Minecraft installs, or Java.
- Friendly `LauncherException` hierarchy (`VersionNotFoundException`, `InstallationFailedException`, `GameProcessStartException`, `AuthenticationFailedException`) with mapping from CmlLib's own exception types.
- `FileLauncherLogger` - daily-rotated file logger to `%LOCALAPPDATA%/HyperionMinecraftLauncher/logs/launcher-YYYY-MM-DD.log` (platform path resolved via `DefaultLogDirectory`).
- Minimal Avalonia desktop app: single `MainWindow` (username field, version dropdown, Refresh + Launch buttons, scrollable log textbox), `MainViewModel`, hand-rolled `AsyncRelayCommand`, manual constructor DI in `App.axaml.cs`.
- xUnit test suite covering CmlLib service exception mapping, file logger rotation, and view-model commands. UI is smoke-tested manually (manual test plan in README).
- `Directory.Build.props`, `dotnet.yml` CI (restore + build + test on push / PR to `product`), README, LICENSE, `.gitignore`, project `CLAUDE.md`, and `PLAN.md` documenting the three-worker partition.

### Known limitations
- Microsoft and Mojang auth modes throw `AuthenticationFailedException`. Real Microsoft auth needs an Azure-app Client ID; tracked for v0.2. Mojang username+password was discontinued upstream in 2022.
- Avalonia 11.2.3 pulls a transitive `Tmds.DBus.Protocol` 0.20.0 with a known vulnerability (NU1903). Upstream fix tracked; bump Avalonia in v0.2.
