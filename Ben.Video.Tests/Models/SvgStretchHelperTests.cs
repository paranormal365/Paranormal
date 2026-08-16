using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class SvgStretchHelperTests
{
    [Fact]
    public void ForceFillDimensions_StripsExistingWidthAndHeight()
    {
        var input = "<svg width=\"200\" height=\"150\" viewBox=\"0 0 200 150\"><rect/></svg>";

        var result = SvgStretchHelper.ForceFillDimensions(input);

        Assert.DoesNotContain("width=\"200\"", result);
        Assert.DoesNotContain("height=\"150\"", result);
        Assert.Contains("width=\"100%\"", result);
        Assert.Contains("height=\"100%\"", result);
    }

    [Fact]
    public void ForceFillDimensions_PreservesOtherAttributes()
    {
        var input = "<svg width=\"200\" height=\"150\" viewBox=\"0 0 200 150\" xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>";

        var result = SvgStretchHelper.ForceFillDimensions(input);

        Assert.Contains("viewBox=\"0 0 200 150\"", result);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", result);
    }

    [Fact]
    public void ForceFillDimensions_AddsNonUniformScaling()
    {
        var input = "<svg width=\"200\" height=\"150\"><rect/></svg>";

        var result = SvgStretchHelper.ForceFillDimensions(input);

        Assert.Contains("preserveAspectRatio=\"none\"", result);
    }

    [Fact]
    public void ForceFillDimensions_NoExistingWidthHeight_StillInjectsFillDimensions()
    {
        var input = "<svg viewBox=\"0 0 200 150\"><rect/></svg>";

        var result = SvgStretchHelper.ForceFillDimensions(input);

        Assert.Contains("width=\"100%\"", result);
        Assert.Contains("height=\"100%\"", result);
    }

    [Fact]
    public void ForceFillDimensions_OnlyReplacesRootSvgTag_NotNestedOnes()
    {
        var input = "<svg width=\"200\" height=\"150\"><svg width=\"50\" height=\"50\"><rect/></svg></svg>";

        var result = SvgStretchHelper.ForceFillDimensions(input);

        // Root tag stretched to fill...
        Assert.StartsWith("<svg width=\"100%\" height=\"100%\" preserveAspectRatio=\"none\">", result);
        // ...nested svg left untouched
        Assert.Contains("<svg width=\"50\" height=\"50\">", result);
    }

    [Fact]
    public void ForceFillDimensions_PreservesRestOfDocument()
    {
        var input = "<svg width=\"200\" height=\"150\"><circle cx=\"10\" cy=\"10\" r=\"5\" fill=\"red\"/></svg>";

        var result = SvgStretchHelper.ForceFillDimensions(input);

        Assert.Contains("<circle cx=\"10\" cy=\"10\" r=\"5\" fill=\"red\"/>", result);
    }
}
