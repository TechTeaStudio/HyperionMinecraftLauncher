using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.CrashReports;

/// <summary>
/// Drives the parser through two hand-crafted crash reports that mirror what Minecraft actually
/// emits: a Forge 1.20.1 crash whose stack trace points at Sodium, and a Fabric 1.21 crash whose
/// mods-loaded block lists a culprit by hyphen-prefix. The fixtures live inline as multi-line
/// strings so the test class is self-contained.
/// </summary>
public class MinecraftCrashReportParserTests
{
    [Fact]
    public async Task ParseAsync_ForgeReport_ExtractsVersionSummaryAndRanksSodiumFirst()
    {
        var fixture = ForgeSodiumReport;
        var tmp = Path.Combine(Path.GetTempPath(), "crash-2024-08-12_14.05.33-client.txt");
        await File.WriteAllTextAsync(tmp, fixture);
        try
        {
            var parser = new MinecraftCrashReportParser();
            var report = await parser.ParseAsync(tmp, CancellationToken.None);

            Assert.Equal("1.20.1", report.MinecraftVersion);
            Assert.NotNull(report.ForgeOrFabricVersion);
            Assert.Contains("Forge", report.ForgeOrFabricVersion!);
            Assert.Equal("// Why did you do that?", report.SummaryLine);
            // The first suspect frame is the Sodium one; vanilla minecraft frames are ignored.
            Assert.NotEmpty(report.SuspectedMods);
            Assert.Equal("sodium", report.SuspectedMods[0].ModId);
            Assert.NotNull(report.SuspectedMods[0].ModrinthSearchUrl);
            Assert.Contains("modrinth.com/mod?q=sodium", report.SuspectedMods[0].ModrinthSearchUrl!);
            Assert.NotNull(report.SuspectedMods[0].CurseForgeSearchUrl);
            Assert.Contains("curseforge.com/minecraft/mc-mods/search?search=sodium", report.SuspectedMods[0].CurseForgeSearchUrl!);
            // Filename timestamp parsed.
            Assert.Equal(2024, report.GeneratedAt.Year);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task ParseAsync_FabricReport_DetectsFabricLoaderAndRanksLithiumFirst()
    {
        var fixture = FabricLithiumReport;
        var tmp = Path.Combine(Path.GetTempPath(), "crash-2024-09-20_18.30.15-client.txt");
        await File.WriteAllTextAsync(tmp, fixture);
        try
        {
            var parser = new MinecraftCrashReportParser();
            var report = await parser.ParseAsync(tmp, CancellationToken.None);

            Assert.Equal("1.21", report.MinecraftVersion);
            Assert.NotNull(report.ForgeOrFabricVersion);
            Assert.Contains("Fabric", report.ForgeOrFabricVersion!);
            // The summary line is the joke quote on the line under the banner.
            Assert.Contains("crashed", report.SummaryLine, System.StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(report.SuspectedMods);
            // Lithium is what threw on the stack; vanilla + java + fabric loader frames are ignored.
            Assert.Equal("lithium", report.SuspectedMods[0].ModId);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Parse_KeepsFullTextVerbatim_And_DefaultsToMtimeWhenFilenameUnparseable()
    {
        // Filename without the canonical timestamp pattern.
        var parser = new MinecraftCrashReportParser();
        var report = parser.Parse("/tmp/garbage.txt", "---- Minecraft Crash Report ----\n// hello\n\nMinecraft Version: 1.19.4");
        Assert.Equal("1.19.4", report.MinecraftVersion);
        Assert.Contains("---- Minecraft Crash Report ----", report.FullText);
        Assert.Equal("// hello", report.SummaryLine);
        // Filename did not parse; the parser falls back to UtcNow (mtime is unavailable for a non-existent path).
        Assert.True(report.GeneratedAt.Year >= 2024);
    }

    // ----- fixtures -----

    private const string ForgeSodiumReport = @"---- Minecraft Crash Report ----
// Why did you do that?

Time: 2024-08-12 14:05:33
Description: Rendering Block Entity

java.lang.NullPointerException: Cannot invoke ""net.minecraft.client.renderer.RenderType.getBufferSize()"" because ""$$1"" is null
	at sodium.client.render.SodiumWorldRenderer.renderBlock(SodiumWorldRenderer.java:172) ~[sodium-fabric-0.5.8.jar:?]
	at net.minecraft.client.renderer.LevelRenderer.renderLevel(LevelRenderer.java:1242) ~[forge-1.20.1.jar:?]
	at net.minecraft.client.Minecraft.runTick(Minecraft.java:1183) ~[forge-1.20.1.jar:?]
	at java.lang.Thread.run(Thread.java:833) ~[?:?]

A detailed walkthrough of the error, its code path and all known details is as follows:
---------------------------------------------------------------------------------------

-- System Details --
Details:
	Minecraft Version: 1.20.1
	Minecraft Version ID: 1.20.1
	Operating System: Windows 11 (amd64) version 10.0
	Java Version: 17.0.8, Microsoft
	Java VM Version: OpenJDK 64-Bit Server VM (mixed mode), Microsoft
	Memory: 1234567890 bytes (1177 MiB) / 4294967296 bytes (4096 MiB) up to 8589934592 bytes (8192 MiB)
	CPUs: 16
	Processor Vendor: AuthenticAMD
	Forge Version: 47.4.5
	Mod List:
		minecraft                    | Minecraft                      | minecraft-1.20.1                          | DONE     | Manifest: NA
		forge                        | Forge                          | forge-47.4.5                              | DONE     | Manifest: NA
		sodium                       | Sodium                         | sodium-fabric-0.5.8                       | DONE     | Manifest: NA
		jei                          | Just Enough Items              | jei-1.20.1-15.2.0.27                      | DONE     | Manifest: NA
";

    private const string FabricLithiumReport = @"---- Minecraft Crash Report ----
// The client crashed during ticking.

Time: 2024-09-20 18:30:15
Description: Ticking entity

java.lang.IllegalStateException: Entity ticking after worldgen finished
	at lithium.world.tick.LithiumChunkTicker.tickEntity(LithiumChunkTicker.java:88)
	at net.minecraft.server.level.ServerChunkCache.tick(ServerChunkCache.java:312)
	at net.minecraft.server.MinecraftServer.tickChildren(MinecraftServer.java:944)
	at net.fabricmc.loader.impl.launch.knot.Knot.main(Knot.java:54)

-- System Details --
Details:
	Minecraft Version: 1.21
	Minecraft Version ID: 1.21
	Operating System: Linux (amd64) version 6.6.0
	Java Version: 21.0.2, Eclipse Adoptium
	Fabric Loader: 0.16.5
	Fabric Mods:
		- fabricloader 0.16.5
		- fabric-api 0.100.4
		- lithium 0.13.0
		- sodium 0.6.0
		- minecraft 1.21
";
}
