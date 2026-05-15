# Architect review - issue #8 HyperionMinecraftLauncher

## Verdict

SHIP WITH ZERO CRITICAL FINDINGS - skeleton is sound, the three worker scopes landed cleanly, exception mapping is comprehensive, tests are honest and fast (30/30 in ~340 ms). A few non-blocking polish items below.

## Critical findings (would-block-merge)

- [ ] None.

## Recommended fixes (nice-to-have, non-blocking)

1. **Single-class-per-file: clean.** Each public type lives in its own file. `LauncherException.cs` legitimately groups the abstract base + its four sealed subclasses (`VersionNotFoundException`, `InstallationFailedException`, `GameProcessStartException`, `AuthenticationFailedException`) - they form one logical hierarchy and the file totals ~50 lines. The HhStoryGenerator pilot's `LlmAllDownException.cs` did the same. This is acceptable.

2. **Test-only types in `CmlLibMinecraftLauncherServiceTests.cs` (`FakeUnderlyingLauncher`, `RecordingLogger`)** are declared in the same file as the test class, scoped `internal sealed`. The HhStoryGenerator pilot's `StubProvider` in `LlmChainTests.cs` follows the same convention. Acceptable.

3. **`RecordingLogger` is duplicated.** It appears in `CmlLibMinecraftLauncherServiceTests.cs` only; `MainViewModelTests.cs` constructs it via `new RecordingLogger()` because the test files share a namespace. That works because of file-internal `internal sealed` types being in the same assembly. Verified: the build sees them as one type. Acceptable, but consider extracting a shared `Fakes.cs` file in v0.2 if a fourth test file needs the same helper.

4. **`AsyncRelayCommand._isRunning` is a plain field, not volatile.** UI thread is the only intended consumer, but a tester might invoke `ExecuteAsync` from multiple threadpool tasks. Practically harmless for an MVP; flag and forget.

5. **`SynchronousProgress<T>` is in the App project**, but the deterministic-ordering rationale would let it live in Core. Moving it would also let Core tests reuse it. Optional v0.2 cleanup.

6. **`CmlLibUnderlyingLauncher.GetAllVersionsAsync` is double-sorted.** It sorts our materialized list by `ReleaseTime` descending. `MinecraftLauncher.GetAllVersionsAsync` already returns a `VersionMetadataCollection` whose default sort is newest-first; the explicit sort is defensive but redundant. Keep it - clarity > micro-optimization, but it's worth a comment.

7. **`InstallerEventType` only exposes `Queued` and `Done` in CmlLib 4.0.6.** Our `StageFromInstallerEvent` map is fine for those two; the `_ => "Installing"` fallback covers future additions. The PLAN.md said "Started" / "Progressed" - these don't exist in the package version we resolved. The code matches reality; PLAN.md text is not the source of truth, the code is. Could update PLAN.md but it's a planning artefact, not user-facing.

8. **`CmlLibMinecraftLauncherService.LaunchAsync` reports "Starting game" via `progress?.Report(...)` from the calling thread**, while CmlLib emits its own install progress events through whatever thread the underlying install task uses. With our `SynchronousProgress<T>` wrapper in the VM, all of these route through the VM's `Append` on the reporting thread. Avalonia's binding system marshals the `PropertyChanged` for `LogText` back to the UI thread when displaying, so this is safe. Document this in CLAUDE.md if a maintainer wonders.

9. **`MainWindow.axaml` uses `FontFamily="Cascadia Mono, Consolas, Menlo, monospace"`.** Avalonia 11.2's font fallback list works but the unquoted `monospace` token may not resolve on every distro; Cascadia + Consolas covers Windows, Menlo covers macOS, but Linux users may fall back to the system default. Acceptable for v0.1 - the log textbox renders, just not always in a mono font on Linux. Flag for v0.2 if it matters.

10. **`NU1903` warning on transitive `Tmds.DBus.Protocol` 0.20.0** from Avalonia.X11. Tracked in README and CHANGELOG. Bumping Avalonia (11.3+ if released) will close it.

11. **The Avalonia App is `WinExe`.** On non-Windows, this still works (.NET runtime treats `WinExe` as "no console window please" but doesn't lock the binary to Windows). The README's manual smoke test assumes Windows paths; cross-platform users get the Linux/macOS defaults from `DefaultLogDirectory`. Verified locally that `dotnet build` succeeds on all three TFMs.

## Positive observations

- **Layout is clean and matches HhStoryGenerator conventions:** `src/`, `tests/`, `Directory.Build.props` for shared metadata, `.slnx` for the modern solution file, namespaces tracking folder paths (`TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher` <-> `src/HyperionMinecraftLauncher.Core/Launcher/`).
- **Manual constructor DI throughout - no container, no service locator, no Microsoft.Extensions.DependencyInjection package reference.** Exactly the WordDataBase / ConfigBase / HhStoryGenerator philosophy. `App.axaml.cs` does the wiring inline.
- **`IUnderlyingLauncher` shim is the right abstraction.** Tests never touch CmlLib types directly; the production adapter (`CmlLibUnderlyingLauncher`) is the *only* file in the codebase that imports `CmlLib.Core.*`. Easy to swap, easy to mock.
- **Exception mapping is thorough.** Every catch block has a dedicated case for the most likely runtime failure - `HttpRequestException` (network), `IOException` (disk), `UnauthorizedAccessException` (permissions), `Win32Exception` (Java missing), `FileNotFoundException` (asset missing), `KeyNotFoundException` (bad version) - plus a generic catch-all that still produces a friendly message and chains the original `InnerException`. Every failure path also logs through `ILauncherLogger` *before* throwing.
- **Cancellation is preserved.** Every catch block explicitly rethrows `OperationCanceledException` before mapping anything else. Verified by the `LaunchAsync_CancellationPropagates` test.
- **DTOs use `required` init-only properties** - immutable, allocation-friendly records that can't be left half-built. `LaunchRequest.Session` is `required AuthResult`, so the caller can't accidentally launch without an authenticated session.
- **`FileLauncherLogger` is correctly thread-safe** under its coarse `lock (_sync)`. Cheap, predictable, no async-file-IO complications. Daily rotation is driven by an injectable clock so tests can deterministically span midnight.
- **`DefaultLogDirectory.Resolve()` is null-safe.** Falls back to `%TEMP%/HyperionMinecraftLauncher-data` if `Environment.GetFolderPath` returns an empty string (which happens in some sandboxed test runners).
- **`SynchronousProgress<T>` is the right call.** `System.Progress<T>` would race with the final "Launched." append on test threads where the sync context posts to the threadpool. The custom wrapper makes log line ordering deterministic in both test and production code; Avalonia's `PropertyChanged` re-dispatch covers the UI marshalling.
- **Markdown discipline: clean.** Grep for `^---\s*$` across README, CHANGELOG, CLAUDE, PLAN returns zero hits. None of the files contain the forbidden "neyronnye polosky" separator. Documentation uses `##` / `###` headings throughout.
- **CI workflow** triggers on `product` only (not main/master), uses dotnet 10, runs restore + build + test, no NuGet publish step - correct for an application.
- **`<Version>0.1.0</Version>` in both shipping csprojs is in lock-step**, ready for the first version-bump commit.
- **CmlLib.Core 4.0.6 resolves cleanly on netstandard2.0**, which makes the multi-target Core library (net8.0;net9.0;net10.0) work across all three TFMs without conditional package references. Verified with `dotnet build`.
- **Tests don't touch the network or a real Minecraft install.** Every CmlLib interaction routes through `IUnderlyingLauncher`, which the test substitutes with `FakeUnderlyingLauncher`. The file-logger tests use a temp directory and an injected clock. The view-model tests use `StubLauncherService`. Total runtime: ~340 ms.

## Walkthrough of user flow

User runs `dotnet run --project src/HyperionMinecraftLauncher.App`:

1. `Program.Main` -> `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.
2. Avalonia loads `App.axaml`, calls `App.OnFrameworkInitializationCompleted`. Manual DI:
   - `new FileLauncherLogger(DefaultLogDirectory.Resolve())` - creates `%LOCALAPPDATA%/HyperionMinecraftLauncher/logs` if absent, logs "starting" line.
   - `CmlLibMinecraftLauncherService.Create(logger)` - constructs `CmlLibUnderlyingLauncher` (default `.minecraft` path) + the public service.
   - `new MainViewModel(service, logger)` - sets `Username = "Steve"`, empty version list, commands wired.
3. `MainWindow` opens with `DataContext = viewModel`.
4. User clicks **Refresh versions** -> `RefreshVersionsCommand.ExecuteAsync` -> `service.ListVersionsAsync` -> `CmlLibUnderlyingLauncher.GetAllVersionsAsync` -> `MinecraftLauncher.GetAllVersionsAsync` (Mojang manifest). Returns ~600 versions, projected into `VersionMetadata`, sorted by date descending, repopulates `AvailableVersions`. Log: "Loaded N versions."
5. User picks `1.21.5`, clicks **Launch** -> `LaunchCommand.ExecuteAsync`:
   - `service.AuthenticateAsync({Offline, "Steve"})` -> `MSession.CreateOfflineSession("Steve")` -> `AuthResult { Username = "Steve", Uuid = <derived>, AccessToken = "0", IsOffline = true }`.
   - `service.LaunchAsync(request, progress, ct)`:
     - `_underlying.InstallAsync("1.21.5", progress, ct)` -> `MinecraftLauncher.InstallAsync` with `fileProgress` and `byteProgress` wired through `SyncProgress<T>` adapters that re-emit as `LaunchProgress`. Each event flows to the VM via `SynchronousProgress<T>` and gets appended to `LogText` in-order.
     - `progress?.Report(new LaunchProgress { Stage = "Starting game" })`.
     - `_underlying.StartProcessAsync("1.21.5", "Steve", uuid, "0", null, null, ct)` -> `MinecraftLauncher.BuildProcessAsync` -> `process.Start()` -> returns pid.
   - VM appends `"Launched. pid={pid} version=1.21.5"`.
6. The Minecraft window opens.

End-to-end flow is **complete and wired correctly**. Issue #8 acceptance criteria met:
- User login: offline path works; Microsoft/Mojang paths documented as out-of-scope for v0.1 with friendly exceptions.
- Version selection + download: `ListVersionsAsync` populates dropdown; `LaunchAsync` installs the chosen version.
- Launch the game: `StartProcessAsync` spawns the Java process; pid returned.
- Error handling with meaningful messages: every CmlLib failure path is mapped to a friendly `LauncherException` subclass with diagnostic context.
- Logging: `FileLauncherLogger` writes daily-rotated files; every public service method logs both happy-path and failure events.

## Per-check status

1. **Single-class-per-file:** PASS - `LauncherException.cs` is an exception hierarchy (acceptable by HhStoryGenerator precedent); every other file holds exactly one public type.
2. **Namespaces match folder paths:** PASS - all eight Core namespaces (`Auth`, `Launcher`, `Logging`, `Versions`) and three App namespaces (root, `ViewModels`, `Views`) align with their directories.
3. **DI / injection style:** PASS - manual constructor injection, no container, services constructed in `App.axaml.cs`. `IUnderlyingLauncher`, `ILauncherLogger`, and `IMinecraftLauncherService` are all injectable; tests substitute fakes.
4. **Configuration / env vars:** N/A - the launcher has no env-var-driven config in v0.1. Log directory is platform-default; game directory is CmlLib's default `.minecraft`.
5. **Markdown discipline (no `---` body separators):** PASS - zero matches across README, CHANGELOG, CLAUDE, PLAN, ARCHITECT_REVIEW.
6. **Commit message readiness (`<Version>0.1.0` lock-step):** PASS - Core and App csprojs both at `0.1.0`, `CHANGELOG.md` has `## [0.1.0]` entry that accurately describes the shipped functionality (not just the skeleton, unlike HhStoryGenerator pilot's drift).
7. **CI workflow (`product` branch, dotnet 10, restore + build + test, no publish):** PASS - `.github/workflows/dotnet.yml` triggers on push & PR to `product` only, uses `actions/setup-dotnet@v4` with `10.0.x`, runs the three expected steps.
8. **Exception types** (sealed Exception subclasses with diagnostics): PASS - abstract `LauncherException` base + four sealed subclasses; `VersionNotFoundException` carries `VersionName`; all subclasses preserve `InnerException` where applicable.
9. **Cancellation / cooperative cancellation:** PASS - every async public method on Core takes `CancellationToken`; `CmlLibMinecraftLauncherService` explicitly rethrows `OperationCanceledException` ahead of any other catch. View-model uses `CancellationToken.None` for now (acceptable for v0.1; document for v0.2).
10. **HTTP hygiene:** N/A directly - we delegate to CmlLib for all HTTP. CmlLib uses its own `HttpClient` (configurable via `MinecraftLauncherParameters.HttpClient`); v0.2 could inject a UA-tagged client.
11. **README quality:** PASS - concise, no `---`, lists prerequisites, manual smoke test plan, known limitations, build instructions, documents `%LOCALAPPDATA%` log path.
12. **PLAN.md / CLAUDE.md consistency with shipped code:** MOSTLY PASS - every file PLAN.md said would exist, exists, with the documented contract. PLAN.md's "Started/Progressed" `InstallerEventType` enum values are aspirational (CmlLib 4.0.6 only has `Queued` and `Done`); code matches reality.
13. **`Placeholder.cs` cleanup:** PASS - deleted after Worker C's tests landed.
14. **Issue #8 fulfillment:** PASS - walkthrough above confirms each acceptance criterion is met, with the Microsoft/Mojang auth limitation honestly documented.
15. **Subtle bugs / code smells:**
    - JSON option consistency: N/A - no JSON in this app.
    - Cancellation swallowed: NONE FOUND - explicit rethrow in every catch.
    - Stream / HttpClient lifecycles: N/A - CmlLib manages its own.
    - Catch-all `catch (Exception)`: present, but only after every specific case has been tried and only to produce a friendly wrapped exception with the original chained; not a silencer.
    - Race conditions: NONE FOUND - `SynchronousProgress<T>` eliminates the `Progress<T>` post-to-context race that bit us in the first test run.
