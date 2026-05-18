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
/// Each value is a millisecond count attached to its label; TOTAL is the sum of every label's
/// value. Output goes through the project's <see cref="ILauncherLogger"/> (so it reaches the
/// Serilog file sink and the in-app log pane).
/// </summary>
/// <remarks>
/// <para>
/// Two ways to feed the timeline:
/// </para>
/// <list type="bullet">
///   <item><see cref="Mark(string)"/> - measures wall-clock from the previous Mark (or
///   <see cref="Begin"/>) to now, stores that delta. Use for sequential steps.</item>
///   <item><see cref="Record(string, long)"/> - takes an explicit duration. Use for steps
///   that ran inside <c>Task.WhenAll</c>, where the per-step wall-clock was already measured
///   with a local <see cref="Stopwatch"/>.</item>
/// </list>
/// <para>
/// Thread-safety: writes/reads are guarded by an internal lock so a parallelised startup pipeline
/// (multiple <c>Record</c> calls racing from <c>Task.WhenAll</c>) doesn't corrupt the list.
/// The recorded ordering is then "the order checkpoints arrived", which is what we want for a
/// debugging line - it shows actual completion order.
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
    private static readonly List<(string Label, long DeltaMs)> _entries = new(capacity: 16);
    private static long _lastMarkTicks;
    private static bool _active;

    /// <summary>Stopwatch ticks per millisecond on this platform.</summary>
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

    /// <summary>Start (or restart) the timeline.</summary>
    public static void Begin()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lastMarkTicks = Stopwatch.GetTimestamp();
            _active = true;
        }
    }

    /// <summary>
    /// Record a sequential checkpoint with <paramref name="label"/>. The recorded value is the
    /// wall-clock delta in ms from the previous <see cref="Mark"/> (or <see cref="Begin"/>).
    /// </summary>
    public static void Mark(string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        lock (_gate)
        {
            if (!_active) return;
            var now = Stopwatch.GetTimestamp();
            var deltaMs = (long)Math.Round((now - _lastMarkTicks) / TicksPerMs);
            if (deltaMs < 0) deltaMs = 0;
            _entries.Add((label, deltaMs));
            _lastMarkTicks = now;
        }
    }

    /// <summary>
    /// Record a checkpoint with an explicit duration. Used for parallel steps where the
    /// per-step duration was timed locally (the global "previous Mark" pointer doesn't
    /// move so subsequent Mark calls still measure from Begin or the prior sequential Mark).
    /// </summary>
    public static void Record(string label, long durationMs)
    {
        if (string.IsNullOrEmpty(label)) return;
        if (durationMs < 0) durationMs = 0;
        lock (_gate)
        {
            if (!_active) return;
            _entries.Add((label, durationMs));
        }
    }

    /// <summary>
    /// Build and return the summary line. Pure - no side effects. Useful for tests that want
    /// to assert format without touching a logger. TOTAL is the sum of every entry's ms count.
    /// </summary>
    public static string Report()
    {
        lock (_gate)
        {
            var sb = new StringBuilder(96);
            sb.Append("[startup]");

            long total = 0;
            foreach (var (label, deltaMs) in _entries)
            {
                sb.Append(' ');
                sb.Append(label);
                sb.Append('=');
                sb.Append(deltaMs.ToString(CultureInfo.InvariantCulture));
                sb.Append("ms");
                total += deltaMs;
            }

            sb.Append(' ');
            sb.Append("TOTAL=");
            sb.Append(total.ToString(CultureInfo.InvariantCulture));
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
            _lastMarkTicks = 0;
            _active = false;
        }
    }
}
