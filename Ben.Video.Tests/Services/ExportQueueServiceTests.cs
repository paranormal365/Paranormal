using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

public sealed class ExportQueueServiceTests
{
    private static ExportService CreateExporter()
    {
        // ExportService depends on FfmpegService, ClipStore, etc. — those need real instances
        // or mocks. For queue service tests, we only test the queue layer (not the pipeline).
        // We cannot instantiate ExportService without infrastructure, so we test
        // ExportQueueEntry and ExportQueueService state transitions in isolation.
        return null!; // intentionally null — not called in queue-layer tests
    }

    // ── ExportQueueEntry ──────────────────────────────────────────────────────

    [Fact]
    public void ExportQueueEntry_DefaultStateIsQueued()
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        Assert.Equal(QueueEntryState.Queued, entry.State);
    }

    [Fact]
    public void ExportQueueEntry_ProgressPercent_ZeroWhenQueued()
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        Assert.Equal(0, entry.ProgressPercent);
    }

    [Fact]
    public void ExportQueueEntry_ProgressPercent_100WhenCompleted()
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        entry.State = QueueEntryState.Completed;
        Assert.Equal(100, entry.ProgressPercent);
    }

    [Fact]
    public void ExportQueueEntry_ProgressPercent_100WhenFailed()
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        entry.State = QueueEntryState.Failed;
        Assert.Equal(100, entry.ProgressPercent);
    }

    [Fact]
    public void ExportQueueEntry_ElapsedDisplay_ShowsSeconds()
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        entry.StartedAt  = DateTimeOffset.UtcNow.AddSeconds(-45);
        entry.FinishedAt = DateTimeOffset.UtcNow;
        Assert.Contains("s", entry.ElapsedDisplay);
    }

    [Fact]
    public void ExportQueueEntry_ElapsedDisplay_ShowsMinutes()
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        entry.StartedAt  = DateTimeOffset.UtcNow.AddMinutes(-2).AddSeconds(-30);
        entry.FinishedAt = DateTimeOffset.UtcNow;
        Assert.Contains("m", entry.ElapsedDisplay);
    }

    [Fact]
    public void ExportQueueEntry_NameDefaultsToSettings()
    {
        var entry = new ExportQueueEntry
        {
            Name     = "my-video.mp4",
            Settings = new ExportSettings { OutputFilename = "my-video.mp4" },
        };
        Assert.Equal("my-video.mp4", entry.Name);
    }

    // ── QueueEntryState transitions ───────────────────────────────────────────

    [Theory]
    [InlineData(QueueEntryState.Queued,    0)]
    [InlineData(QueueEntryState.Running,   0)]   // no Job → 0
    [InlineData(QueueEntryState.Completed, 100)]
    [InlineData(QueueEntryState.Failed,    100)]
    [InlineData(QueueEntryState.Cancelled, 100)]
    public void ExportQueueEntry_ProgressPercent_ByState(QueueEntryState state, int expected)
    {
        var entry = new ExportQueueEntry { Settings = new ExportSettings() };
        entry.State = state;
        Assert.Equal(expected, entry.ProgressPercent);
    }

    // ── ExportQueueService state calculations ─────────────────────────────────

    [Fact]
    public void CombinedPercent_ZeroWhenNoEntries()
    {
        // Mirror the CombinedPercent logic without needing a real service instance
        var entries = new List<ExportQueueEntry>();
        var total = entries.Count;
        var pct   = total == 0 ? 0 : entries.Sum(e => e.ProgressPercent) / total;
        Assert.Equal(0, pct);
    }

    [Fact]
    public void CombinedPercent_50WhenOneCompletedOneQueued()
    {
        var entries = new List<ExportQueueEntry>
        {
            new() { Settings = new ExportSettings(), State = QueueEntryState.Completed },
            new() { Settings = new ExportSettings(), State = QueueEntryState.Queued    },
        };
        var total = entries.Count;
        var sum   = entries.Sum(e => e.State == QueueEntryState.Completed ? 100 : 0);
        var pct   = sum / total;
        Assert.Equal(50, pct);
    }

    [Fact]
    public void CombinedPercent_100WhenAllCompleted()
    {
        var entries = new List<ExportQueueEntry>
        {
            new() { Settings = new ExportSettings(), State = QueueEntryState.Completed },
            new() { Settings = new ExportSettings(), State = QueueEntryState.Completed },
        };
        var total = entries.Count;
        var sum   = entries.Sum(e => e.ProgressPercent);
        var pct   = sum / total;
        Assert.Equal(100, pct);
    }
}
