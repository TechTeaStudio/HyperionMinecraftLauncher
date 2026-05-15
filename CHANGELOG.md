# Changelog

All notable changes to this project are documented here.
Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.18.0] - 2026-05-15

### Added
- **Liquid-glass UI**. The main window now uses `TransparencyLevelHint="AcrylicBlur, Mica, Blur, None"` and a transparent background; an `ExperimentalAcrylicBorder` with a dark-green tint (`#0F1410` at 78% material opacity) paints behind everything for a frosted-glass look on Win11 / mica-capable platforms. Cards, sidebar paper, header, and dividers are overridden in `Window.Resources` to semi-transparent variants (~80% alpha) so the acrylic shows through; legacy OSes fall through to the flat-dark fallback automatically.

## [0.17.0] - 2026-05-15

### Added
- **Device-code modal dialog** when signing in with Microsoft. `DeviceCodeDialog` is a custom-chrome Window (matches the main launcher) that shows the verification URL on top, then the user-code as a huge 34pt bold monospace block with extra letter-spacing so it's actually readable. Buttons: **Copy code** (writes to clipboard via Avalonia 11's `Window.Clipboard`), **Open browser** (re-opens the verification URL via `Process.Start`), **Hide** (closes manually). Auto-closes when sign-in completes - `MainWindow` listens for `MainViewModel.HasSession` flipping true and calls `Close()` on the open dialog.
- `MainViewModel.DeviceCodeRequested` event is now re-raised on the UI thread (already was, but now also subscribed by `MainWindow` to open the modal instead of only logging).

## [0.16.0] - 2026-05-15

### Added
- **Auto-refresh on startup**: `MainViewModel.RunStartupRefreshesAsync()` walks through Installed versions / launcher_profiles / servers / news / version manifest in sequence right after construction, so the launcher lands fully populated instead of with five empty cards waiting for the user to click "Refresh". App fires it via `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` so the first paint isn't blocked.

## [0.15.0] - 2026-05-15

### Added
- **3D player-skin viewer** on the Skins page (was a stub). `SkinViewer3D : Avalonia.Controls.Control` renders the full Minecraft player model - head, body, both arms, both legs - as 6 textured cuboids, drawn via `DrawingContext.Custom(...)` + `ISkiaSharpApiLeaseFeature` on the live Skia canvas. Mouse drag rotates the model (yaw + pitch). Pixels stay crisp (`SKFilterQuality.None`).
- Implementation:
  - World transform built with `SKMatrix44` (yaw -> pitch -> perspective via `[3,2] = -1/depth`).
  - Each of the 36 faces gets a local matrix (rotate-to-face -> translate onto cuboid -> world). Combined matrix is flattened to `SKMatrix` and `canvas.Concat`-ed before drawing the face's UV sub-rect.
  - Painter's algorithm sorts faces by their transformed Z so back faces draw first.
  - Detects legacy 64x32 skins (height 32) and mirrors the right arm/leg into the left slots to avoid grey placeholder rectangles.
  - Bundled Steve.png loaded by code-behind from `avares://...Assets/Icons/MC/steve.png` so the page is populated immediately.
- Added explicit `SkiaSharp 2.88.9` reference so the package version is locked.
- UV table for every cuboid follows the canonical Java 1.8+ skin spec (`https://minecraft.wiki/w/Skin`).

### Known limitations (planned for follow-ups)
- No second-layer overlay (hat / jacket / sleeves / pants) - draws only the base layer.
- No cape support.
- No slim-arm (Alex) detection - everyone renders with classic 4 px arms.
- No fetch of the authenticated user's real skin - everyone sees Steve. Next step is to wire `MojangSkinFetcher` (decode the `textures` property from `sessionserver.mojang.com/session/minecraft/profile/{uuid}`).

## [0.14.0] - 2026-05-15

### Added
- **Hover and transition animations** across the chrome to give the launcher the same "this feels alive" polish as the official Mojang launcher:
  - Sidebar nav radios: 180 ms `BrushTransition` on `Background` for smooth hover + selection-state crossfade.
  - Launch button (`Classes="LaunchBtn"`): 180 ms `TransformOperationsTransition` scales to 1.05 on hover and 0.97 on press, plus a synchronous `BrushTransition` from the dark primary to the lighter primary - lifts off the page when you reach for it.
  - Window-control buttons (min / max / close): 150 ms background fade. Close still flashes red on hover; min / max get a 20% white wash.
  - Page panels (`Classes="Page"` on Home / Installations / Skins / Servers / News / Settings): 220 ms `DoubleTransition` on `Opacity`, easing out, applied when the panel becomes visible from a sidebar switch.

## [0.13.0] - 2026-05-15

### Changed
- **Custom window chrome** replaces the Windows-native title bar. `Window.SystemDecorations="None"` + `ExtendClientAreaToDecorationsHint="True"` + `ExtendClientAreaChromeHints="NoChrome"` + `ExtendClientAreaTitleBarHeightHint="-1"` gives us a borderless window; we paint our own 48 px header strip with the launcher logo, account chip, and three Win-11-style buttons (minimize / maximize-restore / close) drawn as `Path` glyphs so they stay sharp at any DPI.
  - Dragging the header (anywhere except a button) calls `Window.BeginMoveDrag(...)`; double-click toggles maximize/restore.
  - Close button hover turns red (`#E81123` to match the Windows convention).
  - The account chip and Sign-in button live in the same strip - one row of chrome doing double duty.

## [0.12.0] - 2026-05-15

### Changed
- **Font readability**: swapped the chunky pixel `IdreesInc/Minecraft-Font` for **`IdreesInc/Monocraft`** (same author, SIL OFL 1.1, monospaced face designed for legibility in code editors while keeping the Minecraft pixel idiom). The `{StaticResource MinecraftFont}` resource now resolves to Monocraft, so every existing XAML reference picks up the readable face without changes. The old pixel font is still bundled and exposed as `{StaticResource MinecraftPixelFont}` for any future decorative headlines that need the chunkier look.
- `MainWindow.FontFamily="Inter, Segoe UI, system-ui"` so body text uses the existing `Avalonia.Fonts.Inter` package rather than the system default.
- `<Version>` bumped to `0.12.0` in both shipping csproj files.

## [0.11.0] - 2026-05-15

### Fixed
- **Microsoft sign-in now works on Avalonia.** The default `XboxAuthNet` OAuth flow expects a WPF-hosted WebView2 instance, which Avalonia doesn't provide; sign-in failed with `"Current platform does not support to provide default WebUI."` Replaced the OAuth provider with `XboxAuthNet.Game.Msal.OAuth.MsalDeviceCodeProvider` (no native UI host required - Microsoft hands us a short device code, we open the verification page in the user's default browser, MSAL polls until the user finishes). MSAL's own token cache lives in `%LOCALAPPDATA%\.IdentityService\`, so subsequent launches sign in silently with no prompt.

### Added
- `MicrosoftDeviceCodeInfo` DTO (`UserCode`, `VerificationUrl`, `Message`) + `IMicrosoftAuthService.DeviceCodeRequested` event. The view-model subscribes and appends the code to the launcher log (marshalled to the UI thread via `Dispatcher.UIThread.Post`).
- `MicrosoftAuthService` now uses the Mojang/Minecraft MSAL client_id `499c8d36-be2a-4231-9ebd-ef291b7bb64c`; account cache at `%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.json` unchanged.
- `XboxAuthNet.Game.Msal 0.1.3` added to App.

## [0.10.1] - 2026-05-15

### Fixed
- **Crash on first refresh after clicking a sidebar item**: `MainViewModel.Refresh*Async` methods used `await _service.X().ConfigureAwait(false)`, so the continuation that mutated bound `ObservableCollection`s ran on the threadpool. Avalonia's `ItemsControl` / `PanelContainerGenerator` then raised `System.InvalidOperationException: Collection was modified; enumeration operation may not execute` while the panel was iterating its children on the dispatcher thread. Removed `.ConfigureAwait(false)` from every await inside the VM (10 sites) so each continuation lands back on the UI thread before touching bound collections / `IsBusy` / `LogText`. Service-layer code keeps its own `ConfigureAwait(false)` - the change is scoped to view-model code only.

## [0.10.0] - 2026-05-15

### Added
- **Real Settings page** replaces the stub:
  - Memory section: two sliders (`-Xms` / `-Xmx`) with live MB readout. Maximum auto-clamps to `~75% of system RAM` (capped at 16 GB) via the new `SystemRam.RecommendedMaxHeapMb()` helper that uses `GlobalMemoryStatusEx` on Windows and `/proc/meminfo` on Linux.
  - Custom JVM arguments multi-line text box (persisted; CmlLib forwarding planned).
  - Game-directory and Java-executable overrides with **Browse buttons** that open Avalonia 11's `IStorageProvider.OpenFolderPickerAsync` / `OpenFilePickerAsync` dialogs.
  - Launcher preferences: "Keep launcher open after the game starts" + "Show game stdout/stderr in launcher log".
  - "Save settings" button persists everything via `ILauncherSettingsStore`.
- `Core/Settings/` folder:
  - `LauncherSettings` DTO.
  - `ILauncherSettingsStore` + `FileLauncherSettingsStore` (JSON at `%LOCALAPPDATA%\HyperionMinecraftLauncher\settings.json`, atomic write-temp-and-rename).
  - `SystemRam` helper.
- 4 new tests (`LauncherSettingsStoreTests`): missing file returns defaults, save+load round-trips every field, corrupt JSON returns defaults, save creates parent directories.

### Changed
- `MainViewModel` constructor gains an optional `ILauncherSettingsStore`; reads on construction (sync). `LaunchAsync` now passes the user's Xms / Xmx / GameDirectory into `LaunchRequest` so the next launch honours them.
- App csproj fix-up: bumped to `0.10.0` together with Core (the previous bump landed on the wrong commit).
- `<Version>` bumped to `0.10.0` in both shipping csproj files.

## [0.9.0] - 2026-05-15

### Added
- Real **Minecraft news feed** on the News page, sourced from `https://launchercontent.mojang.com/news.json` - the same endpoint Mojang's own launcher reads from, so Hyperion users see the same articles in the same order.
- `Core/News/` folder: `NewsEntry` DTO (`Title`, `Category`, `Date`, `Text`, `ImageUrl`, `ReadMoreLink`, `Id`), `INewsClient` interface, `MojangNewsClient` HTTP impl with `Parse(Stream)` static for fast unit tests.
- News page UI shows each article as a card with hero image (72x72 from the feed CDN), title, category chip, date, teaser text, and "Read more on minecraft.net" button that opens the article in the default browser via `Process.Start { UseShellExecute = true }`.
- `IMinecraftLauncherService.ListNewsAsync(...)`; the view-model exposes a `News` observable collection and `RefreshNewsCommand`.
- 4 new tests (`MojangNewsClientTests`): valid feed maps every field, falls back to `playPageImage` when `newsPageImage` missing, no-entries returns empty, absolute image URL passes through unchanged.
- `AsyncImageLoader.Avalonia 3.3.0` (pinned to the last 3.x-Avalonia-11 line - 3.8.0 jumps to Avalonia 12) to fetch the hero images asynchronously without blocking the UI thread.

### Changed
- `CmlLibMinecraftLauncherService` constructor gains an optional `INewsClient` parameter (defaults to `MojangNewsClient`).
- `<Version>` bumped to `0.9.0` in both shipping csproj files.

## [0.8.0] - 2026-05-15

### Added
- Official Minecraft block / item textures pulled from 5+ mirrors (`InventivetalentDev/minecraft-assets` 1.20.4 / 1.21 / 1.21.4 / master branches; `mcasset.cloud` CDN; `PrismarineJS/minecraft-data`; `Mojang/bedrock-samples`). 17 textures fetched from first-success mirror; the chest entity texture and a 32x32 cropped/upscaled Steve face (extracted from the default skin's [8,8,8,8] head face + hat overlay) round out the set to 21 PNGs under `src/HyperionMinecraftLauncher.App/Assets/Icons/MC/`.
- All UI icons are now real Minecraft textures: grass-block-side (Home), chest entity (Installations), Steve face (Skins / account chip), ender pearl (Servers), writable book (News), comparator (Settings), oak planks (profiles list), compass (manifest versions).
- `scripts/download_mc_icons.py` and `scripts/extract_extra_mc_icons.py` - reproducible setup scripts left in-tree.

### Changed
- **Compact layout** to fix the text-overlap / cramped feel:
  - Window default 1040x640 -> 960x580; min size 860x540 -> 800x500.
  - Sidebar 220px -> 180px.
  - Header 60px -> 48px (logo 28 -> 24, title font 20 -> 16).
  - Content margin 20px -> 14x12. Card padding 14px -> 10px. Inter-card margin 10px -> 6px.
  - Title fonts 22 -> 18, body fonts -> 11-12. Launch button: pixel-font 14 (was 16) and tighter 18x6 padding.
  - Account chip: smaller padding (8,4 vs 12,6), 22px Steve face, 11px button.
- Replaced the resource-pack banner Image strips on the home cards (which contained 4-icon menu sprites and overlapped the watermark) with single 24x24 MC block-texture icons.
- Sidebar nav rows now use raster MC textures via `RadioButton.Tag`; padding 12,10 -> 10,8.

### Notes
- `Material.Icons.Avalonia 2.4.1` and `MaterialIconStyles` are still wired in `App.axaml` for future use, but no view currently references a MaterialIcon kind - every chrome glyph is now MC pixel art.
- `<Version>` bumped to `0.8.0` in both shipping csproj files.

## [0.7.1] - 2026-05-15

### Fixed
- **App crashed on first command click**: `AsyncRelayCommand.RaiseCanExecuteChanged` fired the event from a threadpool continuation (after `await ... ConfigureAwait(false)` in `ExecuteAsync`), and Avalonia's `Button.CanExecuteChanged` handler reads `Button.Command` (a styled property) which throws `InvalidOperationException: Call from invalid thread` off the dispatcher. Now marshals to the UI thread via `Dispatcher.UIThread.Post` when not already on it.
- Account chip no longer shows "Sign in" twice (label + button) before sign-in. The label now hides entirely until a session exists (`HasSession` property), and the button reads "Sign in with Microsoft" to make the action explicit.

## [0.7.0] - 2026-05-15

### Added
- Read-only support for the multiplayer server list (`servers.dat`). The Servers page is no longer a stub - it now lists every server saved by the in-game multiplayer screen with display name and host:port. Status pinging and add/edit/delete are planned for a later release.
- `Core/Servers/` folder: `ServerListEntry` DTO (Name, Ip, IconBase64, AcceptTextures), `IServersStore` interface, `FileServersStore` implementation using `fNbt 1.0.0` to parse the uncompressed NBT.
- `IMinecraftLauncherService.ListServersAsync(...)`; the view-model owns a `Servers` observable collection and a `RefreshServersCommand`.
- 4 new tests in `FileServersStoreTests`: missing file returns empty, two entries round-trip name+ip, optional icon + acceptTextures round-trip correctly, corrupt file returns empty instead of throwing.

### Changed
- `Core.csproj` now depends on `fNbt 1.0.0` (NBT parser, no transitive deps, ~150 KB).
- `CmlLibMinecraftLauncherService` constructor gains an optional `IServersStore` parameter (defaults to `FileServersStore`).
- `<Version>` bumped to `0.7.0` in both shipping csproj files.

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
