using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 144 — <see cref="BlobUrlLifecycle"/>'s own
/// bookkeeping. It doesn't revoke anything itself; it only tracks who currently owns each URL
/// and logs a diagnostic (never throws) when a revoke or transfer doesn't match what it expects —
/// exactly the two live-found bug shapes (double-revoke, revoke-while-attached) this phase fixed
/// directly in VideoEditor/VideoPreview.
/// </summary>
public sealed class BlobUrlLifecycleTests
{
    [Fact]
    public void Created_ThenRevoking_WithMatchingOwner_LogsNothing()
    {
        var errorLog = new ErrorLogService();
        var registry = new BlobUrlLifecycle(errorLog);

        registry.Created("blob:1", "OwnerA");
        registry.Revoking("blob:1", "OwnerA");

        Assert.False(errorLog.HasEntries);
        Assert.False(registry.IsLive("blob:1"));
    }

    [Fact]
    public void Revoking_UntrackedUrl_LogsADoubleRevokeDiagnosticButDoesNotThrow()
    {
        var errorLog = new ErrorLogService();
        var registry = new BlobUrlLifecycle(errorLog);

        registry.Revoking("blob:never-created", "OwnerA");

        Assert.True(errorLog.HasEntries);
        Assert.Contains(errorLog.Entries, e => e.Source == "BlobUrlLifecycle");
    }

    [Fact]
    public void Revoking_SameUrlTwice_SecondCallLogsDoubleRevoke()
    {
        var errorLog = new ErrorLogService();
        var registry = new BlobUrlLifecycle(errorLog);
        registry.Created("blob:1", "OwnerA");

        registry.Revoking("blob:1", "OwnerA"); // legitimate
        Assert.False(errorLog.HasEntries);

        registry.Revoking("blob:1", "OwnerA"); // double-revoke
        Assert.True(errorLog.HasEntries);
    }

    [Fact]
    public void Revoking_ByAnOwnerThatIsNotTheCurrentOwner_LogsRevokeWhileAttached()
    {
        // The exact shape of the OnClipSelectedAsync bug this phase fixed: ownership moved to a
        // different owner (via Transfer), and the ORIGINAL owner then tries to revoke the same
        // string, unaware it's no longer theirs.
        var errorLog = new ErrorLogService();
        var registry = new BlobUrlLifecycle(errorLog);
        registry.Created("blob:1", "VideoEditor.timelinePreview");
        registry.Transfer("blob:1", "WorkingWindow");

        registry.Revoking("blob:1", "VideoEditor.timelinePreview"); // stale owner, doesn't match

        Assert.True(errorLog.HasEntries);
        Assert.Contains(errorLog.Entries, e => e.Message.Contains("revoke-while-attached"));
    }

    [Fact]
    public void Transfer_ToANewOwner_UpdatesWhoOwnsIt()
    {
        var errorLog = new ErrorLogService();
        var registry = new BlobUrlLifecycle(errorLog);
        registry.Created("blob:1", "OwnerA");

        registry.Transfer("blob:1", "OwnerB");
        Assert.False(errorLog.HasEntries); // legitimate transfer of a tracked url — no diagnostic

        // Now OwnerB is the correct one to revoke it.
        registry.Revoking("blob:1", "OwnerB");
        Assert.False(errorLog.HasEntries);
    }

    [Fact]
    public void Transfer_OfAnUntrackedUrl_LogsButStillRegistersIt()
    {
        var errorLog = new ErrorLogService();
        var registry = new BlobUrlLifecycle(errorLog);

        registry.Transfer("blob:never-created", "OwnerA");

        Assert.True(errorLog.HasEntries);
        Assert.True(registry.IsLive("blob:never-created")); // still ends up tracked
    }

    [Fact]
    public void IsLive_ReflectsCurrentTrackingState()
    {
        var registry = new BlobUrlLifecycle(new ErrorLogService());
        Assert.False(registry.IsLive("blob:1"));

        registry.Created("blob:1", "OwnerA");
        Assert.True(registry.IsLive("blob:1"));

        registry.Revoking("blob:1", "OwnerA");
        Assert.False(registry.IsLive("blob:1"));
    }

    [Fact]
    public void TrackedCount_ReflectsNumberOfLiveUrls()
    {
        var registry = new BlobUrlLifecycle(new ErrorLogService());
        registry.Created("blob:1", "OwnerA");
        registry.Created("blob:2", "OwnerA");
        Assert.Equal(2, registry.TrackedCount);

        registry.Revoking("blob:1", "OwnerA");
        Assert.Equal(1, registry.TrackedCount);
    }

    [Fact]
    public void Created_SameUrlTwice_OverwritesRatherThanDuplicating()
    {
        var registry = new BlobUrlLifecycle(new ErrorLogService());
        registry.Created("blob:1", "OwnerA");
        registry.Created("blob:1", "OwnerB");

        Assert.Equal(1, registry.TrackedCount);
        registry.Revoking("blob:1", "OwnerB"); // the most recent Created wins
        Assert.False(registry.IsLive("blob:1"));
    }
}
