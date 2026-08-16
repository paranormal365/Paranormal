using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class CanvasHitTesterTests
{
    [Fact]
    public void HitTest_EmptyList_ReturnsNull()
    {
        Assert.Null(CanvasHitTester.HitTest([], 0.5, 0.5));
    }

    [Fact]
    public void HitTest_PointInsideSingleRect_ReturnsIndexZero()
    {
        var rects = new[] { new CanvasHitRect(0.2, 0.2, 0.3, 0.3) };
        Assert.Equal(0, CanvasHitTester.HitTest(rects, 0.3, 0.3));
    }

    [Fact]
    public void HitTest_PointOutsideRect_ReturnsNull()
    {
        var rects = new[] { new CanvasHitRect(0.2, 0.2, 0.3, 0.3) };
        Assert.Null(CanvasHitTester.HitTest(rects, 0.9, 0.9));
    }

    [Theory]
    [InlineData(0.2, 0.2)] // top-left corner, inclusive
    [InlineData(0.5, 0.5)] // bottom-right corner, inclusive
    [InlineData(0.2, 0.5)]
    [InlineData(0.5, 0.2)]
    public void HitTest_PointOnRectBoundary_IsInclusive(double px, double py)
    {
        var rects = new[] { new CanvasHitRect(0.2, 0.2, 0.3, 0.3) };
        Assert.Equal(0, CanvasHitTester.HitTest(rects, px, py));
    }

    [Fact]
    public void HitTest_OverlappingRects_ReturnsFirstMatch_TopmostWins()
    {
        // Both rects contain (0.5, 0.5); the list must already be ordered topmost-first by the
        // caller (e.g. descending LayerIndex) — HitTest itself just returns the first match.
        var rects = new[]
        {
            new CanvasHitRect(0.4, 0.4, 0.3, 0.3), // topmost
            new CanvasHitRect(0.0, 0.0, 1.0, 1.0), // background, also contains the point
        };
        Assert.Equal(0, CanvasHitTester.HitTest(rects, 0.5, 0.5));
    }

    [Fact]
    public void HitTest_MissesFirstRect_FallsThroughToSecond()
    {
        var rects = new[]
        {
            new CanvasHitRect(0.0, 0.0, 0.1, 0.1),
            new CanvasHitRect(0.4, 0.4, 0.3, 0.3),
        };
        Assert.Equal(1, CanvasHitTester.HitTest(rects, 0.5, 0.5));
    }

    [Fact]
    public void HitTest_ZeroSizeRect_OnlyMatchesItsExactPoint()
    {
        var rects = new[] { new CanvasHitRect(0.5, 0.5, 0.0, 0.0) };
        Assert.Equal(0, CanvasHitTester.HitTest(rects, 0.5, 0.5));
        Assert.Null(CanvasHitTester.HitTest(rects, 0.501, 0.5));
    }
}
