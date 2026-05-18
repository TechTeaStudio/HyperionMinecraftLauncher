using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Diagnostics;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Diagnostics;

/// <summary>
/// Verifies <see cref="StartupTimeline"/> emits a deterministic-format line:
/// <c>[startup] label1=NNms label2=NNms ... TOTAL=NNms</c>. The actual deltas vary
/// from run to run, so the tests assert structure (labels present in order, every
/// number parseable as a non-negative int).
/// </summary>
public class StartupTimelineTests : IDisposable
{
    public StartupTimelineTests()
    {
        // The helper is static; isolate every test by resetting before AND after.
        StartupTimeline.ResetForTests();
    }

    public void Dispose() => StartupTimeline.ResetForTests();

    [Fact]
    public void Report_WithNoBegin_EmitsEmptyTotalLine()
    {
        var line = StartupTimeline.Report();
        Assert.Equal("[startup] TOTAL=0ms", line);
    }

    [Fact]
    public void Report_AfterBeginNoMarks_EmitsTotalOnly()
    {
        StartupTimeline.Begin();
        var line = StartupTimeline.Report();
        Assert.StartsWith("[startup] TOTAL=", line);
        Assert.EndsWith("ms", line);
    }

    [Fact]
    public void Report_WithCheckpoints_HasLabelsInOrderAndParseableMs()
    {
        StartupTimeline.Begin();
        StartupTimeline.Mark("dispatcher");
        Thread.Sleep(2);
        StartupTimeline.Mark("versions");
        Thread.Sleep(1);
        StartupTimeline.Mark("instances");

        var line = StartupTimeline.Report();

        // Format: "[startup] dispatcher=NNms versions=NNms instances=NNms TOTAL=NNms"
        Assert.StartsWith("[startup] ", line);
        Assert.Contains("dispatcher=", line);
        Assert.Contains("versions=", line);
        Assert.Contains("instances=", line);
        Assert.Contains("TOTAL=", line);

        // Labels must appear in the order they were Mark()'d.
        var idxDispatcher = line.IndexOf("dispatcher=", StringComparison.Ordinal);
        var idxVersions = line.IndexOf("versions=", StringComparison.Ordinal);
        var idxInstances = line.IndexOf("instances=", StringComparison.Ordinal);
        var idxTotal = line.IndexOf("TOTAL=", StringComparison.Ordinal);
        Assert.True(idxDispatcher < idxVersions);
        Assert.True(idxVersions < idxInstances);
        Assert.True(idxInstances < idxTotal);

        // Every "<label>=NN<ms|MS>" segment must parse as a non-negative integer ms count.
        var rx = new Regex(@"(\w[\w-]*)=(\d+)ms", RegexOptions.CultureInvariant);
        var matches = rx.Matches(line);
        Assert.True(matches.Count >= 4); // dispatcher, versions, instances, TOTAL
        foreach (Match m in matches)
        {
            var ok = int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
            Assert.True(ok, $"Could not parse ms in segment '{m.Value}'.");
            Assert.True(value >= 0, $"Non-negative expected, got {value} in '{m.Value}'.");
        }
    }

    [Fact]
    public void Report_TotalIsSumOfPositiveDeltas_WhenChecksRun()
    {
        StartupTimeline.Begin();
        StartupTimeline.Mark("a");
        StartupTimeline.Mark("b");
        StartupTimeline.Mark("c");

        var line = StartupTimeline.Report();

        // Pull every <label>=NNms segment out, sum the non-TOTAL ones, compare to TOTAL.
        var rx = new Regex(@"(\w[\w-]*)=(\d+)ms", RegexOptions.CultureInvariant);
        int sum = 0;
        int total = -1;
        foreach (Match m in rx.Matches(line))
        {
            var value = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[1].Value == "TOTAL") total = value;
            else sum += value;
        }
        Assert.True(total >= 0);
        Assert.Equal(sum, total);
    }

    [Fact]
    public void ReportTo_EmitsExactlyOneInfoLine_ToLogger()
    {
        var logger = new RecordingLogger();
        StartupTimeline.Begin();
        StartupTimeline.Mark("dispatcher");
        StartupTimeline.Mark("versions");
        StartupTimeline.ReportTo(logger);

        Assert.Single(logger.InfoEntries);
        var line = logger.InfoEntries[0];
        Assert.StartsWith("[startup] ", line);
        Assert.Contains("dispatcher=", line);
        Assert.Contains("versions=", line);
        Assert.Contains("TOTAL=", line);
    }

    [Fact]
    public void MatchingExampleShape_FromTaskBrief()
    {
        // Sanity: the brief shows
        // "[startup] dispatcher=15ms versions=420ms instances=12ms profiles=8ms servers=6ms news=110ms ms-auth=0ms TOTAL=571ms"
        // - the format must allow every label there including a hyphenated "ms-auth".
        StartupTimeline.Begin();
        StartupTimeline.Mark("dispatcher");
        StartupTimeline.Mark("versions");
        StartupTimeline.Mark("instances");
        StartupTimeline.Mark("profiles");
        StartupTimeline.Mark("servers");
        StartupTimeline.Mark("news");
        StartupTimeline.Mark("ms-auth");

        var line = StartupTimeline.Report();
        foreach (var label in new[] { "dispatcher", "versions", "instances", "profiles", "servers", "news", "ms-auth" })
            Assert.Contains(label + "=", line);
        Assert.Contains("TOTAL=", line);
    }

    private sealed class RecordingLogger : ILauncherLogger
    {
        public System.Collections.Generic.List<string> InfoEntries { get; } = new();
        public System.Collections.Generic.List<string> WarnEntries { get; } = new();
        public System.Collections.Generic.List<string> ErrorEntries { get; } = new();

        public void Info(string message) => InfoEntries.Add(message);
        public void Warn(string message) => WarnEntries.Add(message);
        public void Error(string message, Exception? exception = null) => ErrorEntries.Add(message);
    }
}
