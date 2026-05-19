<p align="center">
  <img src="https://raw.githubusercontent.com/TechTeaStudio/HyperionMinecraftLauncher/product/icon.png" alt="HyperionMinecraftLauncher logo" width="160" />
</p>

<h1 align="center">HyperionMinecraftLauncher</h1>

<p align="center">
  A modern Avalonia desktop launcher for Minecraft, built on top of <a href="https://github.com/CmlLib/CmlLib.Core">CmlLib.Core</a>. Microsoft device-code sign-in with multi-account switching, a Prism-style instance manager unified with the official launcher's installed versions, mod-loader install (Forge / Fabric / Quilt / NeoForge), Modrinth and CurseForge mod browsers, modpack import, crash-report parser, auto-backup of worlds, headless dedicated-server registry, CLI mode, 14-language UI, and a Minecraft-styled palette that matches the official Mojang launcher.
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&amp;logoColor=white" />
  <img alt="Avalonia" src="https://img.shields.io/badge/Avalonia-11.2.x-8B5CF6" />
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue" />
  <a href="https://github.com/TechTeaStudio/HyperionMinecraftLauncher/actions/workflows/dotnet.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/TechTeaStudio/HyperionMinecraftLauncher/dotnet.yml?branch=product&amp;logo=github&amp;label=build" /></a>
  <a href="LICENSE.txt"><img alt="License" src="https://img.shields.io/badge/license-MIT-blue.svg" /></a>
  <img alt="Tests" src="https://img.shields.io/badge/tests-590%20passing-brightgreen" />
  <img alt="Version" src="https://img.shields.io/badge/version-0.32.9-3C8527" />
</p>

## Overview

HyperionMinecraftLauncher is a drop-in alternative to the official Mojang launcher with the feature set of Prism / MultiMC for instance management and a single-window Avalonia 11 front-end. The Core library wraps `CmlLib.Core` behind a single `IMinecraftLauncherService` contract for version listing, install, authentication, launch, mod-loader install, server pinging, crash-report parsing, and backups. The App drives that contract through one view-model and renders a warm-dark, green-accented interface that matches the official Mojang launcher palette.

The launcher reads from and writes alongside the same `.minecraft` directory the official launcher uses, never overwriting Mojang's `launcher_profiles.json` or `launcher_accounts.json`. Its own state (instances, settings, MSAL refresh tokens, multi-account roster, cached news + skins, skin history, headless servers, backups) lives under `%LOCALAPPDATA%\HyperionMinecraftLauncher\` on Windows and the appropriate XDG roots on Linux, so Hyperion and the official launcher coexist without stepping on each other.

## Highlights

### Accounts and auth
- **Microsoft sign-in with multi-account switcher.** MSAL device-code flow with a readable user code in a modal; silent re-sign-in on subsequent launches; account chip in the header opens a flyout to switch, add, or sign out of accounts. Cached roster persists between sessions.
- **Offline mode** for LAN games and offline-mode servers when no Microsoft account is connected.

### Instances and launch
- **Unified instance manager.** User-created instances and versions already installed under `.minecraft/versions/` show up in one list, with a small "auto" badge on the auto-imported ones.
- **Import from MultiMC / Prism.** "Import from MultiMC..." on the Installations page takes either the `.zip` produced by Prism's "Export instance" or a copy of the on-disk instance folder. Hyperion parses `instance.cfg` + `mmc-pack.json`, maps the component UID (`net.minecraft`, `net.minecraftforge`, `net.neoforged`, `net.fabricmc.fabric-loader`, `org.quiltmc.quilt-loader`) to its own `ModLoader`, and copies the `.minecraft/` tree (mods, configs, saves, options, server list) into a fresh per-instance folder. JVM args, min/max RAM, and the iconKey carry over too.
- **Per-instance overrides.** Edit dialog lets you override RAM, JVM args, game directory, and window resolution per instance. Empty values inherit from global Settings.
- **Edit instance icon.** Right-click any tile (or the green "..." button) and pick from a 15-tile Minecraft icon set. Auto-imported instances are protected from edits.
- **Quick Play.** Launch straight into a saved world or a multiplayer server via `--quickPlaySingleplayer` / `--quickPlayMultiplayer`. Servers list grew a "Join" button; the Worlds tab grew a "Resume" button.
- **Auto-backup before launch.** Worlds in the instance's `saves/` directory are zipped to `gameDir/backups/{world}-{timestamp}.zip` before every launch, with configurable retention. One-click restore per world.
- **Auto-download Adoptium JRE per Minecraft version.** Java 8 / 17 / 21 picked automatically based on the version id; downloaded once, cached under `%LOCALAPPDATA%/HyperionMinecraftLauncher/java/{requirement}/`.

### Mods and modpacks
- **Mod-loader installer.** Forge, NeoForge (via `CmlLib.Core.Installer.*`), Fabric and Quilt (via the loaders' official meta APIs) install transparently before launch. Loader-version dropdown on the New Instance dialog.
- **Modrinth + CurseForge mod browser.** Search either source, install straight into a per-instance `mods/` folder, enable / disable / remove. Disabled mods get the `.jar.disabled` suffix (Prism convention) so toggling is one rename.
- **Modpack import.** Drop a `.mrpack` (Modrinth) or a CurseForge `.zip` and Hyperion writes the instance, downloads every file, and copies the `overrides/` tree.
- **Crash report parser.** A "Crashes" tab on each instance lists every report under `crash-reports/`, parses the Forge pipe-table or Fabric hyphen-list mod block, ranks suspect mods by stacktrace frame, and offers one-click Modrinth + CurseForge search links.

### Servers
- **Multiplayer server list** parsed from `.minecraft/servers.dat` (per instance) with live Server List Ping: status colour, latency, online / max players, and decoded MOTD.
- **Headless dedicated-server registry.** Folder-per-server under `LOCALAPPDATA/headless_servers/{id}/` with metadata, EULA, and a baseline `server.properties`. Hyperion downloads `server.jar` (Mojang manifest, sha1 verified), auto-installs the matching Adoptium Temurin JRE, starts the JVM, streams stdout into a console pane on the Headless Servers page, and sends commands (`say`, `op`, `stop`, ...) over stdin.

### UX and platform
- **Skins.** 3D skin + cape viewer (pure-Skia, no GL context). Upload a new skin PNG, switch capes, browse a 10-entry skin history, re-apply a historic skin. Community gallery powered by MineSkin v2 with paginated minifigure thumbnails; nickname search resolves through Mojang's public profile API so typing a username like `Notch` shows that account's actual current skin (tagged with a blue "M" badge). Pagination is anchored below the gallery and the panel resizes with the window.
- **News feed** from `launchercontent.mojang.com/news.json` (same source the official launcher uses), with a one-hour disk cache and a stale-on-failure fallback.
- **Logs page.** A dedicated sidebar entry shows today's `launcher-YYYY-MM-DD.log` with substring filter, copy-all, and "Open logs folder".
- **Per-instance browser.** Screenshots, Worlds, Servers, Crashes, Resource packs, Shader packs, and Data packs tabs under each instance. PNG thumbnails open in the OS default viewer; world rows show humanised "last played"; right-click on a world opens its folder; pack tabs support enable / disable / remove and drag-drop install.
- **Discord Rich Presence.** "In Hyperion launcher" when idle, "Playing &lt;version&gt; - &lt;instance&gt;" when the game is running. Optional, falls back to a no-op when Discord isn't installed.
- **Update banner + manual check.** GitHub Releases polled hourly; a green banner appears when a newer version is published. Settings has a "Check for updates" button that reports either the update or "you're on the latest version".
- **14-language UI.** Source is English; bundled translations: Russian, Ukrainian, Polish, Spanish, Brazilian Portuguese, German, French, Italian, Dutch, Turkish, Simplified Chinese, Japanese, Korean. Locale picker in Settings; falls back to OS culture by default.
- **CLI mode.** `--list-instances`, `--list-versions`, `--launch <id>`, `--help`, `--version` work without opening the Avalonia window. Uses `AttachConsole` on Windows so stdout reaches the parent shell.
- **Cross-platform.** Windows installer, Linux `.AppImage`, and macOS `.app` bundle (Intel + Apple Silicon, with optional `.pkg` / `.dmg`) all ship today. Linux paths follow the XDG Base Directory spec (`$XDG_STATE_HOME` for logs, `$XDG_CONFIG_HOME` for config, etc.); macOS uses `~/Library/Application Support` and `~/Library/Logs`.
- **Minecraft palette.** Warm dark background `#171615`, surface tiles `#262423`, green CTAs with the `#6CC349 -> #3C8527` button shading from the official Mojang launcher. Pixel-style 1px black outlines, sharp 4dp corners, and a Minecraft-font sidebar.

### Engineering
- **Serilog file log** at `%LOCALAPPDATA%/HyperionMinecraftLauncher/logs/launcher-YYYY-MM-DD.log` (Linux: `$XDG_STATE_HOME/HyperionMinecraftLauncher/`). Daily rotation, 14-day retention, 32 MB per-file cap, JSON compact format.
- **Startup timeline.** One log line per launch reports the timings of every parallel refresh: `[startup] dispatcher=Xms versions=Yms ... TOTAL=Zms`.
- **Parallel startup refreshes** via `Task.WhenAll`; MS-auth silent sign-in stays sequential at the end.
- **Single-source-of-truth versioning.** Edit `/VERSION` at the repo root, build, and every consumer (the two `.csproj` files via `Directory.Build.props`, the sidebar label, the update-checker probe, the startup log line) picks up the new number automatically.

## How it compares

| Capability | Hyperion | Mojang official | Prism / MultiMC |
|---|---|---|---|
| Microsoft sign-in (multi-account switcher) | yes | yes (one account) | yes |
| Offline mode | yes | yes | yes |
| Instance manager + auto-import installed versions + MultiMC import | yes | no (profiles only) | yes |
| Per-instance icon / RAM / JVM / game-dir / resolution overrides | yes | no | yes |
| News feed (Mojang) | yes | yes | no |
| Multiplayer server list with live ping (servers.dat + SLP) | yes | yes (no ping) | yes |
| 3D skin + cape viewer, skin upload, cape switcher, skin history | yes | partial | varies |
| Forge / Fabric / Quilt / NeoForge installer | yes | yes | yes |
| OptiFine / Legacy-Forge installers | roadmap | no | yes |
| Modrinth + CurseForge mods browser, per-instance mod manager | yes | no | yes |
| Modpack import (`.mrpack` Modrinth + CurseForge `.zip`) | yes | no | yes |
| Instance export / import as `.zip` for sharing | yes | no | yes |
| Crash report parser with mod-link suggestions | yes | no | yes |
| Auto-backup `saves/` before launch + restore | yes | no | partial |
| Headless dedicated-server registry (jar download + JVM + console) | yes | no | no |
| Resource pack / shader pack / datapack manager | yes | partial | yes |
| Process stdout / stderr piped into the launcher log | yes (Settings toggle) | no | yes |
| CLI mode (`--launch`, `--list-instances`, ...) | yes | no | no |
| Quick Play (jump into world or server) | yes | yes (1.20.5+) | varies |
| Auto-download Adoptium JRE per MC version | yes | yes | yes (manual) |
| Discord Rich Presence | yes | no | no |
| Update banner via GitHub Releases | yes | n/a | no |
| 14-language UI (en, ru, uk, pl, es, pt-BR, de, fr, it, nl, tr, zh-Hans, ja, ko) | yes | yes (more) | yes |
| Native AOT publish profile | opt-in (unvalidated) | n/a | no |
| Cross-platform | Windows + Linux AppImage + macOS .app today | Windows / macOS / Linux | all three |
| Open source | yes (MIT) | no | yes |

As of v0.32.0, Hyperion ships the full Prism feature set (mod loaders, per-instance settings, mod browser, crash parser, modpack import, zip share, auto-backup) on top of the official launcher's news feed and Microsoft auth path, plus Hyperion-only extras (multi-account switcher, Discord RPC, headless-server registry, CLI mode, 14-language UI) under a palette tuned to match the Mojang launcher.

## Build and run

```bash
dotnet build HyperionMinecraftLauncher.slnx
dotnet test  HyperionMinecraftLauncher.slnx
dotnet run   --project src/HyperionMinecraftLauncher.App
```

Requires .NET SDK 10. The Core library multi-targets `net8.0;net9.0;net10.0`; the App is `net10.0` (Avalonia 11.2.x, WinExe).

### Linux AppImage

```bash
bash scripts/build-appimage.sh
```

Publishes a self-contained `linux-x64` binary and packages it into `HyperionMinecraftLauncher-x86_64.AppImage`. `appimagetool` must be on `$PATH`.

### macOS build

```bash
bash scripts/build-macos-app.sh         # .app bundle for Intel + Apple Silicon
bash scripts/build-macos-dmg.sh         # optional: wrap each .app into a drag-to-install .dmg
```

`build-macos-app.sh` publishes self-contained `osx-x64` and `osx-arm64` binaries, then assembles `publish/HyperionMinecraftLauncher-x86_64.app` and `publish/HyperionMinecraftLauncher-arm64.app` with a proper `Info.plist`, an `AppIcon.icns` generated from the repo-root `icon.png` via `sips` + `iconutil`, and the resx satellite assemblies mirrored into `Contents/Resources/Localization/`. If `productbuild` is on `$PATH` (it ships with Xcode Command Line Tools) the script also produces `.pkg` installers next to the bundles. The bundles are **unsigned and not notarized** - on first launch users must right-click the `.app` and pick "Open", or clear the Gatekeeper quarantine attribute manually:

```bash
xattr -d com.apple.quarantine HyperionMinecraftLauncher-x86_64.app
```

`build-macos-dmg.sh` wraps each produced `.app` in a compressed `.dmg` with a `/Applications` symlink for the standard drag-to-install affordance. macOS-only (uses `hdiutil`).

### CLI mode

```bash
HyperionMinecraftLauncher.exe --version
HyperionMinecraftLauncher.exe --list-instances
HyperionMinecraftLauncher.exe --list-versions --type release
HyperionMinecraftLauncher.exe --launch <instance-id> [--server host:port]
```

### Smoke test (5 minutes)

1. `dotnet run --project src/HyperionMinecraftLauncher.App` opens the launcher window.
2. The launcher auto-refreshes on startup in parallel: news, server list, installed versions, instances, manifest, accounts, skin history, plus a silent Microsoft sign-in if MSAL still holds a refresh token. A single `[startup] dispatcher=Xms ...` log line reports per-step timings.
3. Click **Sign in with Microsoft** if you want online play. A modal shows the device code; sign in at the URL it prints; the chip flips to your username and your real skin face replaces Steve's.
4. On the **Home** page pick an instance (the dropdown lists both your saved instances and any version already under `.minecraft/versions/`). Per-instance overrides (RAM, JVM args, etc.) are reachable via right-click "Edit instance...".
5. Click **Launch**. Worlds in the instance are zipped to `gameDir/backups/` first; then the loader (if any) installs; then the game starts within ~30 s on first launch and instantly thereafter.

## Configuration and storage

| Path | Owner | Purpose |
|---|---|---|
| `%APPDATA%\.minecraft\versions\` | Mojang launcher | Installed version manifests and JARs. Hyperion reads, never writes. |
| `%APPDATA%\.minecraft\launcher_profiles.json` | Mojang launcher | Profiles shown on the read-only cross-reference path. Hyperion reads, never writes. |
| `%APPDATA%\.minecraft\servers.dat` | Mojang launcher | Multiplayer server list (NBT). Hyperion reads, never writes. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\instances\{Id}.json` | Hyperion | One JSON file per user-created instance. Atomic writes. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\settings.json` | Hyperion | Persisted launcher settings (locale, RAM, JVM args, game-dir, Java exe override, Discord, auto-backup, CurseForge API key, ...). |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.v2.json` | Hyperion | Multi-account roster (v0.27+). Legacy `accounts.json` migrates on first read. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\cache\` | Hyperion | News + player-skin disk cache (1 h / 6 h TTL with stale-on-failure fallback). |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\skins_history\` | Hyperion | Last 10 uploaded skin PNGs + `index.json`. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\headless_servers\{id}\` | Hyperion | One folder per registered headless server (metadata + eula + properties). |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\java\{Java8\|Java17\|Java21}\` | Hyperion | Adoptium JRE installs, one per required Java major. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\logs\launcher-YYYY-MM-DD.log` | Hyperion | Daily-rotated Serilog file log (JSON, 14-day retention, 32 MB cap). |
| `%LOCALAPPDATA%\.IdentityService\` | Microsoft MSAL | MSAL refresh-token cache (managed by the library, used for silent sign-in). |
| `&lt;instance.GameDirectory&gt;\backups\{world}-{timestamp}.zip` | Hyperion | Auto-backup snapshots, pruned to `LauncherSettings.AutoBackupKeepLatest` per world. |

On Linux, paths follow XDG Base Directory: logs land under `$XDG_STATE_HOME/HyperionMinecraftLauncher/` (`~/.local/state/HyperionMinecraftLauncher/`), config under `$XDG_CONFIG_HOME/`, data under `$XDG_DATA_HOME/`, cache under `$XDG_CACHE_HOME/`. On macOS, logs go to `~/Library/Logs/HyperionMinecraftLauncher/`.

## Project layout

```
HyperionMinecraftLauncher/
+- VERSION                                          <- single source of truth for the version
+- Directory.Build.props                            <- reads VERSION into <Version> for every project
+- src/HyperionMinecraftLauncher.Core/              <- library (multi-targets net8/9/10)
|  +- Auth/, Auth/Accounts/                         <- multi-account roster + MSAL contract
|  +- Backups/                                      <- IBackupService + zip-per-world
|  +- Cache/                                        <- FileCache (news + skin)
|  +- CrashReports/                                 <- parser + listener with mod-link suggestions
|  +- Diagnostics/                                  <- StartupTimeline
|  +- InstanceBrowsing/                             <- per-instance screenshots / worlds / servers / resource-packs / shader-packs / data-packs
|  +- Installations/                                <- InstalledVersion scan + LoaderDetector
|  +- Installations/Loaders/                        <- IModLoaderInstaller dispatch (Forge / Fabric / Quilt / NeoForge)
|  +- Instances/, Instances/Export/                 <- Instance record + FileInstanceStore + Hyperion-zip import / export
|  +- Java/                                         <- Adoptium JRE auto-download
|  +- Launcher/                                     <- IMinecraftLauncherService + CmlLib adapter
|  +- Localization/                                 <- ILocalizationService + ResxLocalizationService
|  +- Logging/                                      <- SerilogLauncherLogger + LogFiltering helper
|  +- Mods/, Mods/Modrinth/, Mods/CurseForge/,      <- mod abstractions + repositories
|  |  Mods/Modpacks/                                 <- modpack importers (.mrpack + CurseForge .zip)
|  +- News/                                         <- Mojang news feed
|  +- Platform/                                     <- IEnvironment + XdgPaths
|  +- Presence/                                     <- IPresenceService (NullPresenceService default)
|  +- Profiles/                                     <- launcher_profiles.json reader
|  +- Servers/, Servers/Ping/, Servers/Headless/    <- servers.dat reader + SLP pinger + headless registry
|  +- Settings/                                     <- LauncherSettings + store + SystemRam probe
|  +- Skins/, Skins/History/                        <- skin fetch + upload + cape + 10-entry history
|  +- Updates/                                      <- GitHubReleasesUpdateChecker + SemverComparer
|  +- Versions/                                     <- VersionMetadata
+- src/HyperionMinecraftLauncher.App/               <- Avalonia desktop front-end (WinExe, net10)
|  +- Assets/Icons/MC/                              <- bundled Minecraft icon set
|  +- Auth/MicrosoftAuthService.cs                  <- MSAL device-code wrapper (multi-account)
|  +- Cli/                                          <- CliArgumentParser + CliEntryPoint (Hyperion CLI)
|  +- Controls/SkinPreview.cs                       <- 3D skin + cape viewer
|  +- Localization/                                 <- Strings.resx (en source) + 13 translations
|  +- Presence/DiscordPresenceService.cs            <- Discord Rich Presence wrapper
|  +- Themes/                                       <- MinecraftPalette.axaml + LiquidGlass.axaml redirector
|  +- Views/                                        <- MainWindow + 11 dialogs
|  +- ViewModels/MainViewModel.cs                   <- the one view-model behind everything
|  +- App.axaml(.cs), Program.cs                    <- DI bootstrap + Avalonia entry + CLI dispatch
+- tests/HyperionMinecraftLauncher.Core.Tests/      <- 590 xUnit tests
+- scripts/                                          <- build-appimage.sh + build-macos-app.sh + build-macos-dmg.sh + Python helpers
+- .github/workflows/dotnet.yml                      <- build + test on ubuntu + windows + macos matrix
+- CHANGELOG.md
+- QUICKSTART.md
+- LICENSE.txt
+- README.md
```

## Versioning and release

The launcher version is stored in **one file**: `/VERSION` at the repo root. The build picks it up automatically:

```
+- VERSION                          (currently: 0.32.9)
+- Directory.Build.props            (reads VERSION into <Version>)
+- src/*.csproj                     (inherit <Version>, no per-project override)
```

The sidebar label, the update-checker probe, and the startup log line all read the assembly version at runtime, so bumping is one edit:

```bash
echo "0.33.0" > VERSION
dotnet build HyperionMinecraftLauncher.slnx
```

Format is 3-part SemVer (`X.Y.Z`); commit format is `vX.Y.Z <short description>` capped at 72 characters.

### Continuous integration

Pushing to the `product` branch triggers `.github/workflows/dotnet.yml` (restore + build + test on Ubuntu, Windows, and macOS matrix, .NET 10). Packaging scripts (`build-appimage.sh`, `build-macos-app.sh`) do not run in `dotnet.yml`; the dedicated `release.yml` workflow described below picks them up on a version tag instead.

### Tag-driven release flow

Cutting a new public release is a four-step ritual. The packaging job is intentionally decoupled from per-commit CI so day-to-day pushes do not burn artifact storage.

1. Edit `/VERSION` to the new number (e.g. `0.33.0`).
2. `git commit -am "vX.Y.Z Release X.Y.Z"`.
3. `git tag vX.Y.Z && git push origin vX.Y.Z` to trigger the release workflow.
4. The workflow (`.github/workflows/release.yml`) builds Windows, Linux, and macOS artifacts in parallel and publishes a GitHub Release with the tag's body. The matrix runs:
   - `ubuntu-latest` -> Linux AppImage via `scripts/build-appimage.sh`
   - `windows-latest` -> self-contained `win-x64` single-file publish, zipped
   - `macos-latest` -> Intel + Apple Silicon `.app` bundles via `scripts/build-macos-app.sh`, with optional `.dmg` wrappers from `scripts/build-macos-dmg.sh`

The release job uses `softprops/action-gh-release` with `body_path: CHANGELOG.md`. Branch pushes never trigger this workflow; only tags matching `v*.*.*` do.

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

## Roadmap

Items still open after the v0.32.x parity push. Anything not listed here is shipped on `product`; see [CHANGELOG.md](CHANGELOG.md) for the full history.

### Loaders
- **OptiFine and Legacy-Forge (1.7.10 era) installers.** `CmlLibModLoaderInstaller` throws `NotSupportedException` for these two cases today; the other four loaders (Forge / NeoForge / Fabric / Quilt) install transparently before launch.

### Skin browser
- **Server-side search across the full gallery.** MineSkin v2's anonymous tier ignores `?name=` and caps `?size=` at 128, so the browser oversamples and filters client-side. The hybrid Mojang nickname resolver (`api.mojang.com/users/profiles/minecraft/{name}` -> `sessionserver.mojang.com/session/minecraft/profile/{uuid}`, prepended on page 0) covers the common "find my friend's skin" path. A paid MineSkin key or a server-side proxy would unlock proper server-side search, tags, and variant metadata across the whole feed. NameMC support stays in-tree as `[Obsolete]` so it can be revived once a CF-solver or proxy lands.

### Performance and packaging
- **Validated Native AOT publish.** The `ReleaseAot` configuration is wired but Avalonia 11.2 + MSAL + CmlLib have not been validated for full AOT yet. Run the trim-warnings audit, suppress / re-architect where needed, ship a working `dotnet publish -c ReleaseAot` story.
- **Reduce startup dispatcher delta.** The `[startup] dispatcher=Xms` chunk is the biggest single contributor (~3 s in cold runs). Profile, hoist eager work out of the constructor path, defer where possible.

### Localization
- **Plural-form helpers.** Some plural-fragile strings ("{0} resultado(s)") would benefit from ICU-style plural blocks. Not blocking, but improves polish on count-heavy lines.
- **More languages.** Open to PRs beyond the current 14. Each new locale is one `Strings.{culture}.resx` and one entry in `App.axaml.cs`'s `AvailableCultures` list.

### Polish
- **Code-signing + notarization for macOS bundles.** `build-macos-app.sh` produces unsigned `.app` / `.pkg`; users must right-click -> Open or clear `com.apple.quarantine` on first launch. Apple Developer credentials would let CI run `codesign` + `xcrun notarytool submit` and emit a Gatekeeper-friendly artifact.
- **Material.Avalonia replacement audit.** A handful of controls still pull Material.Avalonia for the `Depth0` shadow override; verify these still look right against the Minecraft palette or replace with native Avalonia styles.

## Further reading

- [QUICKSTART.md](QUICKSTART.md): five-minute new-developer walkthrough.
- [CHANGELOG.md](CHANGELOG.md): release notes for every version.
- [ARCHITECT_REVIEW.md](ARCHITECT_REVIEW.md): the internal architectural review.
- [src/HyperionMinecraftLauncher.App/Localization/README.md](src/HyperionMinecraftLauncher.App/Localization/README.md): contributor doc for adding new locales.

## License

Licensed under the [MIT License](LICENSE.txt). Copyright &copy; Tech Tea Studio.

<p align="center">
  Built as part of the Hyperion Ecosystem by <a href="https://techteastudio.cc">TechTeaStudio</a>.
</p>
