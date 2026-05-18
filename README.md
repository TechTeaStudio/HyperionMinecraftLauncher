<p align="center">
  <img src="https://raw.githubusercontent.com/TechTeaStudio/HyperionMinecraftLauncher/product/icon.png" alt="HyperionMinecraftLauncher logo" width="160" />
</p>

<h1 align="center">HyperionMinecraftLauncher</h1>

<p align="center">
  A modern Avalonia desktop launcher for Minecraft, built on top of <a href="https://github.com/CmlLib/CmlLib.Core">CmlLib.Core</a>. Microsoft device-code sign-in, an instance manager that unifies user-created modpacks with the official launcher's installed versions, the live news feed from Mojang, the multiplayer server list, a 3D skin and cape viewer, and a liquid-glass Material-on-acrylic UI styled for Minecraft.
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&amp;logoColor=white" />
  <img alt="Avalonia" src="https://img.shields.io/badge/Avalonia-11.2.x-8B5CF6" />
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-blue?logo=windows" />
  <a href="https://github.com/TechTeaStudio/HyperionMinecraftLauncher/actions/workflows/dotnet.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/TechTeaStudio/HyperionMinecraftLauncher/dotnet.yml?branch=product&amp;logo=github&amp;label=build" /></a>
  <a href="LICENSE.txt"><img alt="License" src="https://img.shields.io/badge/license-MIT-blue.svg" /></a>
  <img alt="Tests" src="https://img.shields.io/badge/tests-86%20passing-brightgreen" />
</p>

## Overview

HyperionMinecraftLauncher is a drop-in alternative to the official Mojang launcher with the feature set of Prism / MultiMC for instance management and a UI written from scratch in Avalonia 11. The Core library wraps `CmlLib.Core` behind a single `IMinecraftLauncherService` contract for version listing, install, authentication and process start; the App is a single-window Avalonia front-end that drives that contract through a view-model and renders a Minecraft-styled liquid-glass interface on top.

The launcher reads from and writes alongside the same `.minecraft` directory the official launcher uses, never overwriting Mojang's `launcher_profiles.json` or `launcher_accounts.json`. Its own state (instances, settings, MSAL refresh tokens, cached news + skins) lives under `%LOCALAPPDATA%\HyperionMinecraftLauncher\` so the two launchers can be installed side by side without stepping on each other.

## Highlights

- **Microsoft sign-in (device code).** MSAL-backed flow: a readable user code is shown in a modal, the OAuth dance runs in the system browser, and the resulting Minecraft Services profile is cached so subsequent launches sign in silently. No embedded WebView2 dependency.
- **Offline mode.** `MSession.CreateOfflineSession` for LAN games and offline-mode servers when no Microsoft account is connected.
- **Unified instance manager.** User-created instances and versions already installed under `.minecraft\versions\` show up in one list, with a small "auto" badge on the auto-imported ones. New Instance dialog has a 15-tile Minecraft icon picker.
- **Live news feed** from `launchercontent.mojang.com/news.json` (same source the official launcher uses). 1-hour disk cache, stale-on-failure fallback.
- **Multiplayer server list** parsed from `.minecraft\servers.dat` via fNbt.
- **3D skin and cape viewer** rendered via `MinecraftSkinRender.Image` (pure-Skia path; no OpenGL context needed). Drag-to-rotate head, full-body sprite, real isometric chest icon for the Installations sidebar.
- **Settings page.** Persisted JVM min/max heap, custom JVM args, game-directory override, Java executable override, "keep launcher open" toggle. Sliders auto-cap at 75% of system RAM (16 GB ceiling).
- **Daily-rotated file log** at `%LOCALAPPDATA%\HyperionMinecraftLauncher\logs\launcher-YYYY-MM-DD.log`. Every line shown in the UI also lands on disk, with stack traces on errors.
- **Liquid-glass UI.** `ExperimentalAcrylicBorder` underlay, custom chrome, Material.Avalonia controls with shadow-depth tuned for the acrylic surface, micro-interactions on every card (hover lift, scale-on-press launch button, avatar tilt). Minecraft font for headings, Inter for body.

## How it compares

| Capability | Hyperion | Mojang official | Prism / MultiMC |
|---|---|---|---|
| Microsoft sign-in | yes (device code, no WebView2) | yes (web sign-in) | yes |
| Offline mode | yes | yes | yes |
| Instance manager (user-created) | yes | no (profiles only) | yes |
| Auto-import installed versions into instance list | yes | n/a | partial |
| Per-instance icon picker | yes (15 MC presets) | no | yes |
| News feed (Mojang) | yes | yes | no |
| Multiplayer server list (servers.dat) | yes | yes | yes |
| 3D skin + cape viewer | yes | partial | varies |
| Custom JVM args + memory sliders | yes (UI; forwarding planned) | partial | yes |
| Forge / Fabric / Quilt / NeoForge install | roadmap | yes | yes |
| Modrinth mods browser | roadmap | no | yes |
| Liquid-glass Material UI | yes | partial | no |
| Cross-platform | Windows today, Linux possible | Windows / macOS / Linux | all three |

The honest pitch: Hyperion sits between the official Mojang launcher and Prism / MultiMC. If you want the official launcher's news feed and Microsoft auth path with a Prism-style instance grid and an actually modern UI, this is it. If you primarily need mod-loader install for Forge / Fabric today, stick with Prism until the roadmap items below land.

## Build and run

```bash
dotnet build HyperionMinecraftLauncher.slnx
dotnet test  HyperionMinecraftLauncher.slnx
dotnet run --project src/HyperionMinecraftLauncher.App
```

Requires .NET SDK 10. The Core library multi-targets `net8.0;net9.0;net10.0`; the App is `net10.0` only (Avalonia 11.2.x, WinExe).

### Smoke test (5 minutes)

1. `dotnet run --project src/HyperionMinecraftLauncher.App` opens the launcher window.
2. The launcher auto-refreshes on startup: news, server list, installed versions, instances, the manifest, plus a silent Microsoft sign-in if MSAL still holds a refresh token.
3. Click **Sign in with Microsoft** if you want online play. A modal shows the device code; sign in at the URL it prints; the chip flips to your username and your real skin face replaces Steve's.
4. On the **Home** page pick an instance (the "Your instances" combobox lists both your saved instances and any version already under `.minecraft\versions\`).
5. Click **Launch**. The log textbox streams install progress; Minecraft starts within ~30 s on first launch and instantly thereafter.

## Configuration and storage

| Path | Owner | Purpose |
|---|---|---|
| `%APPDATA%\.minecraft\versions\` | Mojang launcher | Installed version manifests and JARs. Hyperion reads this, never writes. |
| `%APPDATA%\.minecraft\launcher_profiles.json` | Mojang launcher | Profiles shown on the read-only Profiles cross-reference path. Hyperion reads, never writes. |
| `%APPDATA%\.minecraft\servers.dat` | Mojang launcher | Multiplayer server list (NBT). Hyperion reads, never writes. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\instances\{Id}.json` | Hyperion | One JSON file per user-created instance. Atomic writes (write-temp + rename). |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\settings.json` | Hyperion | Persisted launcher settings (RAM, JVM args, game-dir, Java exe overrides). |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.json` | Hyperion | Microsoft account cache, separate from Mojang's `launcher_accounts.json`. |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\cache\` | Hyperion | News + player-skin disk cache (1 h / 6 h TTL with stale-on-failure fallback). |
| `%LOCALAPPDATA%\HyperionMinecraftLauncher\logs\launcher-YYYY-MM-DD.log` | Hyperion | Daily-rotated file log. |
| `%LOCALAPPDATA%\.IdentityService\` | Microsoft MSAL | MSAL refresh-token cache (managed by the library, used for silent sign-in). |

## Project layout

```
HyperionMinecraftLauncher/
+- src/HyperionMinecraftLauncher.Core/         <- library (multi-targets net8/9/10)
|  +- Auth/                                    <- AuthRequest / AuthResult / IMicrosoftAuthService
|  +- Cache/                                   <- FileCache (news + skin)
|  +- Installations/                           <- InstalledVersion, ModLoader scan
|  +- Instances/                               <- Instance record + FileInstanceStore
|  +- Launcher/                                <- IMinecraftLauncherService + CmlLib adapter
|  +- Logging/                                 <- FileLauncherLogger
|  +- News/                                    <- Mojang news feed
|  +- Profiles/                                <- launcher_profiles.json reader
|  +- Servers/                                 <- servers.dat NBT reader
|  +- Settings/                                <- LauncherSettings + store + SystemRam probe
|  +- Versions/                                <- VersionMetadata
+- src/HyperionMinecraftLauncher.App/          <- Avalonia desktop front-end (WinExe, net10)
|  +- Assets/Icons/MC/                         <- bundled Minecraft icon set
|  +- Auth/MicrosoftAuthService.cs             <- MSAL device-code wrapper
|  +- Controls/SkinPreview.cs                  <- 3D skin + cape viewer
|  +- Views/MainWindow.axaml(.cs)              <- single launcher window
|  +- Views/NewInstanceDialog.axaml(.cs)       <- name + version + icon picker modal
|  +- Views/DeviceCodeDialog.axaml(.cs)        <- readable code modal for MSAL flow
|  +- ViewModels/MainViewModel.cs              <- the one view-model behind everything
|  +- ViewModels/AsyncRelayCommand.cs          <- hand-rolled async ICommand
|  +- App.axaml(.cs), Program.cs               <- DI bootstrap + Avalonia entry
+- tests/HyperionMinecraftLauncher.Core.Tests/ <- 84 xUnit tests
+- scripts/                                    <- one-off helpers (Python iso-chest generator)
+- .github/workflows/dotnet.yml                <- build + test on push / PR to product
+- CHANGELOG.md
+- QUICKSTART.md
+- LICENSE.txt
+- README.md
```

## Versioning and release

Both shipping `.csproj` files carry a synchronized `<Version>`:

```xml
<Version>0.26.0</Version>  <!-- src/HyperionMinecraftLauncher.Core/HyperionMinecraftLauncher.Core.csproj -->
<Version>0.26.0</Version>  <!-- src/HyperionMinecraftLauncher.App/HyperionMinecraftLauncher.App.csproj -->
```

The Core library and the App ship together as one application. Both `<Version>` values bump in lock-step; the tests project keeps the default `1.0.0` since it never ships. Format is 3-part SemVer (`X.Y.Z`); commit format is `vX.Y.Z <short description>` capped at 72 characters.

Pushing to `product` triggers `.github/workflows/dotnet.yml` (restore + build + test on Ubuntu, .NET 10). There is no NuGet publish step. This is an application, not a library.

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

## Roadmap

Tracked under the `feat/prism-parity` umbrella and to be picked up in priority order:

- **Mod-loader installer.** Forge / Fabric / Quilt / NeoForge via `CmlLib.Core.Installer.*`. Surface the loader picker in the New Instance dialog and resolve loader versions on demand.
- **Per-instance overrides.** Move RAM / JVM args / game-dir / window resolution from the Settings page onto each `Instance`, with inheritance fallback to the global defaults.
- **Modrinth mods browser.** Search + install mods directly against a Hyperion instance via `Modrinth.Net`.
- **Process-stream piping.** Forward Minecraft's stdout / stderr into the launcher log when "Show game log" is enabled.
- **Cross-platform packaging.** AppImage / .deb / .pkg builds (Avalonia already supports Linux + macOS; only the WinExe target and Windows-only paths are blockers).

## Further reading

- [QUICKSTART.md](QUICKSTART.md): five-minute new-developer walkthrough.
- [CHANGELOG.md](CHANGELOG.md): release notes for every version.
- [ARCHITECT_REVIEW.md](ARCHITECT_REVIEW.md): the internal architectural review.

## License

Licensed under the [MIT License](LICENSE.txt). Copyright &copy; Tech Tea Studio.

<p align="center">
  Built as part of the Hyperion Ecosystem by <a href="https://techteastudio.cc">TechTeaStudio</a>.
</p>
