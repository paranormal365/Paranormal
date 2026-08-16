using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Effects;

public sealed class FadeEnvelopeTests
{
    [Fact]
    public void Compute_DuringFadeIn_RampsLinearly()
    {
        Assert.Equal(0.0, FadeEnvelope.Compute(0.0, 4.0, 1.0, 1.0), 3);
        Assert.Equal(0.5, FadeEnvelope.Compute(0.5, 4.0, 1.0, 1.0), 3);
        Assert.Equal(1.0, FadeEnvelope.Compute(1.0, 4.0, 1.0, 1.0), 3);
    }

    [Fact]
    public void Compute_DuringFadeOut_RampsLinearlyDown()
    {
        Assert.Equal(1.0, FadeEnvelope.Compute(3.0, 4.0, 1.0, 1.0), 3);
        Assert.Equal(0.5, FadeEnvelope.Compute(3.5, 4.0, 1.0, 1.0), 3);
        Assert.Equal(0.0, FadeEnvelope.Compute(4.0, 4.0, 1.0, 1.0), 3);
    }

    [Fact]
    public void Compute_NoFades_AlwaysFullyOpaque()
    {
        Assert.Equal(1.0, FadeEnvelope.Compute(0.0, 4.0, 0.0, 0.0), 3);
        Assert.Equal(1.0, FadeEnvelope.Compute(4.0, 4.0, 0.0, 0.0), 3);
    }

    [Fact]
    public void Compute_PastLifetimeEnd_ClampsToZero()
    {
        Assert.Equal(0.0, FadeEnvelope.Compute(5.0, 4.0, 1.0, 1.0), 3);
    }

    // ── CalloutClip integration (new fade fields, backlog #29 phase) ─────────

    [Fact]
    public void CalloutClip_ComputeFadeAlpha_UsesOwnFadeFields()
    {
        var clip = new CalloutClip { Duration = 4.0, FadeInSeconds = 1.0, FadeOutSeconds = 1.0 };

        Assert.Equal(0.5, clip.ComputeFadeAlpha(0.5), 3);
        Assert.Equal(1.0, clip.ComputeFadeAlpha(2.0), 3);
        Assert.Equal(0.5, clip.ComputeFadeAlpha(3.5), 3);
    }

    [Fact]
    public void CalloutClip_FadeDefaults_AreZero_PreservingExistingBehavior()
    {
        var clip = new CalloutClip { Duration = 4.0 };

        Assert.Equal(0.0, clip.FadeInSeconds);
        Assert.Equal(0.0, clip.FadeOutSeconds);
        Assert.Equal(1.0, clip.ComputeFadeAlpha(0.0), 3);
    }
}
