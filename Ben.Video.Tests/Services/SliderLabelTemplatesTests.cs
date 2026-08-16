using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #30 fix — <see cref="SliderLabelTemplates.LabelFor{TValue}"/> is the crux: only the first
/// and last tick Kendo actually generates should render a label, everything in between should
/// render nothing (this is what eliminates the bunching from Kendo's default per-LargeStep
/// labels). Crucially, Kendo does not always generate a tick exactly at Max — see the
/// non-step-aligned range tests below.
/// </summary>
public sealed class SliderLabelTemplatesTests
{
    [Fact]
    public void LabelFor_AtMin_ReturnsLabel()
    {
        Assert.Equal("-180", SliderLabelTemplates.LabelFor(-180, -180, 180, 45));
    }

    [Fact]
    public void LabelFor_AtMaxWhenStepAligned_ReturnsLabel()
    {
        // (180 - -180) / 45 == 8, an exact multiple, so Kendo's last generated tick is 180 itself.
        Assert.Equal("180", SliderLabelTemplates.LabelFor(180, -180, 180, 45));
    }

    [Theory]
    [InlineData(-135)]
    [InlineData(-45)]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(135)]
    public void LabelFor_AtIntermediateTick_ReturnsNull(double tick)
    {
        Assert.Null(SliderLabelTemplates.LabelFor(tick, -180, 180, 45));
    }

    [Fact]
    public void LabelFor_NonStepAlignedRange_LabelsHighestGeneratedTickNotMax()
    {
        // Min=0, Max=13.8, LargeStep=1 — Kendo generates 0,1,2,...,13 and stops (13.8 is never a
        // tick). The highest generated tick (13) should be labeled; 13.8 itself never occurs as a
        // tick value, so there's nothing to assert about it directly.
        Assert.Equal("0", SliderLabelTemplates.LabelFor(0, 0, 13.8, 1));
        Assert.Equal("13", SliderLabelTemplates.LabelFor(13, 0, 13.8, 1));
        Assert.Null(SliderLabelTemplates.LabelFor(12, 0, 13.8, 1));
        Assert.Null(SliderLabelTemplates.LabelFor(7, 0, 13.8, 1));
    }

    [Fact]
    public void LabelFor_FractionalEndpoint_FormatsWithoutTrailingZeros()
    {
        Assert.Equal("0.1", SliderLabelTemplates.LabelFor(0.1, 0.1, 60.0, 5.0));
        Assert.Equal("60", SliderLabelTemplates.LabelFor(60.0, 0.1, 60.0, 5.0));
    }

    [Fact]
    public void LabelFor_ZeroToOneRange_FormatsCleanly()
    {
        Assert.Equal("0", SliderLabelTemplates.LabelFor(0.0, 0.0, 1.0, 0.1));
        Assert.Equal("1", SliderLabelTemplates.LabelFor(1.0, 0.0, 1.0, 0.1));
    }

    [Fact]
    public void LabelFor_MinEqualsMax_StillLabelsTheSinglePoint()
    {
        // Degenerate but defensive: a zero-width slider (e.g. Min==Max==0 trim range) shouldn't throw.
        Assert.Equal("5", SliderLabelTemplates.LabelFor(5, 5, 5, 1));
    }

    [Fact]
    public void LabelFor_IntTValue_WorksLikeDouble()
    {
        // TelerikSlider<int> is used for a couple of Properties-panel fields (e.g. font size) —
        // LabelFor must be generic enough to cover both int and double sliders.
        Assert.Equal("12", SliderLabelTemplates.LabelFor(12, 12, 120, 10));
        Assert.Equal("120", SliderLabelTemplates.LabelFor(120, 12, 120, 10));
        Assert.Null(SliderLabelTemplates.LabelFor(60, 12, 120, 10));
    }

    [Fact]
    public void Endpoints_ProducesARenderFragmentFactory()
    {
        var factory = SliderLabelTemplates.Endpoints(0, 10, 1);
        Assert.NotNull(factory);
        Assert.NotNull(factory(0));
        Assert.NotNull(factory(10));
    }
}
