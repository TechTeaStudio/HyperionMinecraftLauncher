# Changelog

All notable changes to this project are documented here.
Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-05-15

### Added
- Material Design baseline via `Material.Avalonia 3.14.2` + `Material.Icons.Avalonia 2.4.1`. `App.axaml` now uses `<themes:MaterialTheme BaseTheme="Dark">` instead of `<FluentTheme/>`, with brush-resource overrides that retint the Material palette to a Minecraft look (grass-block green primary `#3F7E22`, XP-bar yellow secondary `#F7CA18`, dirt/stone/planks named brushes `McGrassBrush`/`McDirtBrush`/`McCobblestoneBrush`/`McPlanksBrush`).
- Minecraft pixel font (`IdreesInc/Minecraft-Font`, SIL OFL 1.1) bundled under `src/HyperionMinecraftLauncher.App/Assets/Fonts/`. Referenced as `{StaticResource MinecraftFont}` (resolves via `avares://HyperionMinecraftLauncher/Assets/Fonts#Minecraft`).
- Banner textures from the user-supplied resource pack (`small_simple_icons` variant, RGBA) copied into `src/HyperionMinecraftLauncher.App/Assets/Banners/` so subsequent UI work can reference them via `avares://`.
- `scripts/inspect_icons.py`, `scripts/inspect_all_textures.py`, `scripts/copy_banner_textures.py`, `scripts/download_minecraft_font.py` - throwaway one-off helpers used to inventory the resource pack and pull the font; left in-tree as reproducible setup.

### Changed
- `.gitignore` now excludes `/assets/` (the raw user-dropped resource pack at repo root - we copy what we use into `src/.../Assets/` and let the rest stay local).
- `<Version>` bumped to `0.2.0` in both `HyperionMinecraftLauncher.Core.csproj` and `HyperionMinecraftLauncher.App.csproj`.

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
