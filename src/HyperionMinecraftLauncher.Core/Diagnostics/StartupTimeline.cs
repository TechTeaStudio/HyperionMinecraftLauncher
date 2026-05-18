using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Diagnostics;

/// <summary>
/// Tiny static helper that records labelled checkpoints during launcher startup and emits
/// one summary line at the end:
/// <c>[startup] dispatcher=15ms versions=420ms instances=12ms profiles=8ms servers=6ms news=110ms ms-auth=0ms TOTAL=571ms</c>.
/// Each value is the wall-clock delta (ms, rounded) from the previous checkpoint; TOTAL is from
/// <see cref="Begin"/> to the last <see cref="Mark"/>. Output goes through the project's
/// <see cref="ILauncherLogger"/> (so it reaches the Serilog file sink and the in-app log pane).
/// </summary>
/// <remarks>
/// <para>
/// Thread-safety: writes/reads are guarded by an internal lock so a parallelised startup pipeline
/// (multiple <c>Mark</c> calls racing from <c>Task.WhenAll</c>) doesn't corrupt the list. The
/// recorded ordering is then "the order checkpoints arrived", which is what we want for a
/// debugging line - it shows the actual scheduling, not a synthetic one.
/// </para>
/// <para>
/// Single-instance: the launcher only runs one startup at a time, so a single static instance
/// is fine. <see cref="Begin"/> resets state, so re-entering startup (theoretical) does the
/// right thing.
/// </para>
/// </remarks>
public static class StartupTimeline
{
    private static readonly object _gate = new();
    private static readonly List<(string Label, long Ticks)> _entries = new(capacity: 16);
    private static long _startTicks;
    private static bool _active;

    /// <summary>Stopwatch ticks per millisecond on this platform.</summary>
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

    /// <summary>Start (or restart) the timeline. Subsequent <see cref="Mark"/> deltas are measured against this point.</summary>
    public static void Begin()
    {
        lock (_gate)
        {
            _entries.Clear();
            _startTicks = Stopwatch.GetTimestamp();
            _active = true;
        }
    }

    /// <summary>
    /// Record a checkpoint with <paramref name="label"/>. Caller is expected to invoke after the
    /// step it wants to time has completed; the recorded value is the timestamp at that instant.
    /// </summary>
    public static void Mark(string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        lock (_gate)
        {
            if (!_active) return;
            _entries.Add((label, Stopwatch.GetTimestamp()));
        }
    }

    /// <summary>
    /// Build and return the summary line. Pure - no side effects. Useful for tests that want
    /// to assert format without touching a logger.
    /// </summary>
    public static string Report()
    {
        lock (_gate)
        {
            var sb = new StringBuilder(96);
            sb.Append("[startup]");

            long previous = _startTicks;
            long last = _startTicks;
            foreach (var (label, ticks) in _entries)
            {
                var deltaMs = (long)Math.Round((ticks - previous) / TicksPerMs);
                if (deltaMs < 0) deltaMs = 0;
                sb.Append(' ');
                sb.Append(label);
                sb.Append('=');
                sb.Append(deltaMs.ToString(CultureInfo.InvariantCulture));
                sb.Append("ms");
                previous = ticks;
                last = ticks;
            }

            var totalMs = (long)Math.Round((last - _startTicks) / TicksPerMs);
            if (totalMs < 0) totalMs = 0;
            sb.Append(' ');
            sb.Append("TOTAL=");
            sb.Append(totalMs.ToString(CultureInfo.InvariantCulture));
            sb.Append("ms");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Emit the summary line through <paramref name="logger"/> and stop the timeline.
    /// Safe to call without a prior <see cref="Begin"/> (emits an empty <c>[startup] TOTAL=0ms</c> line).
    /// </summary>
    public static void ReportTo(ILauncherLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var line = Report();
        logger.Info(line);
        lock (_gate)
        {
            _active = false;
        }
    }

    /// <summary>
    /// Test-only hook: wipe state so each test starts from a clean slate. Public for
    /// cross-assembly use (the test project is not <c>InternalsVisibleTo</c>'d to Core).
    /// Not part of the runtime contract - production code never calls this.
    /// </summary>
    public static void ResetForTests()
    {
        lock (_gate)
        {
            _entries.Clear();
            _startTicks = 0;
            _active = false;
        }
    }
}
