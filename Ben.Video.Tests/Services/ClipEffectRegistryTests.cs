using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Plugins.Video;
using Ben.Video.Editor.Plugins.Image;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class ClipEffectRegistryTests
{
    private static ClipEffectRegistry BuildRegistry()
    {
        var r = new ClipEffectRegistry();
        r.Register(new ColorGradingEffect());
        r.Register(new FadeInEffect());
        r.Register(new FadeOutEffect());
        r.Register(new FadeToBlackEffect());
        r.Register(new FadeToWhiteEffect());
        r.Register(new GrayscaleEffect());
        r.Register(new FlyInFromTopEffect());
        return r;
    }

    // ── Registration ────────────────────────────────────────────────────────────

    [Fact]
    public void Register_AllBuiltIns_AllPresentInAll()
    {
        var registry = BuildRegistry();
        Assert.Equal(7, registry.All.Count);
    }

    [Fact]
    public void GetById_KnownId_ReturnsEffect()
    {
        var registry = BuildRegistry();
        var effect = registry.GetById("color_grading");
        Assert.NotNull(effect);
        Assert.Equal("Color Grading", effect.DisplayName);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.GetById("does_not_exist"));
    }

    [Fact]
    public void Register_Duplicate_ThrowsInvalidOperation()
    {
        var registry = new ClipEffectRegistry();
        registry.Register(new FadeInEffect());
        Assert.Throws<InvalidOperationException>(() => registry.Register(new FadeInEffect()));
    }

    // ── CreateDefault ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDefault_ColorGrading_HasExpectedDefaults()
    {
        var effect = new ColorGradingEffect();
        var applied = effect.CreateDefault();
        Assert.Equal("color_grading", applied.EffectId);
        Assert.Equal(0.0, applied.Parameters["brightness"]);
        Assert.Equal(1.0, applied.Parameters["contrast"]);
        Assert.Equal(1.0, applied.Parameters["saturation"]);
    }

    [Fact]
    public void CreateDefault_FadeIn_HasDuration()
    {
        var applied = new FadeInEffect().CreateDefault();
        Assert.Equal("fade_in", applied.EffectId);
        Assert.True(applied.Parameters.ContainsKey("duration"));
    }

    // ── BuildFilterFragment ──────────────────────────────────────────────────────

    [Fact]
    public void ColorGrading_NeutralParams_ReturnsEmpty()
    {
        var effect = new ColorGradingEffect();
        var p = new Dictionary<string, double> { ["brightness"] = 0, ["contrast"] = 1, ["saturation"] = 1 };
        Assert.Equal(string.Empty, effect.BuildFilterFragment(p, 10.0));
    }

    [Fact]
    public void ColorGrading_NonNeutralBrightness_ReturnsEqFilter()
    {
        var effect = new ColorGradingEffect();
        var p = new Dictionary<string, double> { ["brightness"] = 0.5, ["contrast"] = 1, ["saturation"] = 1 };
        var fragment = effect.BuildFilterFragment(p, 10.0);
        Assert.Contains("eq=brightness=", fragment);
        Assert.Contains("contrast=", fragment);
        Assert.Contains("saturation=", fragment);
    }

    [Fact]
    public void FadeIn_ReturnsCorrectFragment()
    {
        var effect = new FadeInEffect();
        var p = new Dictionary<string, double> { ["duration"] = 2.0 };
        var fragment = effect.BuildFilterFragment(p, 10.0);
        Assert.Equal("fade=t=in:st=0:d=2.000", fragment);
    }

    [Fact]
    public void FadeOut_ReturnsCorrectFragment()
    {
        var effect = new FadeOutEffect();
        var p = new Dictionary<string, double> { ["duration"] = 2.0 };
        var fragment = effect.BuildFilterFragment(p, 10.0);
        Assert.Contains("fade=t=out", fragment);
        Assert.Contains("st=8.000", fragment);
        Assert.Contains("d=2.000", fragment);
    }

    [Fact]
    public void FadeToBlack_ContainsColorBlack()
    {
        var effect = new FadeToBlackEffect();
        var p = new Dictionary<string, double> { ["duration"] = 1.0 };
        Assert.Contains("color=black", effect.BuildFilterFragment(p, 5.0));
    }

    [Fact]
    public void FadeToWhite_ContainsColorWhite()
    {
        var effect = new FadeToWhiteEffect();
        var p = new Dictionary<string, double> { ["duration"] = 1.0 };
        Assert.Contains("color=white", effect.BuildFilterFragment(p, 5.0));
    }

    [Fact]
    public void Grayscale_FullIntensity_ReturnsHueFilter()
    {
        var effect = new GrayscaleEffect();
        var p = new Dictionary<string, double> { ["intensity"] = 1.0 };
        Assert.Contains("hue=s=", effect.BuildFilterFragment(p, 5.0));
    }

    [Fact]
    public void Grayscale_ZeroIntensity_ReturnsEmpty()
    {
        var effect = new GrayscaleEffect();
        var p = new Dictionary<string, double> { ["intensity"] = 0.0 };
        Assert.Equal(string.Empty, effect.BuildFilterFragment(p, 5.0));
    }

    [Fact]
    public void FlyInFromTop_ReturnsNonEmptyFragment()
    {
        var effect = new FlyInFromTopEffect();
        var p = new Dictionary<string, double> { ["duration"] = 0.5 };
        var fragment = effect.BuildFilterFragment(p, 5.0);
        Assert.NotEmpty(fragment);
        Assert.Contains("crop", fragment);
    }

    // ── BuildAppliedEffectsFilter ─────────────────────────────────────────────

    [Fact]
    public void BuildAppliedEffectsFilter_Empty_ReturnsEmpty()
    {
        var registry = BuildRegistry();
        var result = ExportArgBuilders.BuildAppliedEffectsFilter([], registry, 10.0);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_SingleEffect_ReturnsFragment()
    {
        var registry = BuildRegistry();
        var applied = new AppliedEffect { EffectId = "fade_in", Parameters = new() { ["duration"] = 1.0 } };
        var result = ExportArgBuilders.BuildAppliedEffectsFilter([applied], registry, 10.0);
        Assert.Contains("fade=t=in", result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_MultipleEffects_CombinesWithComma()
    {
        var registry = BuildRegistry();
        var effects = new List<AppliedEffect>
        {
            new() { EffectId = "fade_in",      Parameters = new() { ["duration"] = 1.0 } },
            new() { EffectId = "color_grading", Parameters = new() { ["brightness"] = 0.3, ["contrast"] = 1, ["saturation"] = 1 } },
        };
        var result = ExportArgBuilders.BuildAppliedEffectsFilter(effects, registry, 10.0);
        Assert.Contains("fade=t=in", result);
        Assert.Contains("eq=brightness=", result);
        Assert.Contains(",", result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_UnknownEffectId_SkipsGracefully()
    {
        var registry = BuildRegistry();
        var applied = new AppliedEffect { EffectId = "nonexistent", Parameters = new() };
        var result = ExportArgBuilders.BuildAppliedEffectsFilter([applied], registry, 10.0);
        Assert.Equal(string.Empty, result);
    }
}
