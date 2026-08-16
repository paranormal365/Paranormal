using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class CalloutResizeMathTests
{
    // Reference box: X=0.2, Y=0.3, Width=0.4, Height=0.2 → right=0.6, bottom=0.5

    [Fact]
    public void ApplyResize_BottomRight_GrowsOnlyWidthAndHeight()
    {
        var (x, y, w, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, "br", 0.1, 0.05);

        Assert.Equal(0.2,  x, precision: 9); // left unchanged
        Assert.Equal(0.3,  y, precision: 9); // top unchanged
        Assert.Equal(0.5,  w, precision: 9); // 0.4 + 0.1
        Assert.Equal(0.25, h, precision: 9); // 0.2 + 0.05
    }

    [Fact]
    public void ApplyResize_TopLeft_MovesOriginAndShrinksInversely()
    {
        var (x, y, w, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, "tl", 0.1, 0.05);

        Assert.Equal(0.3,  x, precision: 9); // left moved right by 0.1
        Assert.Equal(0.35, y, precision: 9); // top moved down by 0.05
        Assert.Equal(0.3,  w, precision: 9); // width shrank by 0.1 (right edge fixed at 0.6)
        Assert.Equal(0.15, h, precision: 9); // height shrank by 0.05 (bottom edge fixed at 0.5)
    }

    [Fact]
    public void ApplyResize_TopRight_MixesAxes()
    {
        var (x, y, w, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, "tr", 0.1, -0.05);

        Assert.Equal(0.2,  x, precision: 9); // left unchanged
        Assert.Equal(0.25, y, precision: 9); // top moved up by 0.05 (bottom fixed at 0.5)
        Assert.Equal(0.5,  w, precision: 9); // right edge moved out by 0.1
        Assert.Equal(0.25, h, precision: 9); // height grew by 0.05
    }

    [Fact]
    public void ApplyResize_BottomLeft_MixesAxes()
    {
        var (x, y, w, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, "bl", -0.1, 0.05);

        Assert.Equal(0.1,  x, precision: 9); // left moved out by 0.1 (right fixed at 0.6)
        Assert.Equal(0.3,  y, precision: 9); // top unchanged
        Assert.Equal(0.5,  w, precision: 9); // width grew by 0.1
        Assert.Equal(0.25, h, precision: 9); // bottom moved down by 0.05
    }

    [Theory]
    [InlineData("t")]
    [InlineData("b")]
    public void ApplyResize_TopOrBottomEdge_OnlyChangesHeight(string handle)
    {
        var (x, y, w, _) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, handle, 999, 0.02);

        Assert.Equal(0.2, x, precision: 9); // horizontal edges never move X
        Assert.Equal(0.4, w, precision: 9); // ...or Width
        if (handle == "t") Assert.Equal(0.32, y, precision: 9); // top nudged down by deltaY=0.02
        else                Assert.Equal(0.3,  y, precision: 9); // top unchanged when dragging bottom
    }

    [Theory]
    [InlineData("l")]
    [InlineData("r")]
    public void ApplyResize_LeftOrRightEdge_OnlyChangesWidth(string handle)
    {
        var (x, y, _, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, handle, 0.02, 999);

        Assert.Equal(0.3, y, precision: 9); // vertical edges never move Y
        Assert.Equal(0.2, h, precision: 9); // ...or Height
        if (handle == "l") Assert.Equal(0.22, x, precision: 9); // left nudged right
        else                Assert.Equal(0.2,  x, precision: 9); // left unchanged when dragging right
    }

    [Fact]
    public void ApplyResize_CollapsingDrag_ClampsToMinSize()
    {
        // Drag br far into negative territory — box would collapse past zero without the clamp
        var (x, y, w, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, "br", -1.0, -1.0, minSize: 0.02);

        Assert.Equal(0.02, w, precision: 9);
        Assert.Equal(0.02, h, precision: 9);
        Assert.Equal(0.2,  x, precision: 9); // br never moves the origin regardless
        Assert.Equal(0.3,  y, precision: 9);
    }

    [Fact]
    public void ApplyResize_TopLeftCollapsingDrag_HoldsOppositeEdgeFixed()
    {
        // Drag tl far past the fixed bottom-right corner — box should clamp, not invert
        var (x, y, w, h) = CalloutResizeMath.ApplyResize(0.2, 0.3, 0.4, 0.2, "tl", 5.0, 5.0, minSize: 0.02);

        Assert.Equal(0.6 - 0.02, x, precision: 9); // left clamped just before the fixed right edge (0.6)
        Assert.Equal(0.5 - 0.02, y, precision: 9); // top clamped just before the fixed bottom edge (0.5)
        Assert.Equal(0.02, w, precision: 9);
        Assert.Equal(0.02, h, precision: 9);
    }
}
