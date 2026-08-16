using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class CanvasSnapCalculatorTests
{
    private static readonly IReadOnlyList<double> Guides = CanvasSnapCalculator.DefaultGuides;

    [Fact]
    public void FindSnap_CenterWithinThreshold_SnapsToCenterGuide()
    {
        // Item at x=0.48, width=0.04 → center = 0.50, exactly the canvas-center guide.
        var result = CanvasSnapCalculator.FindSnap(0.48, 0.04, Guides, thresholdFraction: 0.02);

        Assert.Equal(0.5, result.Guide);
        Assert.Equal(0.02, result.Offset, precision: 5); // size/2
    }

    [Fact]
    public void FindSnap_LeadingEdgeWithinThreshold_SnapsToEdgeGuide()
    {
        // Item at x=0.01, width=0.2 → leading edge is near the canvas's left edge guide (0.0).
        var result = CanvasSnapCalculator.FindSnap(0.01, 0.2, Guides, thresholdFraction: 0.02);

        Assert.Equal(0.0, result.Guide);
        Assert.Equal(0.0, result.Offset, precision: 5); // leading edge itself
    }

    [Fact]
    public void FindSnap_TrailingEdgeWithinThreshold_SnapsToEdgeGuide()
    {
        // Item at x=0.79, width=0.2 → trailing edge (0.99) is near the right edge guide (1.0).
        var result = CanvasSnapCalculator.FindSnap(0.79, 0.2, Guides, thresholdFraction: 0.02);

        Assert.Equal(1.0, result.Guide);
        Assert.Equal(0.2, result.Offset, precision: 5); // trailing edge = position + size
    }

    [Fact]
    public void FindSnap_NothingWithinThreshold_ReturnsNullGuide()
    {
        var result = CanvasSnapCalculator.FindSnap(0.2, 0.02, Guides, thresholdFraction: 0.01);

        Assert.Null(result.Guide);
        Assert.Equal(0.0, result.Offset);
    }

    [Fact]
    public void FindSnap_PointOnlyItem_SizeZero_ChecksAnchorAgainstAllGuides()
    {
        // A text overlay's anchor point (size=0) near the rule-of-thirds line at 1/3.
        var result = CanvasSnapCalculator.FindSnap(0.34, 0, Guides, thresholdFraction: 0.02);

        Assert.Equal(1.0 / 3.0, result.Guide!.Value, precision: 5);
        Assert.Equal(0.0, result.Offset);
    }

    [Fact]
    public void Snap_ReturnsAdjustedLeadingEdge_WhenCenterMatches()
    {
        // Item at x=0.48, width=0.04 → center should land exactly on 0.5, so the leading edge
        // (what callers actually apply back to X) becomes 0.5 - 0.02 = 0.48.
        var snapped = CanvasSnapCalculator.Snap(0.481, 0.04, Guides, thresholdFraction: 0.02);

        Assert.Equal(0.48, snapped, precision: 5);
    }

    [Fact]
    public void Snap_NoMatch_ReturnsPositionUnchanged()
    {
        var snapped = CanvasSnapCalculator.Snap(0.2, 0.02, Guides, thresholdFraction: 0.01);

        Assert.Equal(0.2, snapped);
    }

    [Fact]
    public void ActiveGuide_MatchesFindSnapGuide()
    {
        var active = CanvasSnapCalculator.ActiveGuide(0.48, 0.04, Guides, thresholdFraction: 0.02);

        Assert.Equal(0.5, active);
    }

    [Fact]
    public void FindSnap_EmptyGuides_ReturnsNull()
    {
        var result = CanvasSnapCalculator.FindSnap(0.5, 0.1, [], thresholdFraction: 0.02);

        Assert.Null(result.Guide);
    }

    [Fact]
    public void FindSnap_ZeroThreshold_ReturnsNull()
    {
        var result = CanvasSnapCalculator.FindSnap(0.5, 0.0, Guides, thresholdFraction: 0.0);

        Assert.Null(result.Guide);
    }

    [Fact]
    public void FindSnap_PicksClosestAmongMultipleCandidates()
    {
        // Item at x=0.0, width=1.0 spans edge-to-edge: leading edge sits exactly on the left
        // guide (0.0), center on 0.5, trailing edge on 1.0 — all three are exact matches, so the
        // first-found in scan order (offset 0, the leading edge) should win a tie.
        var result = CanvasSnapCalculator.FindSnap(0.0, 1.0, Guides, thresholdFraction: 0.02);

        Assert.Equal(0.0, result.Guide);
        Assert.Equal(0.0, result.Offset);
    }
}
