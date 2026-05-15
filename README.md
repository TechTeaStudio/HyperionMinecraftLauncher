# HyperionMinecraftLauncher

A minimal CmlLib-backed Minecraft launcher: a small Avalonia desktop app on top of a Core library that wraps `CmlLib.Core` for version listing, install, and process start.

## Status

v0.1 - MVP, offline auth only, single launcher window, daily-rotated file log.

## How it works

```
Avalonia window  --> MainViewModel  --> IMinecraftLauncherService
                                         |
                                         +-- ListVersionsAsync -> CmlLib MojangVersionLoader
                                         +-- AuthenticateAsync (offline) -> MSession.CreateOfflineSession
                                         +-- LaunchAsync -> Install + spawn Java process
                                                              |
                                                              +-- IProgress<LaunchProgress> -> log textbox
                                                              +-- LauncherException -> log + UI message
```

Every launcher event also lands in `%LOCALAPPDATA%/HyperionMinecraftLauncher/logs/launcher-YYYY-MM-DD.log`.

## Prerequisites

- A Minecraft account is required for online play. The launcher's v0.1 offline mode is fine for LAN / offline-mode servers but cannot join paid online servers.
- A working **Java runtime** is required for older Minecraft versions (CmlLib provisions a JRE for modern versions automatically when one isn't on `PATH`).
- A free `.minecraft` directory (or write access to the default OS path under `%APPDATA%\.minecraft` on Windows / `~/.minecraft` on Unix).

## Install / build

```bash
dotnet build HyperionMinecraftLauncher.slnx
dotnet test  HyperionMinecraftLauncher.slnx
dotnet run --project src/HyperionMinecraftLauncher.App
```

Requires .NET SDK 10. The Core library multi-targets `net8.0;net9.0;net10.0`; the App is `net10.0` only (Avalonia 11.2.x).

## Manual smoke test

1. `dotnet run --project src/HyperionMinecraftLauncher.App` - the launcher window opens.
2. Click **Refresh versions** - the list populates with release + snapshot ids fetched from Mojang's manifest.
3. Type a username (default `Steve`) and pick a version (try a small classic like `1.21.5`).
4. Click **Launch**. The log textbox shows install progress (`Downloading library ...`, `Extracting native ...`, `Starting game`) and the Minecraft window starts within ~30 s on first launch (cached afterwards).
5. Open `%LOCALAPPDATA%/HyperionMinecraftLauncher/logs/launcher-<today>.log` - every line you saw in the textbox is also there, plus stack traces for any errors.

## Known limitations in v0.1

- **No Microsoft / Mojang auth.** `AuthMode.Microsoft` throws `AuthenticationFailedException`. Real Microsoft auth needs an Azure-app Client ID, which is provisioned per organisation; v0.2 will plug `CmlLib.Core.Auth.Microsoft` in once the Client ID is registered.
- **Mojang username+password auth was discontinued upstream in 2022.** The enum value is retained for completeness; the launcher throws on use.
- **Single window, no settings page.** Game directory and RAM settings are hard-defaulted; expose them in v0.2.
- **No UI automation tests.** The view-model is unit-tested directly; the window is smoke-tested manually.
- Build warns `NU1903` on a transitive `Tmds.DBus.Protocol` 0.20.0 (pulled by Avalonia.X11). Tracked upstream; bump Avalonia in v0.2.

## Release flow

CI builds and tests on every push / PR to the `product` branch. There is no NuGet publish step - this is an application, not a library.

## License

MIT. See `LICENSE.txt`.
