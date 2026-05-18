using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Localization;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Localization;

public class ResxLocalizationServiceTests
{
    /// <summary>
    /// Minimal in-memory <see cref="ResourceManager"/> stand-in. Lets the tests drive the
    /// service without touching a real .resx file. Each culture name (or "" for the canonical
    /// invariant / English set) maps to its own key->value dictionary; unknown cultures fall
    /// through to the invariant set, mirroring the standard satellite-assembly fallback chain.
    /// </summary>
    private sealed class FakeResourceManager : ResourceManager
    {
        private readonly Dictionary<string, Dictionary<string, string>> _cultures;

        public FakeResourceManager(Dictionary<string, Dictionary<string, string>> cultures)
        {
            _cultures = cultures;
        }

        public override string? GetString(string name, CultureInfo? culture)
        {
            culture ??= CultureInfo.InvariantCulture;
            for (var c = culture; c != null; c = c.Parent.Equals(c) ? null : c.Parent)
            {
                if (_cultures.TryGetValue(c.Name, out var dict) && dict.TryGetValue(name, out var val))
                    return val;
                if (c.Equals(CultureInfo.InvariantCulture)) break;
            }
            // Final fallback: explicit invariant ("" key) dictionary
            return _cultures.TryGetValue(string.Empty, out var inv) && inv.TryGetValue(name, out var v) ? v : null;
        }
    }

    private static ResxLocalizationService MakeService(
        Dictionary<string, Dictionary<string, string>> cultures,
        string initialCulture,
        params string[] available)
    {
        var rm = new FakeResourceManager(cultures);
        IReadOnlyList<string> list = available.Length == 0 ? new[] { "en" } : available;
        return new ResxLocalizationService(rm, initialCulture, list);
    }

    [Fact]
    public void Indexer_DefaultCulture_ReturnsEnglishSource()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["MainWindow.Title"] = "Hyperion Minecraft Launcher" },
            },
            initialCulture: "en");

        Assert.Equal("Hyperion Minecraft Launcher", svc["MainWindow.Title"]);
    }

    [Fact]
    public void Indexer_UnknownKey_ReturnsKeyItself()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["Known"] = "Known value" },
            },
            initialCulture: "en");

        Assert.Equal("Sidebar.Missing", svc["Sidebar.Missing"]);
    }

    [Fact]
    public void Get_WithFormatArgs_ProducesExpectedInterpolation()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["Logs.LineCountFooter"] = "{0} lines - last refreshed {1}" },
            },
            initialCulture: "en");

        Assert.Equal(
            "42 lines - last refreshed 12:34:56",
            svc.Get("Logs.LineCountFooter", 42, "12:34:56"));
    }

    [Fact]
    public async Task SetCultureAsync_UnknownCulture_FallsBackToEnglishNoThrow()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["MainWindow.Title"] = "Hyperion Minecraft Launcher" },
            },
            initialCulture: "en",
            available: new[] { "en" });

        // Switching to a culture that has no resx file should not throw. The lookup keeps
        // returning the English source string.
        await svc.SetCultureAsync("zz-ZZ-not-real", CancellationToken.None);
        Assert.Equal("Hyperion Minecraft Launcher", svc["MainWindow.Title"]);
    }

    [Fact]
    public async Task SetCultureAsync_FiresLanguageChangedExactlyOncePerSwitch()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["Hi"] = "Hello" },
                ["ru"] = new() { ["Hi"] = "Privet" },
            },
            initialCulture: "en",
            available: new[] { "en", "ru" });

        var raised = 0;
        svc.LanguageChanged += (_, _) => raised++;

        await svc.SetCultureAsync("ru", CancellationToken.None);
        Assert.Equal(1, raised);

        // Same culture again: no second event.
        await svc.SetCultureAsync("ru", CancellationToken.None);
        Assert.Equal(1, raised);

        // Switching back: another single event.
        await svc.SetCultureAsync("en", CancellationToken.None);
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task SetCultureAsync_RuCulture_ReturnsRussianString()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["Hi"] = "Hello" },
                ["ru"] = new() { ["Hi"] = "Privet" },
            },
            initialCulture: "en",
            available: new[] { "en", "ru" });

        Assert.Equal("Hello", svc["Hi"]);
        await svc.SetCultureAsync("ru", CancellationToken.None);
        Assert.Equal("Privet", svc["Hi"]);
        Assert.Equal("ru", svc.CurrentCulture);
    }

    [Fact]
    public void AvailableCultures_ReturnsConstructorArgument()
    {
        var svc = MakeService(
            new Dictionary<string, Dictionary<string, string>>
            {
                [string.Empty] = new() { ["x"] = "X" },
            },
            initialCulture: "en",
            available: new[] { "en", "ru", "uk", "pl" });

        Assert.Equal(new[] { "en", "ru", "uk", "pl" }, svc.AvailableCultures.ToArray());
    }
}
