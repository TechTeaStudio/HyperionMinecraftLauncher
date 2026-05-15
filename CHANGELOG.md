# Changelog

All notable changes to this project are documented here.
Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.0] - 2026-05-15

### Added
- Read-only support for Mojang's `launcher_profiles.json`. The Installations page is no longer a stub - it now lists every profile from the official launcher with name, type, version id, JVM args, and last-used time, and selecting one pre-fills the Home page's installed-version picker (when the profile's `lastVersionId` matches something on disk).
- `Core/Profiles/` folder:
  - `LauncherProfile` DTO carrying the well-known fields (`Key`, `Name`, `Type`, `Created`, `LastUsed`, `LastVersionId`, `Icon`, `GameDir`, `JavaDir`, `JavaArgs`, `ResolutionWidth`, `ResolutionHeight`).
  - `LauncherProfilesFile` DTO (profiles list + schema version).
  - `ILauncherProfilesStore` interface (read-only).
  - `FileLauncherProfilesStore` JSON implementation tolerant of unknown fields and missing optional keys.
- `IMinecraftLauncherService.ListProfilesAsync(...)` wires the store into the service; the App's view-model owns a `Profiles` observable collection and a `RefreshProfilesCommand`.
- 4 new tests (`LauncherProfilesStoreTests`): missing file returns empty, realistic modern file parses every field, no-profiles-key returns empty, missing-optional-fields produces sensible nulls.

### Changed
- `CmlLibMinecraftLauncherService` constructor gains an optional `ILauncherProfilesStore` parameter (defaults to `FileLauncherProfilesStore`).
- We deliberately do **not** write into Mojang's `launcher_profiles.json` - any future Hyperion-owned profile state will live in a separate file. The Installations page calls out the read-only stance.
- `<Version>` bumped to `0.6.0` in both shipping csproj files.

## [0.5.0] - 2026-05-15

### Added
- Full UI overhaul: 220 px sidebar (Home / Installations / Skins / Servers / News / Settings) + account chip header + content area, replacing the cramped single-row pre-v0.5 layout that overlapped the "Refresh versions" and "Launch" buttons. Window grew to 1040x640.
- `MainViewModel` extended with account state, installed-version selection, sidebar selection, and three new commands: `SignInMicrosoftCommand`, `SignOutCommand`, `RefreshInstalledVersionsCommand`. Home page now picks between (a) installed versions list (no install step) and (b) manifest versions (downloads + installs first). Microsoft-signed-in sessions skip the offline-auth call and flow straight to launch.
- Material Icons throughout the chrome (Minecraft, HomeOutline, Cube, TshirtCrew, Server, Newspaper, Cog, AccountCircle), pixel `Minecraft` font on titles + Launch button, banner-texture accents from the resource pack on the Installed / Manifest version rows.
- Placeholders for Installations / Skins / Servers / News / Settings pages with the Material Icon + Minecraft font + the planned scope - each will graduate to a real feature in a later release.
- 8 new view-model tests covering: account-chip defaults, sidebar selection booleans, `RefreshInstalledVersionsCommand` populating `InstalledVersions`, `LaunchCommand.CanExecute` becoming true on installed-version selection alone, `SignInMicrosoftCommand` flipping the account state, `SignOutCommand` calling the auth provider, signed-in `LaunchAsync` skipping the offline-auth call, and installed-version IDs taking precedence over manifest names in the launch path.

### Changed
- `MainViewModel` constructor now optionally accepts `IMicrosoftAuthService` (used by the sign-out command); the App's manual DI passes it through. Existing tests that omit it continue to work.
- `StubLauncherService` in tests now mirrors the real service's `AuthMode -> IsOffline` mapping so view-model tests against signed-in flows are realistic.
- `<Version>` bumped to `0.5.0` in both shipping csproj files.

### Fixed
- The "text overlap" bug from the v0.4 screenshot (the version dropdown crushed by the Refresh + Launch buttons in a 6-column row) is gone - each control now sits in its own card with predictable vertical spacing.

## [0.4.0] - 2026-05-15

### Added
- `IMinecraftLauncherService.ListInstalledVersionsAsync` enumerates versions present on disk under the resolved `.minecraft` directory. The view-model can now show what's already installed (no manifest fetch needed) and combine it with the remote manifest for an "installed + installable" picker.
- New `Core/Installations/` folder:
  - `IMinecraftInstallationLocator` + `DefaultMinecraftInstallationLocator` - resolve the platform-default `.minecraft` path. Windows: `%APPDATA%\.minecraft`; macOS: `~/Library/Application Support/minecraft` (no dot, no `.minecraft` suffix - the Mojang convention); Linux: `~/.minecraft` (XDG is intentionally ignored).
  - `MinecraftInstallation` record - paths to `versions/`, `launcher_profiles.json`, `launcher_accounts.json`, `servers.dat`.
  - `IInstalledVersionScanner` + `FileSystemInstalledVersionScanner` - read `versions/<id>/<id>.json` and produce `InstalledVersion` DTOs. Sorted newest-first; malformed manifests are skipped silently.
  - `LoaderDetector` + `ModLoader` enum - classify Forge / NeoForge / Fabric / Quilt / OptiFine / LegacyForge based on `mainClass`, `inheritsFrom`, and id naming conventions.
- 13 new tests across `InstalledVersionScannerTests` and `LoaderDetectorTests` covering vanilla, Forge, NeoForge, Fabric, Quilt, OptiFine, LegacyForge, malformed manifests, missing folders, and sort order.

### Changed
- `CmlLibMinecraftLauncherService` constructor now takes optional `IInstalledVersionScanner` + `IMinecraftInstallationLocator` (defaults wire the real implementations).
- `<Version>` bumped to `0.4.0` in both shipping csproj files.

## [0.3.0] - 2026-05-15

### Added
- Real Microsoft account sign-in via `CmlLib.Core.Auth.Microsoft 3.3.1` (Java Edition: Microsoft OAuth -> Xbox Live -> XSTS -> Minecraft Services -> profile fetch). On Windows the default flow uses WebView2; the account cache is stored at `%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.json` (separate from the official launcher's `launcher_accounts.json` to avoid stepping on each other).
- `IMicrosoftAuthService` in `HyperionMinecraftLauncher.Core` (interface; portable). Concrete `MicrosoftAuthService` in `HyperionMinecraftLauncher.App` wraps `JELoginHandler` and translates exceptions into the existing `AuthenticationFailedException`. Tests substitute a `FakeMicrosoftAuthService` so the cross-platform Core test suite never pulls in WebView2.
- `CmlLibMinecraftLauncherService` now takes an optional `IMicrosoftAuthService` and delegates the `AuthMode.Microsoft` branch to it. Without an injected provider, the Microsoft branch still throws a friendly "not configured" message (so headless test contexts work unchanged).

### Changed
- `<Version>` bumped to `0.3.0` in both `HyperionMinecraftLauncher.Core.csproj` and `HyperionMinecraftLauncher.App.csproj`.
- `AuthenticateAsync` no longer rejects an empty username for `AuthMode.Microsoft` (the OAuth-issued profile supplies it). Offline mode still requires one.

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
