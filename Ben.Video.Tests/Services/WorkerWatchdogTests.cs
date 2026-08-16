using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 143 — <see cref="WorkerWatchdog"/>'s own
/// timestamp-driven bookkeeping, entirely independent of any real timer or JS interop (that
/// wiring is <see cref="FfmpegService"/>'s job, covered separately).
/// </summary>
public sealed class WorkerWatchdogTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(45);

    [Fact]
    public void Evaluate_NothingInFlight_NeverWedges()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var now = DateTime.UtcNow;

        Assert.False(watchdog.Evaluate(now + Threshold + TimeSpan.FromMinutes(10)));
        Assert.False(watchdog.IsWedged);
    }

    [Fact]
    public void Evaluate_RecentActivity_NotWedged()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);

        Assert.False(watchdog.Evaluate(start + TimeSpan.FromSeconds(30))); // under the 45s threshold
        Assert.False(watchdog.IsWedged);
    }

    [Fact]
    public void Evaluate_NoActivityPastThreshold_Wedges()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);

        Assert.True(watchdog.Evaluate(start + Threshold + TimeSpan.FromSeconds(1)));
        Assert.True(watchdog.IsWedged);
    }

    [Fact]
    public void RecordActivity_KeepsResettingTheClock_SoALegitimatelySlowCommandNeverWedges()
    {
        // A healthy long-running command (e.g. a 90s export) emits log/progress lines constantly
        // — this is the case that must NOT be flagged, distinguishing "slow" from "silent."
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);

        for (var elapsed = 0; elapsed <= 120; elapsed += 5)
        {
            var now = start + TimeSpan.FromSeconds(elapsed);
            watchdog.RecordActivity(now); // simulates a log line every 5s
            Assert.False(watchdog.Evaluate(now));
        }
    }

    [Fact]
    public void OnWedged_FiresOnlyOnceForTheSameWedge()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);

        var fireCount = 0;
        watchdog.OnWedged += () => fireCount++;

        var wedgedAt = start + Threshold + TimeSpan.FromSeconds(1);
        watchdog.Evaluate(wedgedAt);
        watchdog.Evaluate(wedgedAt + TimeSpan.FromSeconds(5)); // still wedged, same episode
        watchdog.Evaluate(wedgedAt + TimeSpan.FromSeconds(10));

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void CommandFinished_ClearsWedgedState()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);
        watchdog.Evaluate(start + Threshold + TimeSpan.FromSeconds(1));
        Assert.True(watchdog.IsWedged);

        watchdog.CommandFinished();

        Assert.False(watchdog.IsWedged);
        Assert.False(watchdog.Evaluate()); // nothing in flight anymore
    }

    [Fact]
    public void CommandStarted_ResetsWedgedStateForTheNewCommand()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);
        watchdog.Evaluate(start + Threshold + TimeSpan.FromSeconds(1));
        Assert.True(watchdog.IsWedged);

        watchdog.CommandStarted(start + Threshold + TimeSpan.FromSeconds(2));

        Assert.False(watchdog.IsWedged);
    }

    [Fact]
    public void Reset_ClearsInFlightAndWedgedState()
    {
        var watchdog = new WorkerWatchdog(Threshold);
        var start = DateTime.UtcNow;
        watchdog.CommandStarted(start);
        watchdog.Evaluate(start + Threshold + TimeSpan.FromSeconds(1));

        watchdog.Reset();

        Assert.False(watchdog.IsWedged);
        Assert.False(watchdog.Evaluate(start + Threshold * 2)); // no longer "in flight"
    }
}
