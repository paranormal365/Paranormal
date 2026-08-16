using Ben.Video.RenderService;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #36 §8 / item #38 phase C: <see cref="SegmentBudget"/>'s pure arithmetic and eviction
/// ordering, in isolation from <see cref="BackgroundRenderService"/>'s loop/hold/tracker
/// integration (covered separately in <c>BackgroundRenderServiceTests</c>).
/// </summary>
public sealed class SegmentBudgetTests
{
    [Fact]
    public void Track_AddsToTotalBytes()
    {
        var budget = new SegmentBudget();
        budget.Track("a.mp4", Guid.NewGuid(), 100, isRough: false);
        budget.Track("b.mp4", Guid.NewGuid(), 50, isRough: false);

        Assert.Equal(150, budget.TotalBytes);
    }

    [Fact]
    public void Track_SameNameTwice_ReplacesSizeRatherThanAdding()
    {
        var budget = new SegmentBudget();
        var clipId = Guid.NewGuid();
        budget.Track("a.mp4", clipId, 100, isRough: false);
        budget.Track("a.mp4", clipId, 40, isRough: false);

        Assert.Equal(40, budget.TotalBytes);
    }

    [Fact]
    public void Untrack_RemovesFromTotalBytes()
    {
        var budget = new SegmentBudget();
        budget.Track("a.mp4", Guid.NewGuid(), 100, isRough: false);

        budget.Untrack("a.mp4");

        Assert.Equal(0, budget.TotalBytes);
    }

    [Fact]
    public void Untrack_UnknownName_IsHarmless()
    {
        var budget = new SegmentBudget();
        budget.Untrack("never-tracked.mp4"); // must not throw
        Assert.Equal(0, budget.TotalBytes);
    }

    [Fact]
    public void PickEvictions_UnderCap_ReturnsEmpty()
    {
        var budget = new SegmentBudget();
        budget.Track("a.mp4", Guid.NewGuid(), 100, isRough: false);

        var evictions = budget.PickEvictions(capBytes: 1000, protectedClipIds: []);

        Assert.Empty(evictions);
    }

    [Fact]
    public void PickEvictions_OverCap_EvictsOldestTouchedFirst()
    {
        var budget = new SegmentBudget();
        var oldClip = Guid.NewGuid();
        var newClip = Guid.NewGuid();
        budget.Track("old.mp4", oldClip, 60, isRough: false);
        Thread.Sleep(5);
        budget.Track("new.mp4", newClip, 60, isRough: false);

        var evictions = budget.PickEvictions(capBytes: 60, protectedClipIds: []);

        Assert.Equal(("old.mp4", oldClip), Assert.Single(evictions));
    }

    [Fact]
    public void PickEvictions_TouchRefreshesRecency_SoTouchedEntrySurvives()
    {
        var budget = new SegmentBudget();
        var touched   = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        budget.Track("touched.mp4", touched, 60, isRough: false);
        Thread.Sleep(5);
        budget.Track("untouched.mp4", untouched, 60, isRough: false);
        Thread.Sleep(5);
        budget.Touch("touched.mp4"); // now the most recently touched of the two

        var evictions = budget.PickEvictions(capBytes: 60, protectedClipIds: []);

        Assert.Equal(("untouched.mp4", untouched), Assert.Single(evictions));
    }

    [Fact]
    public void PickEvictions_RoughSegmentsEvictedBeforeFineRegardlessOfAge()
    {
        // A rough segment is cheaper to regenerate than a fine one — prefer evicting it first even
        // when it was touched more recently than a fine segment elsewhere.
        var budget = new SegmentBudget();
        var fineClip  = Guid.NewGuid();
        var roughClip = Guid.NewGuid();
        budget.Track("fine.mp4", fineClip, 60, isRough: false);
        Thread.Sleep(5);
        budget.Track("rough.mp4", roughClip, 60, isRough: true); // newer, but rough

        var evictions = budget.PickEvictions(capBytes: 60, protectedClipIds: []);

        Assert.Equal(("rough.mp4", roughClip), Assert.Single(evictions));
    }

    [Fact]
    public void PickEvictions_ProtectedClipId_IsNeverReturned()
    {
        var budget = new SegmentBudget();
        var protectedClip = Guid.NewGuid();
        var evictableClip  = Guid.NewGuid();
        budget.Track("protected.mp4", protectedClip, 60, isRough: false);
        Thread.Sleep(5);
        budget.Track("evictable.mp4", evictableClip, 60, isRough: false);

        // Cap forces evicting both by size alone, but the protected clip must never appear.
        var evictions = budget.PickEvictions(capBytes: 0, protectedClipIds: [protectedClip]);

        Assert.Equal(("evictable.mp4", evictableClip), Assert.Single(evictions));
    }

    [Fact]
    public void PickEvictions_EvictsOnlyEnoughToReachCap()
    {
        var budget = new SegmentBudget();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        budget.Track("a.mp4", a, 50, isRough: false);
        Thread.Sleep(5);
        budget.Track("b.mp4", b, 50, isRough: false);
        Thread.Sleep(5);
        budget.Track("c.mp4", c, 50, isRough: false);

        // Total 150, cap 100 — evicting just "a" (oldest, 50 bytes) brings it to 100.
        var evictions = budget.PickEvictions(capBytes: 100, protectedClipIds: []);

        Assert.Equal(("a.mp4", a), Assert.Single(evictions));
    }

    [Fact]
    public void PickEvictions_DoesNotMutateState()
    {
        var budget = new SegmentBudget();
        budget.Track("a.mp4", Guid.NewGuid(), 100, isRough: false);

        budget.PickEvictions(capBytes: 0, protectedClipIds: []);

        // Calling it again without an intervening Untrack must return the same candidate again —
        // PickEvictions is read-only, the caller is responsible for Untrack.
        Assert.Single(budget.PickEvictions(capBytes: 0, protectedClipIds: []));
        Assert.Equal(100, budget.TotalBytes);
    }
}
