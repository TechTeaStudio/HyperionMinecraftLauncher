using System;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

/// <summary>
/// One user-created Hyperion launcher instance. This is our own model (separate from
/// Mojang's <c>launcher_profiles.json</c>) - storage layout is one JSON file per instance
/// under <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\instances\{Id}.json</c>, which means
/// add / edit / delete only touch the one file and never rewrite a giant master list.
/// </summary>
public sealed record Instance
{
    /// <summary>Stable id - we generate a GUID at creation and never change it.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name (shown on the instance tile).</summary>
    public required string Name { get; init; }

    /// <summary>Minecraft version id (e.g. <c>"1.20.4"</c>). Matches a manifest entry or an installed version.</summary>
    public required string VersionId { get; init; }

    /// <summary>Mod loader (vanilla = <see cref="ModLoader.None"/>).</summary>
    public ModLoader Loader { get; init; } = ModLoader.None;

    /// <summary>Loader version (e.g. <c>"47.4.5"</c> for Forge). Ignored when <see cref="Loader"/> is None.</summary>
    public string? LoaderVersion { get; init; }

    /// <summary>Icon key - matches a file under <c>Assets/Icons/MC/{IconKey}.png</c>. See
    /// <see cref="InstanceIcons"/> for the curated set.</summary>
    public string IconKey { get; init; } = InstanceIcons.GrassBlock;

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last successful launch (null = never played).</summary>
    public DateTimeOffset? LastPlayedAt { get; init; }

    /// <summary>Per-instance game directory override. Null = use shared <c>.minecraft</c>.</summary>
    public string? GameDirectory { get; init; }

    /// <summary>Per-instance JVM args. Null = inherit from <see cref="Core.Settings.LauncherSettings"/>.</summary>
    public string? JvmArguments { get; init; }

    /// <summary>Per-instance min heap. Null = inherit.</summary>
    public int? MinimumRamMb { get; init; }

    /// <summary>Per-instance max heap. Null = inherit.</summary>
    public int? MaximumRamMb { get; init; }

    /// <summary>Window width override.</summary>
    public int? ResolutionWidth { get; init; }

    /// <summary>Window height override.</summary>
    public int? ResolutionHeight { get; init; }
}

/// <summary>Curated icon keys. The view-side resolves these to <c>avares://...</c> URIs.</summary>
public static class InstanceIcons
{
    public const string GrassBlock = "grass_block_side";
    public const string Dirt = "dirt";
    public const string Stone = "stone";
    public const string Cobblestone = "cobblestone";
    public const string Planks = "oak_planks";
    public const string Chest = "chest_iso";
    public const string Compass = "compass";
    public const string Clock = "clock";
    public const string Redstone = "redstone";
    public const string DiamondPickaxe = "diamond_pickaxe";
    public const string IronPickaxe = "iron_pickaxe";
    public const string Book = "book";
    public const string WritableBook = "writable_book";
    public const string Comparator = "comparator";
    public const string EnderPearl = "ender_pearl";

    /// <summary>All preset keys in a stable order - drives the icon picker grid.</summary>
    public static readonly string[] All =
    {
        GrassBlock, Dirt, Stone, Cobblestone, Planks,
        Chest, Compass, Clock, Redstone,
        DiamondPickaxe, IronPickaxe,
        Book, WritableBook, Comparator, EnderPearl,
    };
}
