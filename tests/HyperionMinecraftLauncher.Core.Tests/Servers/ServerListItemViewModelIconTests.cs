using System;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers;

/// <summary>Server icons on <see cref="ServerListItemViewModel"/> rows: servers.dat first, then the live favicon.</summary>
public class ServerListItemViewModelIconTests
{
    private static readonly byte[] CachedIcon = Png(SKColors.Red);
    private static readonly byte[] LiveIcon = Png(SKColors.Blue);
    private static readonly byte[] BlankIcon = Png(SKColors.Transparent);

    [Fact]
    public void Icon_StartsWithTheOneSavedInServersDat()
    {
        var row = new ServerListItemViewModel(Entry(Convert.ToBase64String(CachedIcon)));

        Assert.Equal(CachedIcon, row.IconPng);
        Assert.True(row.HasIcon);
    }

    [Fact]
    public void Icon_IsNullWithoutASavedIconOrWithBrokenData()
    {
        Assert.Null(new ServerListItemViewModel(Entry(null)).IconPng);
        Assert.Null(new ServerListItemViewModel(Entry("not base64 !!")).IconPng);
        Assert.Null(new ServerListItemViewModel(Entry(Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 }))).IconPng);
    }

    [Fact]
    public void Icon_FullyTransparent_CountsAsNoIcon()
    {
        // Some servers (Pepeland among them) send a blank 64x64 icon; show the placeholder instead.
        var row = new ServerListItemViewModel(Entry(Convert.ToBase64String(BlankIcon)));

        Assert.Null(row.IconPng);
        Assert.False(row.HasIcon);
    }

    [Fact]
    public void ApplyStatus_WithFavicon_ReplacesTheIconAndNotifies()
    {
        var row = new ServerListItemViewModel(Entry(Convert.ToBase64String(CachedIcon)));
        var raised = false;
        row.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(ServerListItemViewModel.IconPng);

        row.ApplyStatus(Status(LiveIcon));

        Assert.Equal(LiveIcon, row.IconPng);
        Assert.True(raised);
    }

    [Fact]
    public void ApplyStatus_WithoutUsableFaviconOrOffline_KeepsTheSavedIcon()
    {
        var row = new ServerListItemViewModel(Entry(Convert.ToBase64String(CachedIcon)));

        row.ApplyStatus(Status(null));
        Assert.Equal(CachedIcon, row.IconPng);

        row.ApplyStatus(Status(BlankIcon));
        Assert.Equal(CachedIcon, row.IconPng);

        row.ApplyStatus(null);
        Assert.Equal(CachedIcon, row.IconPng);
    }

    private static ServerListEntry Entry(string? iconBase64) => new() { Name = "Pepeland", Ip = "play.pepeland.net", IconBase64 = iconBase64 };

    private static ServerStatus Status(byte[]? favicon) => new() { OnlinePlayers = 1, MaxPlayers = 10, LatencyMs = 40, FaviconPng = favicon };

    private static byte[] Png(SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
