# Quickstart — HyperionMinecraftLauncher

Minimal Avalonia desktop launcher around `CmlLib.Core` 4.0.6. Pick a Minecraft version, click Launch, watch the log.

## Prerequisites
- .NET SDK 10.0+
- A real Minecraft account (Microsoft); Mojang auth was discontinued upstream in 2022
- Java runtime (for older Minecraft versions only — modern versions ship their own)

## Build
```powershell
dotnet build HyperionMinecraftLauncher.slnx
```

## Test
```powershell
dotnet test HyperionMinecraftLauncher.slnx
```
Expected: 30 passed, 0 failed. Four `NU1903` warnings from `Tmds.DBus.Protocol` (transitive of Avalonia) are documented and expected — fix tracked for v0.2 (Avalonia bump).

## Run the launcher
```powershell
dotnet run --project src/HyperionMinecraftLauncher.App
```
Use the single window: enter a username, pick a version from the dropdown, hit **Launch**. Logs stream to the bottom textbox and to `%LOCALAPPDATA%/HyperionMinecraftLauncher/logs/launcher-YYYY-MM-DD.log`.

## Known limitations (v0.1.0)
- **Microsoft auth flow is not wired** — `AuthMode.Microsoft` throws `AuthenticationFailedException`. Wiring requires an Azure-app Client ID we do not ship. Track for v0.2.
- **Mojang auth** also throws (upstream discontinued).
- UI is smoke-tested manually; there is no automated UI test.

## Embed the launcher service in your own app
```csharp
using var underlying = new CmlLibUnderlyingLauncher();
var logger = new FileLauncherLogger(DefaultLogDirectory.Get());
var launcher = new CmlLibMinecraftLauncherService(underlying, logger);

var versions = await launcher.ListVersionsAsync(ct);
var request   = new LaunchRequest("1.20.4", new AuthRequest(AuthMode.Mojang, "username"));
var result    = await launcher.LaunchAsync(request, progress: null, ct);
```
