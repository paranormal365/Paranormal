using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>Item #59-#65 flakiness investigation, phase 145 (symptom S3) — <see cref="ThumbnailPlanner"/>'s
/// pure arithmetic. <see cref="ThumbnailPlanner.FullCount"/> must reproduce the exact clamp every
/// import call site used before this phase (now centralized instead of duplicated three times).</summary>
public sealed class ThumbnailPlannerTests
{
    [Theory]
    [InlineData(0.5, 1)]   // very short clip — floors to the minimum, never zero
    [InlineData(1.0, 1)]
    [InlineData(2.0, 1)]
    [InlineData(4.0, 2)]
    [InlineData(10.0, 5)]
    [InlineData(16.0, 8)]  // exactly at the ceiling
    [InlineData(60.0, 8)]  // long clip — clamps at the max, doesn't keep growing
    [InlineData(7200.0, 8)] // 2 hours — still clamped
    public void FullCount_MatchesThePreExistingClampExactly(double durationSeconds, int expected)
    {
        Assert.Equal(expected, ThumbnailPlanner.FullCount(durationSeconds));
    }

    [Fact]
    public void UpfrontCount_NeverExceedsTheUpfrontBudget()
    {
        Assert.True(ThumbnailPlanner.UpfrontCount(7200.0) <= ThumbnailPlanner.UpfrontBudget);
    }

    [Fact]
    public void UpfrontCount_NeverExceedsFullCount()
    {
        // A 3s clip only wants 1 thumbnail total — upfront must not ask for more than that just
        // because the upfront budget itself is higher.
        var duration = 3.0;
        Assert.True(ThumbnailPlanner.UpfrontCount(duration) <= ThumbnailPlanner.FullCount(duration));
    }

    [Fact]
    public void UpfrontCount_ForALongClip_EqualsTheBudget()
    {
        Assert.Equal(ThumbnailPlanner.UpfrontBudget, ThumbnailPlanner.UpfrontCount(7200.0));
    }

    [Fact]
    public void UpfrontCount_ForAShortClip_EqualsFullCountNotTheBudget()
    {
        var duration = 2.0; // FullCount == 1, well under the upfront budget of 3
        Assert.Equal(ThumbnailPlanner.FullCount(duration), ThumbnailPlanner.UpfrontCount(duration));
    }
}
