using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class EffectCommandTests
{
    private static VideoClip MakeClip() => new()
    {
        Name     = "test.mp4",
        Duration = 10.0,
    };

    private static AppliedEffect MakeFade() => new()
    {
        EffectId   = "fade_in",
        Parameters = new() { ["duration"] = 1.0 },
    };

    // ── AddEffectCommand ─────────────────────────────────────────────────────

    [Fact]
    public void AddEffect_Execute_AddsEffectToClip()
    {
        var clip    = MakeClip();
        var effect  = MakeFade();
        var command = new AddEffectCommand(clip, effect);

        command.Execute();

        Assert.Single(clip.AppliedEffects);
        Assert.Same(effect, clip.AppliedEffects[0]);
    }

    [Fact]
    public void AddEffect_Undo_RemovesEffect()
    {
        var clip    = MakeClip();
        var effect  = MakeFade();
        var command = new AddEffectCommand(clip, effect);

        command.Execute();
        command.Undo();

        Assert.Empty(clip.AppliedEffects);
    }

    [Fact]
    public void AddEffect_Description_IsNotEmpty()
    {
        var cmd = new AddEffectCommand(MakeClip(), MakeFade());
        Assert.NotEmpty(cmd.Description);
    }

    // ── RemoveEffectCommand ───────────────────────────────────────────────────

    [Fact]
    public void RemoveEffect_Execute_RemovesEffect()
    {
        var clip   = MakeClip();
        var effect = MakeFade();
        clip.AppliedEffects.Add(effect);

        var command = new RemoveEffectCommand(clip, effect);
        command.Execute();

        Assert.Empty(clip.AppliedEffects);
    }

    [Fact]
    public void RemoveEffect_Undo_RestoresAtOriginalIndex()
    {
        var clip   = MakeClip();
        var fx1    = new AppliedEffect { EffectId = "fade_in",  Parameters = new() { ["duration"] = 1.0 } };
        var fx2    = new AppliedEffect { EffectId = "grayscale", Parameters = new() { ["intensity"] = 1.0 } };
        clip.AppliedEffects.Add(fx1);
        clip.AppliedEffects.Add(fx2);

        var command = new RemoveEffectCommand(clip, fx1);
        command.Execute();
        command.Undo();

        Assert.Equal(2, clip.AppliedEffects.Count);
        Assert.Same(fx1, clip.AppliedEffects[0]);
    }

    [Fact]
    public void RemoveEffect_Description_IsNotEmpty()
    {
        var cmd = new RemoveEffectCommand(MakeClip(), MakeFade());
        Assert.NotEmpty(cmd.Description);
    }

    // ── UpdateEffectParameterCommand ──────────────────────────────────────────

    [Fact]
    public void UpdateEffectParameter_Execute_SetsNewValue()
    {
        var effect  = MakeFade();
        var command = new UpdateEffectParameterCommand(effect, "duration", 3.0);

        command.Execute();

        Assert.Equal(3.0, effect.Parameters["duration"]);
    }

    [Fact]
    public void UpdateEffectParameter_Undo_RestoresOldValue()
    {
        var effect  = MakeFade();
        var command = new UpdateEffectParameterCommand(effect, "duration", 3.0);

        command.Execute();
        command.Undo();

        Assert.Equal(1.0, effect.Parameters["duration"]);
    }

    [Fact]
    public void UpdateEffectParameter_Description_IsNotEmpty()
    {
        var cmd = new UpdateEffectParameterCommand(MakeFade(), "duration", 2.0);
        Assert.NotEmpty(cmd.Description);
    }

    // ── AppliedEffect.Clone ───────────────────────────────────────────────────

    [Fact]
    public void AppliedEffect_Clone_ProducesDeepCopy()
    {
        var original = new AppliedEffect
        {
            EffectId   = "fade_in",
            Parameters = new() { ["duration"] = 2.0 },
        };

        var clone = original.Clone();

        Assert.Equal(original.EffectId, clone.EffectId);
        Assert.Equal(original.Parameters["duration"], clone.Parameters["duration"]);

        // Mutating clone must not affect original
        clone.Parameters["duration"] = 99.0;
        Assert.Equal(2.0, original.Parameters["duration"]);
    }
}
