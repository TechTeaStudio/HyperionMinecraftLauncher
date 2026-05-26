# Changelog

All notable changes to this project are documented here.
Format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.32.22] - 2026-05-26

### Changed

- Release page body now contains only the current version's CHANGELOG section plus GitHub's auto-generated "What's Changed" block (PR list since the previous tag + new contributors + diff link), instead of the entire CHANGELOG.md verbatim. The `release.yml` workflow extracts the matching `## [version]` block via an awk pass and writes it to `release_body.md`, then passes `body_path: release_body.md` + `generate_release_notes: true` to `softprops/action-gh-release`. Past releases (v0.32.21 and earlier) still carry the dump; future tags pull only their own section.

## [0.32.21] - 2026-05-26

Hotfix on the Linux release pipeline.

### Fixed

- AppImage packaging step failed on Ubuntu 24.04 GitHub Actions runners with `dlopen(): error loading libfuse.so.2`. `appimagetool` is itself shipped as an AppImage, and modern Ubuntu runner images no longer carry FUSE 2 by default. Now invoked with `APPIMAGE_EXTRACT_AND_RUN=1`, which makes the AppImage runtime self-extract to a tmp dir and exec from there, bypassing FUSE entirely. Slightly slower per invocation, robust across every Linux host.
- `scripts/build-appimage.sh` hardcoded `APP_VERSION="0.28.0"` (stale since v0.29). Now reads from the repo-root `VERSION` file, matching the pattern already used by `build-macos-app.sh` and `build-macos-dmg.sh`.

## [0.32.20] - 2026-05-26

Cumulative release covering twenty iterations on top of v0.31.0's localization wave. Highlights: the skin browser is now pluggable (MineSkin v2 replaces the retired NameMC scraper), Force-offline developer mode lands with full localization, the macOS release pipeline ships real `.app` + `.dmg` bundles for both architectures, a one-click Windows build drops a single self-extracting `.exe` at the repo root, dependabot is muzzled into grouped monthly PRs with auto-merge, and Linux CI is green again after a SkiaSharp native-package pin. First GitHub Release.

### Added

- One-click Windows build: `build.cmd` at the repo root + `scripts/build-release.ps1`. Produces `HyperionMinecraftLauncher.exe` (~110 MB self-extracting single-file with `IncludeNativeLibrariesForSelfExtract=true`) plus `outputs/HyperionMinecraftLauncher-<ver>-win-x64.zip` byte-for-byte equivalent to the CI artefact.
- Force-offline developer toggle in Settings (Launcher preferences). When ON, every Play routes through `AuthMode.Offline` regardless of any cached Microsoft session. Adds a TEST-MODE badge next to the Username row. Backed by `LauncherSettings.ForceOfflineMode` with JSON round-trip tests, localized into all 14 shipped locales.
- MultiMC instance import: parses `instance.cfg` + `mmc-pack.json` from a MultiMC pack `.zip` or instance folder, materialises a Hyperion instance with the right loader pinned.
- Skin browser pivoted from NameMC (Cloudflare-blocked scraper, now retired) to MineSkin v2 API. Pagination (Prev/Next), Enter-to-search, minifigure-card previews, subtle card backgrounds, dynamic panel.
- Hybrid nickname-to-Mojang skin search: an exact-match query against a live player resolves via the Mojang profile API and is labelled as a live-player hit, not a community skin.
- System-JDK probe preferred over Adoptium download: probes `JAVA_HOME` and `PATH` for a major-version match before falling back to the managed Temurin cache, saving the ~180 MB cold download when the user already has the right JRE.
- macOS release pipeline ships real `.app` + `.dmg` for both osx-x64 and osx-arm64 via `scripts/build-macos-app.sh` + `scripts/build-macos-dmg.sh`. Bundles carry proper `Info.plist`, `AppIcon.icns` generated from the repo icon via `sips` + `iconutil`, and resx satellite assemblies under `Contents/Resources/Localization/`.
- Dependabot auto-merge workflow (`.github/workflows/dependabot-auto-merge.yml`). Non-major dependabot PRs squash-merge via `gh pr merge --auto` once branch-protection status checks turn green. Major bumps still require manual review.

### Changed

- Account flyout binds to the real per-account skin head face (fetched + cached on first paint), replacing the universal Steve placeholder.
- Skin viewer rotates the full body on drag (was head-only), with proper pitch/yaw accumulation. Back-view supported.
- CurseForge errors are now friendly: 401, 403, 429 surface "Your CF API key is missing/wrong/rate-limited" instead of the raw `HttpRequestException`.
- Edit instance dialog: height 760, scrollable, icon panel centered with WrapPanel. Previously clipped on smaller screens.
- `MainViewModel.IsSignedInOnline` now also honors the Force-offline toggle, so the Username textbox unlocks and skin-write commands disable even when a Microsoft session is cached.
- Dependabot tamed: six NuGet groups (Avalonia / Microsoft.Extensions / auth-stack / SkiaSharp / test-stack / other), `open-pull-requests-limit: 3` for NuGet and `1` for GitHub Actions, monthly schedule, ignore-list for cosmetic semver-minor/major bumps on the canonical `actions/*` stack. Security CVE bumps still flow unthrottled.
- GitHub Actions bumped to `@v5` across both workflows (checkout, setup-dotnet, upload-artifact, download-artifact). Silences the Node.js 20 deprecation warning.
- CLI mode logger switched from the legacy `FileLauncherLogger` to `SerilogLauncherLogger` (same CLEF-format JSON, same log directory). Removes the obsolete-API warning at `Program.cs:28`.
- Release CI flatten step now includes `*.dmg` alongside `*.zip`, `*.AppImage`, `*.tar.gz`, so macOS installers actually reach the GitHub Releases page (previously they uploaded as workflow artefacts but were dropped at the release attach step).

### Fixed

- macOS release zip used to bundle a raw self-contained publish tree instead of a real `.app` bundle, so Finder refused to treat the artefact as an application. Now wraps each `.app` produced by `build-macos-app.sh` in a Gatekeeper-friendly ZIP, plus a `.dmg` with the standard drag-to-`/Applications` symlink.
- Ubuntu CI was red because of a SkiaSharp version conflict: `SkiaSharp 3.119.2` (managed) vs `SkiaSharp.NativeAssets.Linux 2.88.9` (transitive via `MinecraftSkinRender` -> `Avalonia.Skia`). The 2.88.9 native was winning the deploy race for `runtimes/linux-x64/native/libSkiaSharp.so`. Pinned `SkiaSharp.NativeAssets.Linux 3.119.2` as a direct reference so it overrides the transitive 2.88.9. Added `LD_LIBRARY_PATH` to the Ubuntu test step as defence-in-depth against `dlopen` falling through to the system `/usr/lib/libSkiaSharp.so.88`.
- `FileLauncherLoggerTests.DefaultLogDirectory_Resolve_EndsWithHyperionLogs` was hardcoded to the Windows tail `<App>\logs` and failed on Linux + macOS where the canonical layout is `~/Library/Logs/<App>` (macOS) and `~/.local/state/<App>` (Linux, XDG). Test now branches per-OS.
- MineSkin v2 returned 400 on page sizes above the anonymous API ceiling. Now capped at 128.
- Chromeless-window edge resize regressed when the brand icon was first wired in. Restored.
- Skin-browser pagination panel collapsed on narrow viewports. Now stays visible and adapts.
- 3D head defaulted to a 45-degree-rotated face rather than the canonical face-on view. Body cache also resets between skin loads so old textures stop bleeding through.
- CurseForge API key validation test had an off-by-one key length.

### Notes

- 603 unit tests passing (was 602 at v0.31.0). CI green on Windows + macOS + Ubuntu.
- `Tmds.DBus.Protocol 0.20.0` has a known high-severity advisory (`NU1903`); the vulnerable transitive comes from `Avalonia 11.2.3` and a fix is expected once Avalonia bumps. Not exploitable in the launcher's usage (no DBus IPC).
- The `Strings.ShowGameLogTooltip` resx key is still missing from seven locales (`ja`, `ko`, `nl`, `pl`, `tr`, `uk`, `it`); they cascade to English. Tracked for a future localization pass.

## [0.31.0] - 2026-05-18

Sixth wave of the v0.26 -> v0.31 parallel-agent sprint: full UI localization. The English source `Strings.resx` lives at `src/HyperionMinecraftLauncher.App/Localization/`; 7 translations ship alongside it for a total of 8 locales.

### Added
- `Core/Localization/ILocalizationService` + `ResxLocalizationService` wrapping the standard `ResourceManager` family. Exposes `this[key]`, `Get(key, args)`, `CurrentCulture`, `SetCultureAsync`, `LanguageChanged` event, `AvailableCultures`.
- `LauncherSettings.Locale` (string?, default null = "use OS culture") persisted through the existing settings store.
- Settings page: "Language" card with a dropdown bound to `AvailableLocales`. First entry is "Use system default" (null).
- 289 user-facing strings extracted from MainWindow.axaml and every dialog into `Strings.resx`. Pattern: `{x:Static loc:Strings.Section_Key}`.
- New translations (each ~289 entries, full UI parity with English source):
  - `Strings.ru.resx` Russian
  - `Strings.es.resx` Spanish (neutral)
  - `Strings.pt-BR.resx` Brazilian Portuguese
  - `Strings.de.resx` German (informal "du")
  - `Strings.fr.resx` French (informal "tu", non-breaking space before colons)
  - `Strings.zh-Hans.resx` Simplified Chinese
  - `Strings.ja.resx` Japanese (です・ます polite plain)
- `Localization/README.md` contributor doc explaining where new strings go and how to add a culture.

### Changed
- Test fixtures in `MainViewModelTests` force `CurrentUICulture = en` so log-text assertions stay deterministic on non-English dev machines.

## [0.30.0] - 2026-05-18

Fifth wave: closing the Prism / MultiMC feature gap. Seven parallel agents shipped concurrent features.

### Added
- **Mod-loader installer** (T21a). `IModLoaderInstaller` + `CmlLibModLoaderInstaller` dispatcher honours Fabric / Quilt (from `CmlLib.Core` directly), Forge (`CmlLib.Core.Installer.Forge`), and NeoForge (`CmlLib.Core.Installer.NeoForge`). `IModLoaderVersionFetcher` populates the dropdown on the New Instance dialog when a non-Vanilla chip is picked. On launch, the install runs before CmlLib's regular version install and substitutes the modded version-id into the `LaunchRequest`.
- **Per-instance overrides** (T21b). New `EditInstanceDialog.axaml` with General (Name + icon picker) and Java & memory (Min/Max RAM sliders, JVM args, game directory + Browse, window resolution NumericUpDowns) tabs. `InstanceLaunchSettings.Merge(Instance, LauncherSettings)` resolves per-instance > global; empty / 0 fields mean "inherit". Context-menu entry "Edit instance..." with the 3-layer auto-imported guard.
- **Crash report parser** (T21c). `MinecraftCrashReportParser` recognises both Forge pipe-table and Fabric hyphen-list mod blocks, ranks suspect mods by stacktrace frame, and surfaces Modrinth + CurseForge search URLs. New "Crashes" tab on instance detail with a per-suspect Modrinth / CurseForge button strip and an "Open folder" shortcut.
- **Modpack import** (T21d). `IModpackImporter` with `ModrinthModpackImporter` (.mrpack) and `CurseForgeModpackImporter` (.zip). Auto-detects format. Copies `overrides/` tree, downloads each `files[]` entry, lays out a fresh `Instance` under `LOCALAPPDATA/instances/{newId}/`. UI: "Import modpack..." button on the Installations row + progress strip.
- **Instance export / import** (T21e). `FileInstanceExporter` + `FileInstanceImporter` produce / consume Hyperion-format zips (`hyperion-instance.json` + `metadata.json` + `gameDir/` filtered). `ExportOptions` default excludes `saves/`, `screenshots/`, `logs/`, `crash-reports/`. Per-tile MenuFlyout "Export to zip..." + top-level "Import from zip..." button.
- **Auto-backup worlds before launch** (T21f). `IBackupService` + `FileSystemBackupService` zips each subdir of `saves/` to `gameDir/backups/{world}-{ts}.zip` before every launch when `LauncherSettings.AutoBackupBeforeLaunch` is true. Prunes to `AutoBackupKeepLatest` per world. Per-world "Backup now" / "Restore latest backup" context-menu on the Worlds tab.

### Changed (Performance — T18)
- New `Core/Diagnostics/StartupTimeline`: labelled checkpoints feed one summary log line ("[startup] dispatcher=Xms versions=Yms ... TOTAL=Zms").
- `MainViewModel.RunStartupRefreshesAsync` parallelises every independent refresh via `Task.WhenAll`. MS-auth silent sign-in stays sequential at the end.
- Dropped a redundant version re-sort in `CmlLibUnderlyingLauncher.GetAllVersionsAsync` (CmlLib returns newest-first).
- `SystemRam.RecommendedMaxHeapMb()` now caches via `Lazy<int>`.
- `ServerStatusJson.DecodeFavicon` slices the data-URI prefix off a `ReadOnlySpan<char>` and decodes base64 with `Convert.TryFromBase64Chars`.
- App csproj gains a `ReleaseAot` configuration with `PublishAot=true` flagged "future experimentation only".

## [0.29.0] - 2026-05-18

Fourth wave: Liquid Glass UI redesign matching the Tech Tea Studio Flutter prototype.

### Added
- `Themes/LiquidGlass.axaml` resource dictionary with the full design-token sheet: background gradient `#0D0D1B -> #1A1A28`, surface tints, falling-light gradient + rotation + blur sigma, glass surface gradient (TL->BR), secondary gradient (T->B), rim border, bubble fill + border + three-layer shadow set, radii (15 / 10 / 15), animation timings.

### Changed
- MainWindow background painted with the new vertical gradient; static blurred falling-light wedge added in the top-right.
- Sidebar radio buttons: idle background transparent, `:checked` swells into the bubble (fill + rim + shadows); icons scale 1.0 -> 1.25 on selection.
- Cards (`Classes="Card"`) use the glass surface gradient with the rim border and the new 15dp corner radius. The existing hover-lift transition is preserved.
- Launch / "+ New Instance" / "Upload skin" / "+ New server" / Save settings buttons share the `Classes="LaunchBtn"` style with the bubble visual.
- All seven dialogs (NewInstance, EditInstance, EditInstanceIcon, DeviceCode, JoinServer, Worlds, NewHeadlessServer, SkinVariant, ImportModpack) adopt the same glass-on-acrylic look.

## [0.28.0] - 2026-05-18

Third wave: platform / infra.

### Added
- **Auto-download Adoptium JRE per MC version** (T4). `JavaRequirementResolver.For(string mcVersion)` maps to Java 8 / 17 / 21. `AdoptiumJavaRuntimeManager` downloads and extracts the JRE under `LOCALAPPDATA/HyperionMinecraftLauncher/java/{requirement}/` and patches `LaunchRequest.JavaPath` automatically.
- **Update notification** (T16). `GitHubReleasesUpdateChecker` polls `repos/TechTeaStudio/HyperionMinecraftLauncher/releases/latest` (1h disk-cached). A dismissible banner across the top of the window opens the release page in the default browser.
- **Headless dedicated-server registry** (T11). `IHeadlessServerStore` + `FileHeadlessServerStore` create and persist server folders under `LOCALAPPDATA/headless_servers/{id}/` with `metadata.json` + `eula.txt=true` + minimal `server.properties`. New "Headless servers" sidebar page. Start / Stop are placeholders pending v0.32 server-jar download.
- **CLI mode** (T11.5). Program.cs detects `--help` / `--version` / `--list-instances` / `--list-versions` / `--launch` and runs in console mode (P/Invoke `AttachConsole(-1)` on Windows). Pure `CliArgumentParser` is xUnit-tested.
- **Linux support** (T20). `IEnvironment` + `XdgPaths` route every Core file consumer through XDG-appropriate roots (`$XDG_STATE_HOME` for logs, `$XDG_CONFIG_HOME` for config, `$XDG_DATA_HOME` for data, `$XDG_CACHE_HOME` for cache). `scripts/build-appimage.sh` produces a self-contained linux-x64 AppImage. CI matrix now runs `ubuntu-latest + windows-latest`.

### Changed
- Production logger swapped from `FileLauncherLogger` to `SerilogLauncherLogger` (T19): daily-rotated JSON log via Serilog + Serilog.Sinks.File + Serilog.Formatting.Compact. 14-day retention, 32 MB cap per file. `FileLauncherLogger` is `[Obsolete]` but kept for deterministic clock-injected tests.

## [0.26.0] - 2026-05-18

### Added
- **Edit the icon of an existing instance.** Right-click any user-created tile on the Installations grid to get a "Change icon..." entry, or click the small "..." overflow button in the tile's top-right corner. Opens a modal that reuses the 15-tile Minecraft icon picker from the New Instance dialog, pre-selects the current icon, and on Save persists the change via `IMinecraftLauncherService.SaveInstanceAsync`. The tile updates in place via the existing `IconKey` binding.
  - Auto-imported instances (those synthesised from `.minecraft/versions/`) do not surface the menu item or the overflow button. They belong to the official launcher and Hyperion doesn't persist edits for them.
  - Logs a single `ILauncherLogger.Info` line per change: `Instance {Id} ({Name}) icon changed to {NewIconKey}.`
- New `EditInstanceIconDialog.axaml(.cs)` view and `MainViewModel.ChangeInstanceIconAsync(Instance, string)` method.
- xUnit coverage in `FileInstanceStoreTests` verifying `SaveInstanceAsync` round-trips a changed `IconKey` through the file store (overwrite + fresh-load assertion).
## [0.27.0] - 2026-05-18

### Added
- **Quick Play.** Launch straight into a server or world instead of the main menu, exactly like the official launcher's Quick Play feature.
  - `QuickPlay` closed discriminated union (`QuickPlay.None` / `QuickPlay.Singleplayer(WorldFolderName)` / `QuickPlay.Multiplayer(Host, Port=25565)`) on `LaunchRequest`. Default is `None`, so every existing launch path keeps working unchanged.
  - `CmlLibUnderlyingLauncher.BuildQuickPlayArgs` projects the QuickPlay target into Minecraft 1.20+ game arguments (`--quickPlaySingleplayer <world>` / `--quickPlayMultiplayer <host:port>`), appended via `MLaunchOption.ExtraGameArguments` so the launch args are emitted literally regardless of which feature gates the version manifest declares.
  - Servers page gets a "Join" button per row. Click opens a `JoinServerDialog` modal ("Join &lt;name&gt; on &lt;ip&gt;?") with a "Pick instance" combobox defaulting to the currently-selected instance. Confirm sets `QuickPlay = Multiplayer(host, port)` and launches.
  - Home page gets a "Resume world..." button. Click opens a `WorldsDialog` modal that lists subfolders of `&lt;gameDir&gt;/saves/`; confirm sets `QuickPlay = Singleplayer(folderName)` and launches.
  - `CmlLibMinecraftLauncherService.ParseHostPort(string)` splits `host[:port]` (with IPv6 bracket support and graceful fallback to port 25565) - used by the Servers Join path.
  - Log line: "Quick play: joining &lt;target&gt;." precedes every Quick Play launch.

### Changed
- `IUnderlyingLauncher.StartProcessAsync` collapsed from a 6-parameter shape to `StartProcessAsync(LaunchRequest, CancellationToken)` so future LaunchRequest additions don't churn the seam interface.

## [0.25.2] - 2026-05-16

### Changed
- `.gitignore` now excludes `/.icon/` (local folder-icon build artifacts: `_build-folder-ico.ps1` script + generated `folder.ico`). `desktop.ini` was already covered by the OS-junk section.

## [0.25.1] - 2026-05-16

### Changed
- README rewritten in the TechTeaStudio house style: centered logo, badges (.NET 10, Avalonia 11.2.x, platform, build, license, test count), Highlights / How it compares / Configuration and storage / Project layout / Roadmap sections.
- Added `icon.png` at the repo root (256x256 nearest-upscaled `grass_block_side`) so the README hero renders crisp on GitHub.

## [0.25.0] - 2026-05-15

### Changed
- **Instances and Installed Versions unified into a single concept.** The Home page combobox now binds to `Instances` (the same source as the Installations grid) instead of the parallel `InstalledVersions` list, and every version found under `.minecraft/versions/` that isn't already covered by a user-created instance is surfaced as an auto-imported entry. One source of truth, one place to launch from.
  - `Instance` record grows `IsAutoImported` (true for synthesised entries) and a `DisplayText` computed property (single-bind safe for ComboBox closed-state).
  - `RefreshInstancesAsync` now also calls `ListInstalledVersionsAsync`, merges by `VersionId`, and keeps `InstalledVersions` populated for the legacy `SelectedProfile` cross-reference path. Auto-imported instances get a sensible default icon based on detected loader (`cobblestone` for Forge/NeoForge, `oak_planks` for Fabric/Quilt, `grass_block_side` for vanilla).
  - `DeleteInstanceCommand.CanExecute` returns false when the selected instance is auto-imported (the version folder belongs to the official launcher; we don't touch it). The actual delete handler short-circuits with a friendly log line as well.
  - On Launch, auto-imported instances no longer get a `SaveInstanceAsync` for the `LastPlayedAt` stamp - the bump still happens in-memory so the tile floats to the front, but we don't write a JSON file for an instance we didn't create.
  - Installations grid shows a small "auto" pill (top-right of the tile) on auto-imported entries so the distinction is visible at a glance.
- Startup refresh no longer calls `RefreshInstalledVersionsAsync` separately - `RefreshInstancesAsync` now covers both paths.

## [0.24.0] - 2026-05-15

### Added
- **Instance grid + New Instance dialog** on the Installations page (replaces the read-only Mojang profiles view).
- `Core/Instances/` module:
  - `Instance` record - per-instance fields: `Id`, `Name`, `VersionId`, `Loader`, `LoaderVersion`, `IconKey`, `CreatedAt`, `LastPlayedAt`, `GameDirectory`, `JvmArguments`, `MinimumRamMb`, `MaximumRamMb`, `ResolutionWidth/Height`.
  - `InstanceIcons` - 15 curated MC icon-key constants (grass, dirt, stone, chest, compass, ...).
  - `IInstanceStore` + `FileInstanceStore` - one JSON file per instance under `%LOCALAPPDATA%\HyperionMinecraftLauncher\instances\{Id}.json`. Atomic writes (write-temp + rename). Load is tolerant of malformed entries.
- `IMinecraftLauncherService` grows `ListInstancesAsync`, `SaveInstanceAsync`, `DeleteInstanceAsync`.
- `MainViewModel` exposes `Instances` (ObservableCollection), `SelectedInstance`, `RefreshInstancesCommand`, `DeleteInstanceCommand`, and `CreateInstanceAsync(name, version, icon)`. Launch flow now prefers `SelectedInstance.VersionId` over the home page version pickers; on successful launch the instance's `LastPlayedAt` is stamped and the tile floats to the front of the grid.
- `Views/NewInstanceDialog.axaml(.cs)` - custom-chrome modal with name, version dropdown (consumes `AvailableVersions`), and a 15-tile icon picker driven by `InstanceIcons.All`.
- `IconKeyToUri` value converter resolves icon-key strings to bundled `avares://...Assets/Icons/MC/{key}.png` bitmaps.

### Changed
- The Installations page is now a `WrapPanel` ListBox of 130x148 tiles (icon + name + version), with "+ New Instance", "Refresh", and "Delete selected" buttons. Hover lift inherited from the existing `Classes="Card"` style.
- Auto-refresh on startup now also loads instances (in addition to installed versions / profiles / servers / news / manifest).

## [0.23.0] - 2026-05-15

### Added
- **Card hover-lift.** Every paper-brush card on Home / Installations / Servers / News / Settings now carries `Classes="Card"`; the new style runs a 160 ms `TransformOperationsTransition` + `BrushTransition` that translates the card up 2 px and brightens the surface when the pointer enters.
- **Account-chip avatar tilt.** The player face in the header rotates `-8deg` and scales 1.08 on hover (200 ms `QuadraticEaseOut`). Tagged `Classes="Avatar"`.
- **Sign-in / Sign-out chip buttons** (`Classes="ChipBtn"`) scale to 1.06 on hover - same family as the Launch button but gentler so the header doesn't jiggle aggressively.
- **Sidebar nav tiles** now also translate 2 px to the right on hover (in addition to the existing background swap) for a more "alive" feel.
- **ListBoxItem** hover background fades over 140 ms instead of snapping (Profiles list, Servers list).

## [0.22.0] - 2026-05-15

### Fixed
- **ComboBox selected item no longer renders empty.** Avalonia 11's `<Run Text="{Binding X}" />` inside a `TextBlock` template doesn't round-trip through the ComboBox's selection-display path (dropdown items rendered fine, the closed-state slot blanked out). Replaced every multi-`<Run>` template with a single-binding `<TextBlock Text="{Binding DisplayText}" />`; added a computed `DisplayText` property to `InstalledVersion`, `VersionMetadata`, and `LauncherProfile` that produces the previous "id - type" / "id - loader" / "name - type" string in one go.
- **Window-control buttons (min / max / close) no longer cast a Material drop-shadow.** Added `Material.Styles.Assists.ShadowAssist.ShadowDepth="Depth0"` on the `Button.WinCtrl` style and a templated-Border override that pins the inner shadow to transparent. Same treatment applied to the sidebar nav radios so the whole chrome stays flat against the acrylic.

### Changed
- Card / panel corner radius bumped from 4 px to 6 px across all 14 cards (Home, Installations, Servers, News, Settings) for a slightly softer "modern" feel.

## [0.21.2] - 2026-05-15

### Added
- **Drag-to-rotate head** on the Skins page. `SkinPreview` now tracks pointer state on the head image; each pointer-move delta tweaks the `Skin3DHeadTypeB` x / y view parameters and re-renders the head PNG, which is cheap because the source is 64x64. Cursor turns into a four-arrow indicator over the head, plus a "Drag the head to rotate" hint sits at the bottom.
- **Cape support.** New `SkinPreview.CapeSource` styled property renders via `Cape2DTypaA.MakeCapeImage` next to the body sprite. `MainWindow` pushes the cape PNG (already fetched by `MojangPlayerSkinFetcher` and cached on disk) when sign-in finishes; it clears the slot when the user has no cape.
- **Real isometric chest icon** for the Installations sidebar item. Generated once by `scripts/render_iso_chest.py`: loads the chest entity texture, crops top + front + right-side faces, warps each into a 2:1 pixel-art isometric parallelogram with PIL's `PERSPECTIVE` transform, and composites back-to-front. Output at `Assets/Icons/MC/chest_iso.png` (336x348, indexed) - looks like an actual chest now, not a flat texture strip.

## [0.21.1] - 2026-05-15

### Fixed
- **Skins page no longer renders blank** (GL_INVALID_OPERATION spam in stderr was 1282/1282/1282). The OpenGL adapter against `MinecraftSkinRender.OpenGL` choked on functions Avalonia's GLES context doesn't expose; rather than fight `glTexStorage2DMultisample` availability and ANGLE quirks, the page now uses Coloryr's pure-Skia path: `MinecraftSkinRender.Image` renders a 3D-perspective head (Skin3DHeadTypeB) and a 2D full-body sprite (Skin2DTypeA) into PNGs, displayed side-by-side via a new `SkinPreview` UserControl. No GL context required, no GL errors. Loses mouse-rotation; gains correct rendering.
- **Account-chip avatar now updates to the real player skin** after Microsoft sign-in. The chip used to hard-code the bundled `steve_face_with_hat.png` asset URI; it now binds to `MainViewModel.AvatarBitmap`, which `MainWindow.ApplySkinBytes` populates by cropping the 8x8 head face (via `Skin2DHeadTypeA.MakeHeadImage`) from whichever skin is active - default Steve at launch, user's real face after sign-in / cache hit.

### Removed
- `Controls/SkinRender/` (broken OpenGL adapter + the unused ColorMC license file).
- `MinecraftSkinRender.OpenGL` and the `<AllowUnsafeBlocks>` toggle - no longer needed.

## [0.21.0] - 2026-05-15

### Added
- **Silent auto-sign-in on startup.** `IMicrosoftAuthService.SignInSilentlyAsync` uses MSAL's cached refresh token directly via `JELoginHandler.AuthenticateSilently` (no device-code prompt, no UI). `MainViewModel.RunStartupRefreshesAsync` calls it when `HasCachedAccount` is true so the user lands signed-in - no clicking Sign-in every launch, no re-entering the device code. Falls through silently to "you're signed out" when the refresh token has expired.
- **`Core/Cache/FileCache`**: tiny disk-backed key-value store rooted at `%LOCALAPPDATA%\HyperionMinecraftLauncher\cache\`. Reads check the on-disk last-write-time against an optional TTL; writes are atomic (write-temp + rename). Slash-separated keys nest into subdirectories.
- **News disk cache (1 h TTL)**: `MojangNewsClient` now writes the raw `news.json` bytes to cache after a successful fetch. On startup, a fresh cache hit skips the network entirely; on network failure a *stale* cache entry is still served so the launcher renders yesterday's news offline.
- **Skin disk cache (6 h TTL)**: `MojangPlayerSkinFetcher` caches `skins/{uuid}.png` + a tiny metadata sidecar `{uuid}.json` (slim flag, cape ref) + optional `{uuid}.cape.png`. On stale-cache hit *with* a network failure we still serve the last-known PNG so the user keeps seeing their face offline.

### Changed
- `MojangNewsClient` and `MojangPlayerSkinFetcher` constructors gain an optional `FileCache` parameter. `App.axaml.cs` builds one cache, feeds it to the news client, and passes it through to the skin fetcher in `MainWindow`.
- `IMicrosoftAuthService` grows a third method; the fake in the tests + the stub launcher implement it.

## [0.20.0] - 2026-05-15

### Changed
- **Replaced the home-rolled SkiaSharp 3D viewer** with the real GPU renderer from `Coloryr/MinecraftSkinRender.OpenGL` 1.2.0 (MIT, on NuGet). The old painter's-algorithm Skia approach had cracks between faces, no depth buffer and no overlay layers; the new path is proper OpenGL with depth-tested cubes, full base-layer + hat/jacket overlays, slim arms, cape, and built-in walking animation.
- New control `AvaloniaSkinViewer : OpenGlControlBase` (in `Controls/SkinRender/`) wraps the renderer. Custom `AvaloniaGlApi : OpenGLApi` adapter routes every GL call through `Avalonia.OpenGL.GlInterface` - mostly via the strongly-typed methods, with `GetProcAddress` + delegate fallbacks for the few functions Avalonia 11.2 doesn't expose directly (`glDepthMask`, `glCullFace`, `glBlendFunc`, `glRenderbufferStorageMultisample`, `glBlitFramebuffer`, info-log helpers, etc.).
- Mouse drag rotates the model, scroll wheel zooms. The renderer's `Tick(seconds)` is driven by a `DispatcherTimer` at ~30 fps for the walking animation.
- New control exposes `SkinSource`, `CapeSource`, `Slim` styled properties; `MainWindow` forwards the bundled Steve PNG plus, when the user is signed in, the real skin + cape + slim-flag from `sessionserver.mojang.com`.
- Bumped SkiaSharp to `3.119.2` (required by `MinecraftSkinRender` 1.2.0); old `SkinViewer3D.cs` deleted.
- Enabled `<AllowUnsafeBlocks>` in the App csproj for the GL function-pointer marshalling.

## [0.19.0] - 2026-05-15

### Added
- **Real player skin** in the 3D viewer after Microsoft sign-in. New `Core/Skins/` module:
  - `PlayerSkinInfo` DTO (`SkinPng`, `IsSlim`, `CapePng`).
  - `IPlayerSkinFetcher` interface.
  - `MojangPlayerSkinFetcher` - hits `sessionserver.mojang.com/session/minecraft/profile/{uuid}?unsigned=false`, base64-decodes the `textures` property, downloads the skin and (if present) cape PNGs. Network / schema errors degrade silently.
- `MainWindow` listens for `HasSession` flip to true; when the session is online (real Microsoft auth, not offline), it fetches the skin via UUID and pushes the bytes into `SkinViewer.Skin` on the UI thread. Offline sessions and signed-out states keep the bundled Steve.

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
