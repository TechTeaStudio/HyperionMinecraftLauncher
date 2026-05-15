# Implementation plan — HyperionMinecraftLauncher

This plan partitions the work across **three worker agents that run in parallel** after the planner finishes. Each worker owns a disjoint set of files. The shared types laid down by the skeleton (`IMinecraftLauncherService`, `IUnderlyingLauncher`, `ILauncherLogger`, `AuthMode/Request/Result`, `LaunchRequest/Result/Progress`, `LauncherException` hierarchy, `VersionMetadata`) are **fixed contracts** — workers must not modify them, only consume them.

Convention used everywhere:

- C# 12+ records with `init` setters for DTOs; sealed classes for services.
- `Nullable enable`, `ImplicitUsings enable` (already in `Directory.Build.props`).
- One class / record / interface per file (Microsoft style).
- TDD: red - green - refactor. Each worker writes their own xUnit file in `tests/HyperionMinecraftLauncher.Core.Tests/`.
- Tests **must not** touch the network, real Minecraft installs, or real Microsoft / Mojang auth endpoints. Use the `IUnderlyingLauncher` shim.

## Worker A - CmlLib service + exception mapping

### Files to create

- `src/HyperionMinecraftLauncher.Core/Launcher/CmlLibUnderlyingLauncher.cs` - real `IUnderlyingLauncher` implementation that owns a `CmlLib.Core.MinecraftLauncher` instance and translates between our DTOs and CmlLib's API.
- `src/HyperionMinecraftLauncher.Core/Launcher/CmlLibMinecraftLauncherService.cs` - public `IMinecraftLauncherService` implementation. Constructor takes `(IUnderlyingLauncher underlying, ILauncherLogger logger)`. Exposes a convenience static factory `Create()` for the App's manual DI that constructs the CmlLib-backed underlying launcher.
- `tests/HyperionMinecraftLauncher.Core.Tests/CmlLibMinecraftLauncherServiceTests.cs`.

### Files NOT to touch

- Anything under `Auth/`, `Logging/`, `Versions/`, or the interfaces themselves.
- Worker B / C files.

### Implementation notes

- `ListVersionsAsync`: calls `underlying.GetAllVersionsAsync(ct)`, returns the sorted list (release dates descending, then name).
- `AuthenticateAsync`:
  - `AuthMode.Offline`: build an `AuthResult` from `CmlLib.Core.Auth.MSession.CreateOfflineSession(username)`. Map its `Username/UUID/AccessToken` into our DTO. `IsOffline = true`.
  - `AuthMode.Microsoft` / `AuthMode.Mojang`: throw `AuthenticationFailedException("Microsoft / Mojang auth is not implemented in v0.1.")` - the README documents this as a known limitation pending a Microsoft Client ID.
- `LaunchAsync`:
  1. `await underlying.InstallAsync(request.VersionName, progress, ct)` - the shim is responsible for surfacing CmlLib progress events as `LaunchProgress`.
  2. `int pid = await underlying.StartProcessAsync(...)`.
  3. Return `new LaunchResult { ProcessId = pid, VersionName = request.VersionName }`.
- Exception mapping (in both `CmlLibUnderlyingLauncher` and the service):
  - `InvalidOperationException` / `KeyNotFoundException` from version lookup -> `VersionNotFoundException`.
  - `HttpRequestException`, `IOException`, `UnauthorizedAccessException` during install -> `InstallationFailedException`.
  - `System.ComponentModel.Win32Exception`, missing-Java errors during process start -> `GameProcessStartException`.
  - Anything else from CmlLib that's not a `LauncherException` or `OperationCanceledException` is wrapped in `InstallationFailedException` with a friendly message and the original as inner.
- All friendly exceptions are logged via the injected `ILauncherLogger` *before* being thrown (so the log file always has the failure even if the caller swallows).

### Acceptance criteria (tests, in `CmlLibMinecraftLauncherServiceTests.cs`)

- A tiny in-test `FakeUnderlyingLauncher : IUnderlyingLauncher` plus `RecordingLogger : ILauncherLogger`.
- `ListVersionsAsync` returns the list the fake produced, unchanged in order.
- `AuthenticateAsync(Offline)` produces `Username = "Steve"`, non-empty `Uuid`, non-empty `AccessToken`, `IsOffline = true`.
- `AuthenticateAsync(Microsoft)` -> `AuthenticationFailedException` and the message mentions "Microsoft".
- `LaunchAsync` happy-path calls `InstallAsync` *then* `StartProcessAsync` and returns the pid.
- `LaunchAsync` propagates the `LaunchProgress` events the fake emits during install (assert at least one `Stage = "Downloading"` and one `Stage = "Starting game"` value made it through `IProgress<LaunchProgress>`).
- If the fake throws `KeyNotFoundException` from `InstallAsync`, the service throws `VersionNotFoundException` whose `VersionName` equals the request.
- If the fake throws `HttpRequestException` from `InstallAsync`, the service throws `InstallationFailedException` with the original as `InnerException`.
- If the fake throws `System.ComponentModel.Win32Exception` from `StartProcessAsync`, the service throws `GameProcessStartException`.
- Every failure path is also logged via the `RecordingLogger` (assert the captured `Error` calls include the version name).

## Worker B - File logger + DTO polish

### Files to create

- `src/HyperionMinecraftLauncher.Core/Logging/FileLauncherLogger.cs` - thread-safe daily-rotated file logger writing to `{LogDirectory}/launcher-YYYY-MM-DD.log`. Constructor takes `(string logDirectory, Func<DateTimeOffset>? clock = null)`. Creates the directory if missing. Default clock = `DateTimeOffset.UtcNow`.
- `src/HyperionMinecraftLauncher.Core/Logging/DefaultLogDirectory.cs` - static helper exposing `Resolve()` returning the platform path (`%LOCALAPPDATA%/HyperionMinecraftLauncher/logs` on Windows, `~/.local/share/HyperionMinecraftLauncher/logs` on Unix, via `Environment.SpecialFolder.LocalApplicationData`).
- `tests/HyperionMinecraftLauncher.Core.Tests/FileLauncherLoggerTests.cs`.

### Files NOT to touch

- Auth DTOs - already final from skeleton.
- Launcher DTOs - already final from skeleton.

### Implementation notes

- Log line format: `YYYY-MM-DD HH:mm:ss.fff [LEVEL] <message>` and, on errors, a second line with `at <exception ToString()>`.
- Use a `lock (_sync)` around the `File.AppendAllText` call - cheap, predictable, no async-file-IO complications. The launcher logs are low-volume.
- The log file path is derived from the supplied clock so the test can inject a fixed date and assert rotation.
- When the supplied directory doesn't exist, create it (`Directory.CreateDirectory`).
- The class is `sealed` and implements `IDisposable` for symmetry, but the implementation is currently a no-op (no held handles).

### Acceptance criteria (tests, in `FileLauncherLoggerTests.cs`)

- Writing one Info line and one Error line to a temp directory creates `launcher-2026-05-15.log` with both messages (use a `Func<DateTimeOffset>` clock fixed at 2026-05-15 12:00:00Z).
- An `Error` call with an exception writes the exception's `ToString()` on the next line.
- Switching the clock to the next day and writing again creates a *second* file `launcher-2026-05-16.log`; the first file still contains only the original lines.
- The logger creates the directory if it doesn't exist (start from a temp path that doesn't pre-exist; assert directory exists after construction or after the first write).
- `DefaultLogDirectory.Resolve()` returns a path that ends with `/HyperionMinecraftLauncher/logs` on the current platform (substring match, OS-separator-agnostic).

## Worker C - Avalonia App: window, view-model, manual DI

### Files to create

- `src/HyperionMinecraftLauncher.App/Views/MainWindow.axaml` (+ `.axaml.cs`) - single window with: username `TextBox` bound to `Username`, `ComboBox` of `AvailableVersions` bound to `SelectedVersion`, button `Refresh versions` bound to `RefreshVersionsCommand`, button `Launch` bound to `LaunchCommand`, multi-line read-only `TextBox` bound to `LogText`. Designer-friendly with `d:DataContext`.
- `src/HyperionMinecraftLauncher.App/ViewModels/MainViewModel.cs` - `INotifyPropertyChanged` (manual, no MVVM toolkit dependency unless restored cheap; `AsyncRelayCommand` hand-rolled). Properties: `Username` (default `Steve`), `AvailableVersions` (`ObservableCollection<VersionMetadata>`), `SelectedVersion`, `LogText`, `IsBusy`. Commands: `RefreshVersionsCommand`, `LaunchCommand` (`CanExecute = !IsBusy && SelectedVersion != null`). Constructor takes `(IMinecraftLauncherService service, ILauncherLogger logger)`. The view-model **appends** every launcher event into `LogText` so a manual smoke test surfaces install progress in the UI.
- `src/HyperionMinecraftLauncher.App/ViewModels/AsyncRelayCommand.cs` - tiny `ICommand` impl.
- Replace `src/HyperionMinecraftLauncher.App/App.axaml.cs` real wiring: construct `FileLauncherLogger` (path via `DefaultLogDirectory.Resolve()`) and `CmlLibMinecraftLauncherService.Create(logger)`, build `MainViewModel`, set as `MainWindow.DataContext`, show the window.
- `tests/HyperionMinecraftLauncher.Core.Tests/MainViewModelTests.cs`.

### Files NOT to touch

- Any Core service implementations (Worker A's territory).
- The `FileLauncherLogger` (Worker B's territory).

### Implementation notes

- The view-model lives in the **App** project but the **tests** are in the Core.Tests project. The test project already references the App project (see csproj), so this is fine.
- Avoid Avalonia threading in the view-model: do all async work on the threadpool; the UI binding layer marshals back.
- The `LogText` mutation must be `string` re-assignment, not `StringBuilder` mutation - WPF/Avalonia bindings need property-changed notifications.
- `RefreshVersionsCommand` clears `AvailableVersions` then awaits `service.ListVersionsAsync(ct)`, repopulates, logs count.
- `LaunchCommand`:
  1. `auth = await service.AuthenticateAsync(new AuthRequest { Mode = Offline, Username = Username }, ct);`
  2. `await service.LaunchAsync(new LaunchRequest { VersionName = SelectedVersion!.Name, Session = auth }, progress, ct);`
  3. `progress` callback appends `progress.Stage` (+ optional fraction) to `LogText`.
  4. Catch every `LauncherException` and append `[error] <message>` instead of letting it bubble.

### Acceptance criteria (tests, in `MainViewModelTests.cs`)

- A `StubLauncherService : IMinecraftLauncherService` (returns 3 versions, fake `LaunchAsync` emits one progress event then returns).
- Constructor populates default `Username = "Steve"`, empty `AvailableVersions`, `IsBusy = false`.
- `RefreshVersionsCommand` populates `AvailableVersions` with the stub's 3 entries.
- `LaunchCommand.CanExecute` is `false` while `SelectedVersion is null`, becomes `true` after a version is chosen.
- A full `LaunchCommand.Execute` cycle (with `SelectedVersion` set) ends with `LogText` containing the progress stage strings and a final "Launched" line; `IsBusy` returns to `false`.
- If `service.LaunchAsync` throws an `AuthenticationFailedException`, `LogText` ends with `[error] ...`, no exception propagates, `IsBusy` returns to `false`.

## Coordination

- All three workers run **in parallel** after the planner finishes.
- Each worker only touches files in its own section above. Shared types are **frozen** - if a worker needs a field that isn't on a DTO, stop and raise it rather than editing unilaterally; the planner re-runs to update the contract.
- Tests live in `tests/HyperionMinecraftLauncher.Core.Tests`. Each worker adds their own test file so there are no merge conflicts. Workers may delete `Placeholder.cs` once a real test exists.
- After all workers finish, the **architect** reviews for convention compliance (single-class-per-file, namespaces, manual DI, no markdown horizontal-rules) and the **tester** runs `dotnet build` and `dotnet test` against `HyperionMinecraftLauncher.slnx`.
- Commit message format (when a commit is taken): `vX.Y.Z <short description>` - bump versions in **all three** csprojs before committing.

## Open documented limitations (v0.1)

- **No real Microsoft auth.** The `MicrosoftAuthenticator` flow requires a Microsoft-issued Azure app Client ID, which is provisioned per-organization. v0.1 ships offline auth only; the `AuthMode.Microsoft` path throws a friendly `AuthenticationFailedException`. Add a Client ID and use `CmlLib.Core.Auth.Microsoft` (NuGet) in v0.2.
- **No Mojang auth.** Mojang shut down its username+password endpoint in 2022. The enum value is retained for completeness; the launcher throws on use.
- **Avalonia app is smoke-tested manually.** There is no UI automation test; the headless test project tests the view-model directly. A manual test plan ("type a username, click Refresh, pick `1.21.5`, click Launch, watch the log") lives in the README.
- **NU1903 vulnerability warning** on `Tmds.DBus.Protocol` 0.20.0 (a transitive of Avalonia.X11). Avalonia 11.2.3 still pulls it; upstream fix tracked at https://github.com/AvaloniaUI/Avalonia/issues - update Avalonia in v0.2.
