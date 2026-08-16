using Ben.Video.Editor.Effects;

namespace Ben.Video.Tests.Services;

public sealed class SvgShadowFilterTests
{
    [Fact]
    public void Build_BlurZero_ReturnsEmpty()
    {
        var result = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 3.0, 3.0, 0);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Build_NegativeBlur_ReturnsEmpty()
    {
        var result = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 3.0, 3.0, -1.0);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Build_PositiveBlur_ContainsFilterDef()
    {
        var result = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 3.0, 3.0, 4.0);
        Assert.Contains("<defs>", result);
        Assert.Contains("id=\"bv-shadow\"", result);
        Assert.Contains("feDropShadow", result);
    }

    [Fact]
    public void Build_UsesHalfBlurAsStdDeviation()
    {
        var result = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 0, 0, 10.0);
        Assert.Contains("stdDeviation=\"5.000\"", result);
    }

    [Fact]
    public void Build_UsesOffsetsAsDxDy()
    {
        var result = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 7.0, -2.0, 4.0);
        Assert.Contains("dx=\"7.000\"", result);
        Assert.Contains("dy=\"-2.000\"", result);
    }

    [Fact]
    public void Build_UsesAlphaChannel_AsFloodOpacity()
    {
        var halfAlpha = ColorHelper.Pack(0, 0, 0, 128);
        var result    = SvgShadowFilter.Build(halfAlpha, 0, 0, 4.0);
        Assert.Contains($"flood-opacity=\"{(128 / 255.0):F3}\"", result);
    }
}
