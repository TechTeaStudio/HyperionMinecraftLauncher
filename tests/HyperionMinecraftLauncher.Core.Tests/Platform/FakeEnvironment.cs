using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Platform;

/// <summary>
/// Test double for <see cref="IEnvironment"/>: every value is set explicitly
/// so the test never reads the host process's real environment.
/// </summary>
internal sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string?> _vars = new(StringComparer.Ordinal);
    private readonly Dictionary<Environment.SpecialFolder, string> _folders = new();

    public OSPlatform CurrentPlatform { get; set; } = OSPlatform.Linux;

    public FakeEnvironment SetVar(string name, string? value)
    {
        _vars[name] = value;
        return this;
    }

    public FakeEnvironment SetFolder(Environment.SpecialFolder folder, string path)
    {
        _folders[folder] = path;
        return this;
    }

    public string? GetEnvironmentVariable(string name)
        => _vars.TryGetValue(name, out var v) ? v : null;

    public string GetFolderPath(Environment.SpecialFolder folder)
        => _folders.TryGetValue(folder, out var p) ? p : string.Empty;
}
