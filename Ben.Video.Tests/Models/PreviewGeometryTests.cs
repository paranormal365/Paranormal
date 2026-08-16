using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class PreviewGeometryTests
{
    [Fact]
    public void ComputeContentBox_WiderCanvasThanContainer_LettersboxesTopAndBottom()
    {
        // 16:9 canvas inside a 1:1 (square) container of 400x400 -> fills width, centers vertically
        var (offsetX, offsetY, width, height) = PreviewGeometry.ComputeContentBox(400, 400, 1920, 1080);

        Assert.Equal(0.0,   offsetX, precision: 6);
        Assert.Equal(400.0, width,   precision: 6);
        Assert.Equal(225.0, height,  precision: 6); // 400 / (16/9)
        Assert.Equal(87.5,  offsetY, precision: 6); // (400 - 225) / 2
    }

    [Fact]
    public void ComputeContentBox_TallerCanvasThanContainer_PillarboxesLeftAndRight()
    {
        // 9:16 (portrait) canvas inside a 400x400 square container -> fills height, centers horizontally
        var (offsetX, offsetY, width, height) = PreviewGeometry.ComputeContentBox(400, 400, 1080, 1920);

        Assert.Equal(0.0,   offsetY, precision: 6);
        Assert.Equal(400.0, height,  precision: 6);
        Assert.Equal(225.0, width,   precision: 6); // 400 * (1080/1920)
        Assert.Equal(87.5,  offsetX, precision: 6);
    }

    [Fact]
    public void ComputeContentBox_MatchingAspectRatio_FillsContainerExactly()
    {
        var (offsetX, offsetY, width, height) = PreviewGeometry.ComputeContentBox(800, 450, 1920, 1080);

        Assert.Equal(0.0, offsetX, precision: 6);
        Assert.Equal(0.0, offsetY, precision: 6);
        Assert.Equal(800.0, width,  precision: 6);
        Assert.Equal(450.0, height, precision: 6);
    }

    [Fact]
    public void ComputeContentBox_NonPositiveDimension_FallsBackToFillingContainer()
    {
        var (offsetX, offsetY, width, height) = PreviewGeometry.ComputeContentBox(400, 300, 0, 0);

        Assert.Equal(0.0, offsetX, precision: 6);
        Assert.Equal(0.0, offsetY, precision: 6);
        Assert.Equal(400.0, width,  precision: 6);
        Assert.Equal(300.0, height, precision: 6);
    }

    [Fact]
    public void ToFraction_CenterOfContentBox_ReturnsHalfHalf()
    {
        var (fx, fy) = PreviewGeometry.ToFraction(
            containerLocalX: 200, containerLocalY: 200,
            contentOffsetX: 0, contentOffsetY: 87.5, contentWidth: 400, contentHeight: 225);

        Assert.Equal(0.5, fx, precision: 6);
        Assert.Equal(0.5, fy, precision: 6);
    }

    [Fact]
    public void ToFraction_PointOutsideContentBox_ClampsToZeroOrOne()
    {
        var (fx, fy) = PreviewGeometry.ToFraction(
            containerLocalX: -50, containerLocalY: 1000,
            contentOffsetX: 0, contentOffsetY: 87.5, contentWidth: 400, contentHeight: 225);

        Assert.Equal(0.0, fx, precision: 6);
        Assert.Equal(1.0, fy, precision: 6);
    }

    [Fact]
    public void DeltaToFraction_ScalesByContentSize_NotContainerSize()
    {
        // A 40px move across a 400px-wide content box is a 0.1 fraction delta —
        // this is the core fix: previously code divided by a fixed/native canvas dimension
        // (e.g. 1920) instead of the actual on-screen rendered content width.
        var (fdx, fdy) = PreviewGeometry.DeltaToFraction(40, 22.5, contentWidth: 400, contentHeight: 225);

        Assert.Equal(0.1, fdx, precision: 6);
        Assert.Equal(0.1, fdy, precision: 6);
    }

    [Fact]
    public void DeltaToFraction_ZeroContentSize_ReturnsZero()
    {
        var (fdx, fdy) = PreviewGeometry.DeltaToFraction(40, 40, contentWidth: 0, contentHeight: 0);

        Assert.Equal(0.0, fdx, precision: 6);
        Assert.Equal(0.0, fdy, precision: 6);
    }
}
