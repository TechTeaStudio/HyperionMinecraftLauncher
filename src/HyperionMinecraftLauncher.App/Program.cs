using Avalonia;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using TechTeaStudio.HyperionMinecraftLauncher.App.Cli;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized yet.
    [STAThread]
    public static void Main(string[] args)
    {
        // CLI mode (v0.28 T11.5): when any "--" flag appears in argv, skip Avalonia entirely
        // and run the headless dispatcher. On Windows the App is a WinExe with no console
        // attached, so we manually re-attach to the parent's console so stdout reaches the
        // calling shell.
        if (args.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
        {
            TryAttachParentConsole();

            // Minimal DI for headless runs: just the file logger + the CmlLib service. No view-model,
            // no Avalonia, no Microsoft auth (CLI launches are offline-only on purpose).
            var logger = new FileLauncherLogger(DefaultLogDirectory.Resolve());
            var service = new CmlLibMinecraftLauncherService(new CmlLibUnderlyingLauncher(), logger);
            var rc = CliEntryPoint.RunAsync(args, service, logger).GetAwaiter().GetResult();
            Environment.Exit(rc);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// On Windows a WinExe doesn't get a console by default. Calling <c>AttachConsole</c> with
    /// the special <c>ATTACH_PARENT_PROCESS</c> value (-1) hands us the parent shell's console so
    /// <c>Console.WriteLine</c> shows up where the user expects it. No-op on non-Windows OSes
    /// (the dotnet entry is already a regular console binary on Linux/macOS so stdout works).
    /// </summary>
    private static void TryAttachParentConsole()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            // ATTACH_PARENT_PROCESS == -1
            _ = NativeMethods.AttachConsole(-1);
        }
        catch
        {
            // P/Invoke failure is non-fatal; stdout just won't reach the shell, which the user
            // will notice but won't crash anything.
        }
    }

    // Avalonia configuration. Also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int dwProcessId);
    }
}
