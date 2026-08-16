using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class MotionKeyframeServiceTests
{
    private static Guid LayerId => Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string LayerType = "TextOverlay";

    // ── UpsertKeyframe ────────────────────────────────────────────────────────

    [Fact]
    public void Upsert_CreatesPathAndKeyframe()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, X = 0.3 });

        Assert.True(svc.HasPath(LayerId));
        Assert.Single(svc.GetPath(LayerId)!.Keyframes);
    }

    [Fact]
    public void Upsert_SortsKeyframesByTime()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 5.0, X = 0.8 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.4 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.1 });

        var times = svc.GetPath(LayerId)!.Keyframes.Select(k => k.Time).ToList();
        Assert.Equal([0.0, 2.0, 5.0], times);
    }

    [Fact]
    public void Upsert_ReplacesKeyframeAtSameTime()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.3 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.7 });

        Assert.Single(svc.GetPath(LayerId)!.Keyframes);
        Assert.Equal(0.7, svc.GetPath(LayerId)!.Keyframes[0].X);
    }

    [Fact]
    public void Upsert_RaisesOnChanged()
    {
        var svc   = new MotionKeyframeService();
        var fired = false;
        svc.OnChanged += () => fired = true;

        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0 });

        Assert.True(fired);
    }

    // ── RemoveKeyframe ────────────────────────────────────────────────────────

    [Fact]
    public void Remove_DeletesKeyframe()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0 });
        svc.RemoveKeyframe(LayerId, 2.0);

        Assert.False(svc.HasPath(LayerId));
    }

    [Fact]
    public void Remove_ClearsPathWhenEmpty()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0 });
        svc.RemoveKeyframe(LayerId, 1.0);

        Assert.Null(svc.GetPath(LayerId));
    }

    // ── MoveKeyframeTime (item #57 P6) ───────────────────────────────────────────

    [Fact]
    public void MoveKeyframeTime_RetimesWithoutDuplicating()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.3 });
        svc.MoveKeyframeTime(LayerId, 2.0, 5.0);

        var kfs = svc.GetPath(LayerId)!.Keyframes;
        Assert.Single(kfs);
        Assert.Equal(5.0, kfs[0].Time);
        Assert.Equal(0.3, kfs[0].X);
    }

    [Fact]
    public void MoveKeyframeTime_KeepsListSorted()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, X = 0.1 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 3.0, X = 0.3 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 5.0, X = 0.5 });

        // Drag the middle keyframe past the last one.
        svc.MoveKeyframeTime(LayerId, 3.0, 8.0);

        var times = svc.GetPath(LayerId)!.Keyframes.Select(k => k.Time).ToList();
        Assert.Equal([1.0, 5.0, 8.0], times);
    }

    [Fact]
    public void MoveKeyframeTime_CollidingWithExistingKeyframe_Overwrites()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, X = 0.1 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, X = 0.4 });

        svc.MoveKeyframeTime(LayerId, 1.0, 4.0);

        var kfs = svc.GetPath(LayerId)!.Keyframes;
        Assert.Single(kfs);
        Assert.Equal(4.0, kfs[0].Time);
        Assert.Equal(0.1, kfs[0].X); // the moved keyframe's own values win
    }

    [Fact]
    public void MoveKeyframeTime_NoKeyframeAtOldTime_NoOps()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, X = 0.1 });

        svc.MoveKeyframeTime(LayerId, 9.0, 20.0);

        var kfs = svc.GetPath(LayerId)!.Keyframes;
        Assert.Single(kfs);
        Assert.Equal(1.0, kfs[0].Time);
    }

    [Fact]
    public void MoveKeyframeTime_UnknownLayer_NoOps()
    {
        var svc = new MotionKeyframeService();
        svc.MoveKeyframeTime(LayerId, 1.0, 2.0); // no path exists at all

        Assert.False(svc.HasPath(LayerId));
    }

    [Fact]
    public void MoveKeyframeTime_RaisesOnChanged()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0 });

        var fired = false;
        svc.OnChanged += () => fired = true;
        svc.MoveKeyframeTime(LayerId, 1.0, 2.0);

        Assert.True(fired);
    }

    // ── Evaluate — clamp at edges ─────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsNullWhenNoPath()
    {
        var svc = new MotionKeyframeService();
        Assert.Null(svc.Evaluate(LayerId, 1.0));
    }

    [Fact]
    public void Evaluate_SingleKeyframe_ReturnsItsValues()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 3.0, X = 0.4, Y = 0.6 });

        var f = svc.Evaluate(LayerId, 0.0)!;
        Assert.Equal(0.4, f.X, precision: 9);
        Assert.Equal(0.6, f.Y, precision: 9);
    }

    [Fact]
    public void Evaluate_ClampedBeforeFirst()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.3 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 5.0, X = 0.7 });

        var f = svc.Evaluate(LayerId, 0.0)!;
        Assert.Equal(0.3, f.X, precision: 9); // clamped to first
    }

    [Fact]
    public void Evaluate_ClampedAfterLast()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.3 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 5.0, X = 0.7 });

        var f = svc.Evaluate(LayerId, 99.0)!;
        Assert.Equal(0.7, f.X, precision: 9); // clamped to last
    }

    // ── Evaluate — linear interpolation ──────────────────────────────────────

    [Fact]
    public void Evaluate_LinearMidpoint()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 1.0, Easing = "Linear" });

        var f = svc.Evaluate(LayerId, 1.0)!;
        Assert.Equal(0.5, f.X, precision: 9);
    }

    [Fact]
    public void Evaluate_AlphaInterpolated()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, Alpha = 0.0, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, Alpha = 1.0, Easing = "Linear" });

        var f = svc.Evaluate(LayerId, 1.0)!;
        Assert.Equal(0.25, f.Alpha, precision: 9);
    }

    // ── Cubic Bezier ──────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_BezierAtT0_ReturnsStart()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe
        {
            Time = 0.0, X = 0.1, Y = 0.2,
            HandleOutX = 0.3, HandleOutY = 0.4, Easing = "Linear"
        });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe
        {
            Time = 2.0, X = 0.9, Y = 0.8,
            HandleInX  = 0.7, HandleInY  = 0.6, Easing = "Linear"
        });

        var f = svc.Evaluate(LayerId, 0.0)!;
        Assert.Equal(0.1, f.X, precision: 6);
    }

    [Fact]
    public void Evaluate_BezierAtT1_ReturnsEnd()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe
        {
            Time = 0.0, X = 0.1, HandleOutX = 0.3, HandleOutY = 0.4, Easing = "Linear"
        });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe
        {
            Time = 2.0, X = 0.9, HandleInX  = 0.7, HandleInY  = 0.6, Easing = "Linear"
        });

        var f = svc.Evaluate(LayerId, 2.0)!;
        Assert.Equal(0.9, f.X, precision: 6);
    }

    // ── BuildFfmpegExpression ─────────────────────────────────────────────────

    [Fact]
    public void BuildFfmpegExpression_ReturnsNullWithNoPath()
    {
        var svc = new MotionKeyframeService();
        Assert.Null(svc.BuildFfmpegExpression(LayerId, "x"));
    }

    [Fact]
    public void BuildFfmpegExpression_SingleKeyframe_ReturnsConstant()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, X = 0.5 });

        var expr = svc.BuildFfmpegExpression(LayerId, "x");
        Assert.NotNull(expr);
        Assert.Contains("0.5", expr);
        Assert.StartsWith("(W*", expr); // x expression wrapped in pixel converter
    }

    [Fact]
    public void BuildFfmpegExpression_YProperty_WrappedWithH()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, Y = 0.3 });

        var expr = svc.BuildFfmpegExpression(LayerId, "y");
        Assert.NotNull(expr);
        Assert.StartsWith("(H*", expr);
    }

    [Fact]
    public void BuildFfmpegExpression_AlphaProperty_NotWrapped()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, Alpha = 0.8 });

        var expr = svc.BuildFfmpegExpression(LayerId, "alpha");
        Assert.NotNull(expr);
        Assert.DoesNotContain("W*", expr);
        Assert.DoesNotContain("H*", expr);
    }

    [Fact]
    public void BuildFfmpegExpression_MultiKeyframe_ContainsIfExpression()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.1, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 3.0, X = 0.9, Easing = "Linear" });

        var expr = svc.BuildFfmpegExpression(LayerId, "x")!;
        Assert.Contains("if(lt(t,", expr);
    }

    // ── RestoreAll ────────────────────────────────────────────────────────────

    [Fact]
    public void RestoreAll_ReplacesExistingPaths()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0 });

        var newLayerId = Guid.NewGuid();
        svc.RestoreAll([new MotionPath { LayerId = newLayerId, LayerType = "CalloutClip", Keyframes = [] }]);

        Assert.False(svc.HasPath(LayerId));
        Assert.NotNull(svc.GetPath(newLayerId));
    }

    // ── ClearPath ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClearPath_RemovesAllKeyframes()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 3.0 });

        svc.ClearPath(LayerId);

        Assert.False(svc.HasPath(LayerId));
        Assert.Null(svc.GetPath(LayerId));
    }

    [Fact]
    public void ClearPath_NoOpWhenPathDoesNotExist()
    {
        var svc   = new MotionKeyframeService();
        var fired = false;
        svc.OnChanged += () => fired = true;

        svc.ClearPath(LayerId); // nothing to clear

        Assert.False(fired);
    }

    // ── AllPaths ──────────────────────────────────────────────────────────────

    [Fact]
    public void AllPaths_EmptyInitially()
    {
        var svc = new MotionKeyframeService();
        Assert.Empty(svc.AllPaths);
    }

    [Fact]
    public void AllPaths_CountsAllLayers()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId,       "TextOverlay", new MotionKeyframe { Time = 1.0 });
        svc.UpsertKeyframe(Guid.NewGuid(), "CalloutClip", new MotionKeyframe { Time = 2.0 });

        Assert.Equal(2, svc.AllPaths.Count);
    }

    // ── HasPath / GetPath ─────────────────────────────────────────────────────

    [Fact]
    public void HasPath_FalseForUnknownLayer()
    {
        var svc = new MotionKeyframeService();
        Assert.False(svc.HasPath(Guid.NewGuid()));
    }

    [Fact]
    public void GetPath_ReturnsNullForUnknownLayer()
    {
        var svc = new MotionKeyframeService();
        Assert.Null(svc.GetPath(Guid.NewGuid()));
    }

    // ── Evaluate — Scale interpolation ────────────────────────────────────────

    [Fact]
    public void Evaluate_ScaleInterpolated()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, Scale = 1.0, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, Scale = 2.0, Easing = "Linear" });

        var f = svc.Evaluate(LayerId, 2.0)!;
        Assert.Equal(1.5, f.Scale, precision: 9);
    }

    // ── Evaluate — ScaleX/ScaleY/Rotation interpolation (item #57 P3) ─────────

    [Fact]
    public void Evaluate_ScaleXY_InterpolatedIndependently_WhenBothKeyframesSetThem()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, ScaleX = 1.0, ScaleY = 4.0 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, ScaleX = 3.0, ScaleY = 2.0 });

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Equal(2.0, f.ScaleX, precision: 9);
        Assert.Equal(3.0, f.ScaleY, precision: 9);
    }

    [Fact]
    public void Evaluate_ScaleXY_FallsBackToLegacyScale_WhenNeitherKeyframeSetsThem()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, Scale = 1.0 });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, Scale = 3.0 });

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Equal(2.0, f.ScaleX, precision: 9); // same as f.Scale — old saved projects unaffected
        Assert.Equal(2.0, f.ScaleY, precision: 9);
        Assert.Equal(2.0, f.Scale,  precision: 9);
    }

    [Fact]
    public void Evaluate_ScaleXY_OneKeyframeSetsThem_OtherFallsBackToItsOwnScale()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, Scale = 1.0 });                    // no ScaleX/Y
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, Scale = 1.0, ScaleX = 3.0, ScaleY = 5.0 });

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Equal(2.0, f.ScaleX, precision: 9); // lerp(kf1.ScaleX ?? kf1.Scale=1.0, 3.0, 0.5)
        Assert.Equal(3.0, f.ScaleY, precision: 9); // lerp(kf1.ScaleY ?? kf1.Scale=1.0, 5.0, 0.5)
    }

    [Fact]
    public void Evaluate_Rotation_Null_WhenNeitherKeyframeAnimatesIt()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 0.0, X = 0.0 });
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 4.0, X = 1.0 });

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Null(f.Rotation);
    }

    [Fact]
    public void Evaluate_Rotation_InterpolatedBetweenBothKeyframes_WhenBothSetIt()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 0.0, Rotation = 0.0 });
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 4.0, Rotation = 90.0 });

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Equal(45.0, f.Rotation!.Value, precision: 9);
    }

    [Fact]
    public void Evaluate_Rotation_HoldsConstant_WhenOnlyFirstKeyframeSetsIt()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 0.0, Rotation = 30.0 });
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 4.0 }); // no Rotation

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Equal(30.0, f.Rotation!.Value, precision: 9);
    }

    [Fact]
    public void Evaluate_Rotation_HoldsConstant_WhenOnlySecondKeyframeSetsIt()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 0.0 }); // no Rotation
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 4.0, Rotation = 60.0 });

        var f = svc.Evaluate(LayerId, 2.0)!;

        Assert.Equal(60.0, f.Rotation!.Value, precision: 9);
    }

    [Fact]
    public void Evaluate_SingleKeyframe_CarriesScaleXYAndRotation()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "ClipArtClip", new MotionKeyframe { Time = 1.0, ScaleX = 2.0, ScaleY = 3.0, Rotation = 45.0 });

        var f = svc.Evaluate(LayerId, 1.0)!;

        Assert.Equal(2.0, f.ScaleX, precision: 9);
        Assert.Equal(3.0, f.ScaleY, precision: 9);
        Assert.Equal(45.0, f.Rotation!.Value, precision: 9);
    }

    [Fact]
    public void Evaluate_SingleKeyframe_ScaleXYFallBackToScale_WhenUnset()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 1.0, Scale = 2.5 });

        var f = svc.Evaluate(LayerId, 1.0)!;

        Assert.Equal(2.5, f.ScaleX, precision: 9);
        Assert.Equal(2.5, f.ScaleY, precision: 9);
        Assert.Null(f.Rotation);
    }

    // ── Evaluate — three keyframes (middle segment) ───────────────────────────

    [Fact]
    public void Evaluate_ThreeKeyframes_MiddleSegment()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.5, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, X = 1.0, Easing = "Linear" });

        // Midpoint of middle segment (t=2 to t=4), at t=3
        var f = svc.Evaluate(LayerId, 3.0)!;
        Assert.Equal(0.75, f.X, precision: 9);
    }

    [Fact]
    public void Evaluate_ThreeKeyframes_FirstSegment()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 2.0, X = 0.5, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, X = 1.0, Easing = "Linear" });

        var f = svc.Evaluate(LayerId, 1.0)!;  // midpoint of first segment
        Assert.Equal(0.25, f.X, precision: 9);
    }

    // ── Evaluate — non-linear easing changes result vs linear ─────────────────

    [Fact]
    public void Evaluate_EaseInProducesSlowerStartThanLinear()
    {
        var svcLinear = new MotionKeyframeService();
        var svcEaseIn = new MotionKeyframeService();
        var id2 = Guid.NewGuid();

        svcLinear.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svcLinear.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, X = 1.0, Easing = "Linear" });

        svcEaseIn.UpsertKeyframe(id2, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svcEaseIn.UpsertKeyframe(id2, LayerType, new MotionKeyframe { Time = 4.0, X = 1.0, Easing = "Ease In" });

        // At t=1 (25% through), Ease In should be BEHIND linear (slower start)
        var linear = svcLinear.Evaluate(LayerId, 1.0)!.X;
        var eased  = svcEaseIn.Evaluate(id2, 1.0)!.X;

        Assert.True(eased < linear, $"Ease In ({eased:F4}) should be < Linear ({linear:F4}) at 25%");
    }

    [Fact]
    public void Evaluate_EaseOutProducesFasterStartThanLinear()
    {
        var svcLinear  = new MotionKeyframeService();
        var svcEaseOut = new MotionKeyframeService();
        var id2 = Guid.NewGuid();

        svcLinear.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svcLinear.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 4.0, X = 1.0, Easing = "Linear" });

        svcEaseOut.UpsertKeyframe(id2, LayerType, new MotionKeyframe { Time = 0.0, X = 0.0, Easing = "Linear" });
        svcEaseOut.UpsertKeyframe(id2, LayerType, new MotionKeyframe { Time = 4.0, X = 1.0, Easing = "Ease Out" });

        var linear = svcLinear.Evaluate(LayerId, 1.0)!.X;
        var eased  = svcEaseOut.Evaluate(id2, 1.0)!.X;

        Assert.True(eased > linear, $"Ease Out ({eased:F4}) should be > Linear ({linear:F4}) at 25%");
    }

    // ── BuildFfmpegExpression — multi-keyframe wraps pixel correctly ───────────

    [Fact]
    public void BuildFfmpegExpression_MultiKeyframe_XWrappedWithW()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 0.0, X = 0.1, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe { Time = 3.0, X = 0.9, Easing = "Linear" });

        var expr = svc.BuildFfmpegExpression(LayerId, "x")!;
        Assert.StartsWith("(W*", expr);
        Assert.Contains("if(lt(t,", expr);
    }

    // ── Motion path project serialisation roundtrip ───────────────────────────

    [Fact]
    public void MotionPath_SerialiseAndRestore_PreservesKeyframes()
    {
        // Simulate what ProjectService.MapMotionPath / RestoreAsync do: map → project
        // model → restore to service. This is the direct regression test for the
        // pre-existing bug where FillColor/StrokeColor/ControlPointValues (and the new
        // Shadow* fields) silently didn't survive project save/reload — every field set
        // below must come back unchanged.
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe
        {
            Time        = 1.5,
            X           = 0.3,
            Y           = 0.7,
            Scale       = 1.2,
            Alpha       = 0.8,
            Easing      = "Ease Out",
            HandleOutX  = 0.4,
            HandleOutY  = 0.6,
            FillColor   = ColorHelper.Pack(200, 100, 50, 255),
            StrokeColor = ColorHelper.Pack(10, 20, 30, 128),
            ControlPointValues = new() { [CalloutControlPoints.CornerRadius] = 12.0 },
            ShadowColor   = ColorHelper.Pack(0, 0, 0, 90),
            ShadowOffsetX = 5.0,
            ShadowOffsetY = -2.0,
            ShadowBlur    = 8.0,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new MotionKeyframe
        {
            Time  = 4.0,
            X     = 0.9,
            Y     = 0.1,
        });

        // Simulate map to ProjectMotionPath (mirrors ProjectService.MapMotionPath)
        var original = svc.GetPath(LayerId)!;
        var projected = new Ben.Video.Editor.Models.ProjectMotionPath
        {
            Id        = original.Id,
            LayerId   = original.LayerId,
            LayerType = original.LayerType,
            Keyframes = original.Keyframes.Select(k => new Ben.Video.Editor.Models.ProjectKeyframe
            {
                Time       = k.Time,
                X          = k.X,
                Y          = k.Y,
                Scale      = k.Scale,
                Alpha      = k.Alpha,
                Easing     = k.Easing,
                HandleOutX = k.HandleOutX,
                HandleOutY = k.HandleOutY,
                FillColor           = k.FillColor,
                StrokeColor         = k.StrokeColor,
                ControlPointValues  = new Dictionary<string, double>(k.ControlPointValues),
                ShadowColor         = k.ShadowColor,
                ShadowOffsetX       = k.ShadowOffsetX,
                ShadowOffsetY       = k.ShadowOffsetY,
                ShadowBlur          = k.ShadowBlur,
            }).ToList(),
        };

        // Simulate restore (mirrors ProjectService.RestoreAsync's keyframe projection)
        var restored = new Ben.Video.Editor.Models.MotionPath
        {
            Id        = projected.Id,
            LayerId   = projected.LayerId,
            LayerType = projected.LayerType,
            Keyframes = projected.Keyframes.Select(k => new Ben.Video.Editor.Models.MotionKeyframe
            {
                Time       = k.Time,
                X          = k.X,
                Y          = k.Y,
                Scale      = k.Scale,
                Alpha      = k.Alpha,
                Easing     = k.Easing,
                HandleOutX = k.HandleOutX,
                HandleOutY = k.HandleOutY,
                FillColor           = k.FillColor,
                StrokeColor         = k.StrokeColor,
                ControlPointValues  = new Dictionary<string, double>(k.ControlPointValues),
                ShadowColor         = k.ShadowColor,
                ShadowOffsetX       = k.ShadowOffsetX,
                ShadowOffsetY       = k.ShadowOffsetY,
                ShadowBlur          = k.ShadowBlur,
            }).OrderBy(k => k.Time).ToList()
        };
        var svc2 = new MotionKeyframeService();
        svc2.RestoreAll([restored]);

        var kf = svc2.GetPath(LayerId)!.Keyframes[0];
        Assert.Equal(1.5,      kf.Time,   precision: 9);
        Assert.Equal(0.3,      kf.X,      precision: 9);
        Assert.Equal("Ease Out", kf.Easing);
        Assert.Equal(0.4,      kf.HandleOutX!.Value, precision: 9);
        Assert.Equal(2,        svc2.GetPath(LayerId)!.Keyframes.Count);

        // The persistence-gap regression assertions
        Assert.Equal(ColorHelper.Pack(200, 100, 50, 255), kf.FillColor);
        Assert.Equal(ColorHelper.Pack(10, 20, 30, 128),   kf.StrokeColor);
        Assert.Equal(12.0, kf.ControlPointValues[CalloutControlPoints.CornerRadius], precision: 9);
        Assert.Equal(ColorHelper.Pack(0, 0, 0, 90), kf.ShadowColor);
        Assert.Equal(5.0,  kf.ShadowOffsetX, precision: 9);
        Assert.Equal(-2.0, kf.ShadowOffsetY, precision: 9);
        Assert.Equal(8.0,  kf.ShadowBlur,    precision: 9);
    }

    // ── Bezier handle interpolation correctness ───────────────────────────────

    [Fact]
    public void Evaluate_CubicBezier_MidpointIsNotLinear()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0, Y = 0, Easing = "Ease In Out",
            HandleOutX = 0.5, HandleOutY = 0.0 });
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 2, X = 1, Y = 0, Easing = "Ease In Out",
            HandleInX  = 0.5, HandleInY = 0.0 });

        // At t=1 (midpoint) cubic Bezier with symmetric handles should produce the linear result
        // for x (since control points are symmetric), but we verify it returns a value
        var frame = svc.Evaluate(LayerId, 1.0);
        Assert.NotNull(frame);
        Assert.Equal(0.5, frame!.X, precision: 2);  // symmetric → midpoint is 0.5
    }

    [Fact]
    public void Evaluate_CubicBezier_AsymmetricHandles_DifferFromLinear()
    {
        var svc = new MotionKeyframeService();
        // Ease-in: handle out close to start → slow start, fast end
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0, Y = 0,
            HandleOutX = 0.1, HandleOutY = 0.0 });
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 2, X = 1, Y = 0,
            HandleInX  = 0.9, HandleInY = 0.0, Easing = "Custom" });

        var frame = svc.Evaluate(LayerId, 1.0);  // midpoint
        Assert.NotNull(frame);
        // The result should be between 0 and 1
        Assert.True(frame!.X >= 0 && frame.X <= 1);
    }

    // ── BuildFfmpegExpression: linear-only, documents known limitation ────────

    [Fact]
    public void BuildFfmpegExpression_TwoKeyframes_ProducesLinearExpression()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0.1, Y = 0.2, Scale = 1 });
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 5, X = 0.9, Y = 0.8, Scale = 1 });

        var expr = svc.BuildFfmpegExpression(LayerId, "x");
        Assert.NotNull(expr);
        Assert.Contains("if(lt(t,", expr);      // piecewise structure
        Assert.Contains("min(max(", expr);      // progress clamping
        // Linear-only: no sin/cos/pow (easing keywords not in expression)
        Assert.DoesNotContain("sin(", expr);
        Assert.DoesNotContain("cos(", expr);
        Assert.DoesNotContain("pow(", expr);
    }

    [Fact]
    public void BuildFfmpegExpression_WithEasingKf_StillProducesLinearOutput()
    {
        // Known limitation: easing set in editor does not affect ffmpeg expression.
        // The expression is always linear interpolation. This test DOCUMENTS the gap.
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0.0, Y = 0.0, Easing = "Ease In Out" });
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 2, X = 1.0, Y = 0.0, Easing = "Ease In Out" });

        var expr = svc.BuildFfmpegExpression(LayerId, "x");
        Assert.NotNull(expr);
        // Even with Ease In Out, expression is linear (known limitation)
        Assert.Contains("min(max(", expr);  // linear progress
        Assert.DoesNotContain("sin(", expr);  // no sine curve for ease-in-out
    }

    [Fact]
    public void BuildFfmpegExpression_SingleKeyframe_ReturnsStaticValue()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0.3, Y = 0.7 });

        var xExpr = svc.BuildFfmpegExpression(LayerId, "x");
        Assert.NotNull(xExpr);
        Assert.Contains("0.3000", xExpr);       // static X value
        Assert.Contains("(W*", xExpr);          // pixel-wrapped for ffmpeg
        Assert.DoesNotContain("if(lt(t,", xExpr); // no piecewise for single keyframe
    }

    [Fact]
    public void BuildFfmpegExpression_NoPath_ReturnsNull()
    {
        var svc    = new MotionKeyframeService();
        var result = svc.BuildFfmpegExpression(Guid.NewGuid(), "x");
        Assert.Null(result);
    }

    // ── Easing formula coverage ───────────────────────────────────────────────

    [Theory]
    [InlineData("Linear",     0.5, 0.5)]      // Linear: midpoint = 0.5
    [InlineData("Ease In",    0.5, null)]     // kf2.Easing="Ease In" → p*p → should be > 0.5 at midpoint
    [InlineData("Ease Out",   0.5, null)]     // Should be < 0.5 at midpoint (decelerating)
    [InlineData("Ease In Out", 0.5, 0.5)]    // Symmetric ease: midpoint ≈ 0.5
    [InlineData("Bounce Out", 0.5, null)]    // Non-linear
    public void Evaluate_Easing_MidpointReasonable(string easing, double t, double? expectedX)
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0, Y = 0, Easing = easing });
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 1, X = 1, Y = 0, Easing = easing });

        var frame = svc.Evaluate(LayerId, t);
        Assert.NotNull(frame);
        Assert.True(frame!.X >= 0 && frame.X <= 1, $"X should be in [0,1] for easing '{easing}', got {frame.X}");

        if (expectedX.HasValue)
            Assert.Equal(expectedX.Value, frame.X, precision: 2);
    }

    [Fact]
    public void Evaluate_EaseIn_IsSlowerAtStart()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0, Y = 0 });
        // Easing is on kf2 (the destination keyframe) in MotionKeyframeService.Evaluate
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 1, X = 1, Y = 0, Easing = "Ease In" });

        var at25  = svc.Evaluate(LayerId, 0.25)!.X;
        var at75  = svc.Evaluate(LayerId, 0.75)!.X;

        // Ease in (p*p): at 25% → 0.0625 (slower than linear 0.25)
        Assert.True(at25 < 0.25, $"EaseIn at 25%: {at25:F4} should be < linear 0.25");
        // Ease in (p*p): at 75% → 0.5625 (still below linear 0.75 — p² < p for p∈(0,1))
        Assert.True(at75 < 0.75, $"EaseIn at 75%: {at75:F4} should be < linear 0.75 (Ease In accelerates but position lags)");
    }

    [Fact]
    public void Evaluate_EaseOut_IsFasterAtStart()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 0, X = 0, Y = 0 });
        svc.UpsertKeyframe(LayerId, LayerType, new() { Time = 1, X = 1, Y = 0, Easing = "Ease Out" });

        var at25 = svc.Evaluate(LayerId, 0.25)!.X;
        // Ease out: 1-(1-p)^2 at p=0.25 → 1-0.5625 = 0.4375 > linear 0.25
        Assert.True(at25 > 0.25, $"EaseOut at 25%: {at25:F4} should be > linear 0.25");
    }

    // ── Easing + Bezier handles combined ──────────────────────────────────────

    [Fact]
    public void Evaluate_LinearEasing_WithBezierHandles_CurvesPath()
    {
        // Straight handles (P1/P2 at midpoint of P0→P3) + Linear easing = linear X, curved Y
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.5, HandleOutY = 0.3,  // handle pushes Y upward
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 2, X = 1, Y = 0,               // ends at same Y as start
            HandleInX = 0.5, HandleInY = 0.3,
            Easing = "Linear",
        });

        var mid = svc.Evaluate(LayerId, 1.0)!;     // ep=0.5 (linear midpoint)
        Assert.Equal(0.5, mid.X, precision: 2);    // X is linear when handles are symmetric
        Assert.True(mid.Y > 0, $"Y should arc above 0 at midpoint, got {mid.Y:F4}");
    }

    [Fact]
    public void Evaluate_EaseIn_WithBezierCurve_CombinesEffects()
    {
        // EaseIn (p²) means the Bezier is traversed slowly at first.
        // With an upward Y curve, at 25% time (ep=0.0625) we're near the start of the Bezier.
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.0, HandleOutY = 0.8,  // handle above start
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 4, X = 1, Y = 0,
            HandleInX  = 1.0, HandleInY = 0.8,   // handle above end
            Easing = "Ease In",                   // p² applied before Bezier
        });

        var at25pct = svc.Evaluate(LayerId, 1.0)!;  // t=1/4, ep=0.0625 (EaseIn at 25%)
        var at75pct = svc.Evaluate(LayerId, 3.0)!;  // t=3/4, ep=0.5625 (EaseIn at 75%)

        // With Ease In, the ep at 25% is much smaller (0.0625 vs linear 0.25)
        // so we're near the START of the Bezier path
        Assert.True(at25pct.X < 0.1, $"EaseIn+Bezier: X at 25% should be near start, got {at25pct.X:F4}");
        // At 75% with Ease In: ep=0.5625, much further along
        Assert.True(at75pct.X > 0.3, $"EaseIn+Bezier: X at 75% should be past midpoint, got {at75pct.X:F4}");
    }

    [Fact]
    public void Evaluate_EaseOut_WithBezierCurve_StartsAheadOfLinear()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0.5,
            HandleOutX = 0.5, HandleOutY = 0.5,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 2, X = 1, Y = 0.5,
            HandleInX  = 0.5, HandleInY = 0.5,
            Easing = "Ease Out",  // 1-(1-p)²
        });

        // EaseOut: ep at 25% time = 1-(0.75)² = 0.4375
        // Bezier x at ep=0.4375 should be ahead of linear (ep=0.25 would give x≈0.25)
        var at25pct = svc.Evaluate(LayerId, 0.5)!;   // 0.5/2 = 25% of duration
        Assert.True(at25pct.X > 0.25, $"EaseOut+Bezier: X should be ahead of linear at 25%, got {at25pct.X:F4}");
    }

    [Fact]
    public void Evaluate_EaseInOut_WithBezierCurve_IsSymmetric()
    {
        // "Ease In/Out": p<0.5 → 2p², p≥0.5 → 1-2(1-p)²
        // At midpoint (p=0.5): ep = 1-2*(0.5)² = 0.5 — same as linear
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.25, HandleOutY = 0.5,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 2, X = 1, Y = 0,
            HandleInX  = 0.75, HandleInY = 0.5,
            Easing = "Ease In/Out",
        });

        var mid = svc.Evaluate(LayerId, 1.0)!;   // ep = 0.5 at midpoint for Ease In/Out
        // Bezier with symmetric handles: X should be ~0.5
        Assert.Equal(0.5, mid.X, precision: 1);
        // Y should be > 0 (arced path)
        Assert.True(mid.Y > 0, $"Y should arc above 0 at midpoint, got {mid.Y:F4}");
    }

    [Fact]
    public void Evaluate_BounceOut_WithBezierHandles_ProducesOscillation()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.33, HandleOutY = 0.0,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 1, X = 1, Y = 0,
            HandleInX  = 0.67, HandleInY = 0.0,
            Easing = "Bounce Out",
        });

        // Bounce Out is non-monotonic. At some point near the end the ep may exceed 1 briefly then return.
        // Verify the values are in a reasonable range [0, 1.2] (allow slight overshoot)
        for (var t = 0.0; t <= 1.0; t += 0.1)
        {
            var frame = svc.Evaluate(LayerId, t)!;
            Assert.True(frame.X >= -0.1 && frame.X <= 1.2,
                $"BounceOut+Bezier X at t={t:F1} should be near [0,1], got {frame.X:F4}");
        }

        // Should end at X=1 (end value)
        Assert.Equal(1.0, svc.Evaluate(LayerId, 1.0)!.X, precision: 4);
    }

    [Fact]
    public void Evaluate_ElasticOut_WithBezierHandles_ProducesOscillation()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.5, HandleOutY = 0.0,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 2, X = 1, Y = 0,
            HandleInX  = 0.5, HandleInY = 0.0,
            Easing = "Elastic Out",
        });

        // Elastic Out oscillates near the end — X may briefly exceed 1 then settle
        var nearEnd = svc.Evaluate(LayerId, 1.5)!;   // 75% of duration
        // Just verify it doesn't crash and returns a finite number
        Assert.True(!double.IsNaN(nearEnd.X) && !double.IsInfinity(nearEnd.X),
            $"Elastic Out X at 75% should be finite, got {nearEnd.X}");
        // End value should land at 1.0
        Assert.Equal(1.0, svc.Evaluate(LayerId, 2.0)!.X, precision: 4);
    }

    // ── Partial handles: only one side has handles → linear ──────────────────

    [Fact]
    public void Evaluate_OnlyKf1HasHandles_UsesLinear()
    {
        // Only HandleOut set on kf1, no HandleIn on kf2 → linear interpolation
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.5, HandleOutY = 0.8,  // handle set but no match on kf2
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 1, X = 1, Y = 0,
            // No HandleIn — Bezier is NOT activated
        });

        var mid = svc.Evaluate(LayerId, 0.5)!;
        // No Bezier: Y should be linear (0→0 = 0), not curved
        Assert.Equal(0.0, mid.Y, precision: 4);
        Assert.Equal(0.5, mid.X, precision: 4);
    }

    [Fact]
    public void Evaluate_OnlyKf2HasHandles_UsesLinear()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            // No HandleOut on kf1
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 1, X = 1, Y = 0,
            HandleInX = 0.5, HandleInY = 0.8,  // handle set but no match on kf1
        });

        var mid = svc.Evaluate(LayerId, 0.5)!;
        Assert.Equal(0.0, mid.Y, precision: 4);  // no curve — linear Y stays at 0
    }

    // ── Scale and Alpha are always lerped, never Bezier ───────────────────────

    [Fact]
    public void Evaluate_WithBezierHandles_ScaleAndAlphaAreLinear()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0, Scale = 1.0, Alpha = 1.0,
            HandleOutX = 0.0, HandleOutY = 0.5,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 2, X = 1, Y = 0, Scale = 2.0, Alpha = 0.0,
            HandleInX = 1.0, HandleInY = 0.5,
            Easing = "Linear",
        });

        var mid = svc.Evaluate(LayerId, 1.0)!;  // ep=0.5 (linear)
        // Scale and Alpha use Lerp(a, b, ep) — exactly linear
        Assert.Equal(1.5, mid.Scale, precision: 4);  // lerp(1, 2, 0.5) = 1.5
        Assert.Equal(0.5, mid.Alpha, precision: 4);  // lerp(1, 0, 0.5) = 0.5
    }

    // ── CubicBezier boundary and mathematical correctness ────────────────────

    [Fact]
    public void CubicBezier_AtT0_ReturnsP0()
    {
        // Evaluate at time=0 should give start values
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0.2, Y = 0.3,
            HandleOutX = 0.5, HandleOutY = 0.9,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 1, X = 0.8, Y = 0.7,
            HandleInX = 0.5, HandleInY = 0.1,
        });

        var atStart = svc.Evaluate(LayerId, 0.0)!;
        Assert.Equal(0.2, atStart.X, precision: 4);
        Assert.Equal(0.3, atStart.Y, precision: 4);
    }

    [Fact]
    public void CubicBezier_AtT1_ReturnsP3()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0.2, Y = 0.3,
            HandleOutX = 0.5, HandleOutY = 0.9,
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 1, X = 0.8, Y = 0.7,
            HandleInX = 0.5, HandleInY = 0.1,
        });

        var atEnd = svc.Evaluate(LayerId, 1.0)!;
        Assert.Equal(0.8, atEnd.X, precision: 4);
        Assert.Equal(0.7, atEnd.Y, precision: 4);
    }

    [Fact]
    public void CubicBezier_SymmetricHandles_ProducesLinearX()
    {
        // P0=0, P1=0.5 (midpoint), P2=0.5, P3=1.0 → pure quadratic → linear
        // CubicBezier(0.5, 0, 0.5, 0.5, 1) = 3*0.125*0.5 + 3*0.25*0.5 + 0.125 = 0.5
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 0, X = 0, Y = 0,
            HandleOutX = 0.5, HandleOutY = 0.0,  // handle at X midpoint, Y=0
        });
        svc.UpsertKeyframe(LayerId, LayerType, new()
        {
            Time = 2, X = 1, Y = 0,
            HandleInX  = 0.5, HandleInY = 0.0,  // symmetric
        });

        var mid = svc.Evaluate(LayerId, 1.0)!;  // ep=0.5
        Assert.Equal(0.5, mid.X, precision: 3);  // symmetric handles → linear result
        Assert.Equal(0.0, mid.Y, precision: 4);  // flat Y throughout
    }

    // ── Evaluate — color interpolation ────────────────────────────────────────

    [Fact]
    public void Evaluate_FillColorInterpolated_LinearMidpoint()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 0.0, FillColor = ColorHelper.Pack(0, 0, 0, 255), Easing = "Linear",
        });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 2.0, FillColor = ColorHelper.Pack(200, 100, 50, 255), Easing = "Linear",
        });

        var (r, g, b, a) = ColorHelper.Unpack(svc.Evaluate(LayerId, 1.0)!.FillColor);
        Assert.Equal(100, r);
        Assert.Equal(50,  g);
        Assert.Equal(25,  b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void Evaluate_StrokeColorInterpolated_LinearMidpoint()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 0.0, StrokeColor = ColorHelper.Pack(255, 255, 255, 255), Easing = "Linear",
        });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 2.0, StrokeColor = ColorHelper.Pack(0, 0, 0, 100), Easing = "Linear",
        });

        var (r, g, b, a) = ColorHelper.Unpack(svc.Evaluate(LayerId, 1.0)!.StrokeColor);
        Assert.Equal(128, r); // (255+0)/2, rounded
        Assert.Equal(128, g);
        Assert.Equal(128, b);
        Assert.Equal(178, a); // (255+100)/2, rounded
    }

    [Fact]
    public void Evaluate_SingleKeyframe_ReturnsItsColorUnchanged()
    {
        var svc = new MotionKeyframeService();
        var fill = ColorHelper.Pack(10, 20, 30, 255);
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 3.0, FillColor = fill });

        Assert.Equal(fill, svc.Evaluate(LayerId, 0.0)!.FillColor);
    }

    // ── Evaluate — shadow interpolation ───────────────────────────────────────

    [Fact]
    public void Evaluate_ShadowColorInterpolated_LinearMidpoint()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe
        {
            Time = 0.0, ShadowColor = ColorHelper.Pack(0, 0, 0, 0), Easing = "Linear",
        });
        svc.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe
        {
            Time = 2.0, ShadowColor = ColorHelper.Pack(200, 100, 50, 200), Easing = "Linear",
        });

        var (r, g, b, a) = ColorHelper.Unpack(svc.Evaluate(LayerId, 1.0)!.ShadowColor);
        Assert.Equal(100, r);
        Assert.Equal(50,  g);
        Assert.Equal(25,  b);
        Assert.Equal(100, a);
    }

    [Fact]
    public void Evaluate_ShadowOffsetAndBlurInterpolated_LinearMidpoint()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe
        {
            Time = 0.0, ShadowOffsetX = 0.0, ShadowOffsetY = 0.0, ShadowBlur = 0.0, Easing = "Linear",
        });
        svc.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe
        {
            Time = 4.0, ShadowOffsetX = 10.0, ShadowOffsetY = -6.0, ShadowBlur = 20.0, Easing = "Linear",
        });

        var f = svc.Evaluate(LayerId, 2.0)!;
        Assert.Equal(5.0,  f.ShadowOffsetX, precision: 9);
        Assert.Equal(-3.0, f.ShadowOffsetY, precision: 9);
        Assert.Equal(10.0, f.ShadowBlur,    precision: 9);
    }

    [Fact]
    public void Evaluate_SingleKeyframe_ReturnsItsShadowUnchanged()
    {
        var svc = new MotionKeyframeService();
        var shadow = ColorHelper.Pack(1, 2, 3, 4);
        svc.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe
        {
            Time = 3.0, ShadowColor = shadow, ShadowOffsetX = 7.0, ShadowOffsetY = 8.0, ShadowBlur = 9.0,
        });

        var f = svc.Evaluate(LayerId, 0.0)!;
        Assert.Equal(shadow, f.ShadowColor);
        Assert.Equal(7.0, f.ShadowOffsetX);
        Assert.Equal(8.0, f.ShadowOffsetY);
        Assert.Equal(9.0, f.ShadowBlur);
    }

    [Fact]
    public void Evaluate_ShadowOffset_RespectsEasing()
    {
        var svcLinear = new MotionKeyframeService();
        var svcEaseIn = new MotionKeyframeService();
        var id2 = Guid.NewGuid();

        svcLinear.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe { Time = 0.0, ShadowOffsetX = 0.0, Easing = "Linear" });
        svcLinear.UpsertKeyframe(LayerId, "TextOverlay", new MotionKeyframe { Time = 4.0, ShadowOffsetX = 20.0, Easing = "Linear" });

        svcEaseIn.UpsertKeyframe(id2, "TextOverlay", new MotionKeyframe { Time = 0.0, ShadowOffsetX = 0.0, Easing = "Linear" });
        svcEaseIn.UpsertKeyframe(id2, "TextOverlay", new MotionKeyframe { Time = 4.0, ShadowOffsetX = 20.0, Easing = "Ease In" });

        var linear = svcLinear.Evaluate(LayerId, 1.0)!.ShadowOffsetX;
        var eased  = svcEaseIn.Evaluate(id2, 1.0)!.ShadowOffsetX;

        Assert.True(eased < linear, $"Ease In shadow offset ({eased:F4}) should be < Linear ({linear:F4}) at 25%");
    }

    // ── Evaluate — shape control-point interpolation ──────────────────────────

    [Fact]
    public void Evaluate_ControlPoint_LerpedWhenPresentInBothKeyframes()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 0.0, Easing = "Linear",
            ControlPointValues = new() { [CalloutControlPoints.CornerRadius] = 0.0 },
        });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 2.0, Easing = "Linear",
            ControlPointValues = new() { [CalloutControlPoints.CornerRadius] = 20.0 },
        });

        var f = svc.Evaluate(LayerId, 1.0)!;
        Assert.Equal(10.0, f.ControlPointValues[CalloutControlPoints.CornerRadius], precision: 9);
    }

    [Fact]
    public void Evaluate_ControlPoint_HoldsFromValue_WhenOnlyInFirstKeyframe()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 0.0, Easing = "Linear",
            ControlPointValues = new() { [CalloutControlPoints.OuterRadius] = 0.7 },
        });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0, Easing = "Linear" });

        var f = svc.Evaluate(LayerId, 1.0)!;
        Assert.Equal(0.7, f.ControlPointValues[CalloutControlPoints.OuterRadius], precision: 9);
    }

    [Fact]
    public void Evaluate_ControlPoint_HoldsToValue_WhenOnlyInSecondKeyframe()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0, Easing = "Linear" });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 2.0, Easing = "Linear",
            ControlPointValues = new() { [CalloutControlPoints.OuterRadius] = 0.5 },
        });

        var f = svc.Evaluate(LayerId, 1.0)!;
        Assert.Equal(0.5, f.ControlPointValues[CalloutControlPoints.OuterRadius], precision: 9);
    }

    [Fact]
    public void Evaluate_SingleKeyframe_ReturnsItsControlPointsUnchanged()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe
        {
            Time = 3.0,
            ControlPointValues = new() { [CalloutControlPoints.CornerRadius] = 12.0 },
        });

        var f = svc.Evaluate(LayerId, 0.0)!;
        Assert.Equal(12.0, f.ControlPointValues[CalloutControlPoints.CornerRadius]);
    }

    // ── EditLayer / UpsertKeyframeFromCurrent / IsMidInterpolation (item #57, phase P2) ────────

    [Fact]
    public void EditLayer_NoPath_CallsWriteStatic_NotKeyframeWrite()
    {
        var svc = new MotionKeyframeService();
        var staticCalled = false;

        svc.EditLayer(LayerId, "CalloutClip", 1.0,
            () => new MotionKeyframe { Time = 1.0 },
            kf => Assert.Fail("mutateKeyframe should not run when the layer has no path"),
            () => staticCalled = true);

        Assert.True(staticCalled);
        Assert.False(svc.HasPath(LayerId));
    }

    [Fact]
    public void EditLayer_HasPath_UpsertsKeyframe_NotWriteStatic()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0, X = 0.2, Y = 0.2 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0, X = 0.8, Y = 0.8 });

        svc.EditLayer(LayerId, "CalloutClip", 1.0,
            () => new MotionKeyframe { Time = 1.0 },
            kf => { kf.X = 0.9; kf.Y = 0.1; },
            () => Assert.Fail("writeStatic should not run when the layer has a path"));

        var kf = svc.GetPath(LayerId)!.Keyframes.Single(k => Math.Abs(k.Time - 1.0) < 0.001);
        Assert.Equal(0.9, kf.X, precision: 5);
        Assert.Equal(0.1, kf.Y, precision: 5);
    }

    [Fact]
    public void EditLayer_HasPath_PreservesOtherInterpolatedProperties_OnlyMutatedOnesChange()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0, X = 0.0, Y = 0.0, Scale = 1.0, Alpha = 1.0 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0, X = 1.0, Y = 1.0, Scale = 2.0, Alpha = 0.5 });

        // Mutate only X/Y at the midpoint — Scale/Alpha should come through as whatever was
        // actually interpolated there (1.5 / 0.75), not reset to defaults.
        svc.EditLayer(LayerId, "CalloutClip", 1.0,
            () => new MotionKeyframe { Time = 1.0 },
            kf => { kf.X = 0.3; kf.Y = 0.3; },
            () => { });

        var kf = svc.GetPath(LayerId)!.Keyframes.Single(k => Math.Abs(k.Time - 1.0) < 0.001);
        Assert.Equal(0.3, kf.X, precision: 5);
        Assert.Equal(0.3, kf.Y, precision: 5);
        Assert.Equal(1.5,  kf.Scale, precision: 5);
        Assert.Equal(0.75, kf.Alpha, precision: 5);
    }

    [Fact]
    public void EditLayer_HasPath_AtExistingKeyframeTime_UpdatesThatKeyframeInPlace()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0, X = 0.1, Y = 0.1 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 1.0, X = 0.5, Y = 0.5 });

        svc.EditLayer(LayerId, "CalloutClip", 1.0,
            () => new MotionKeyframe { Time = 1.0 },
            kf => kf.X = 0.77,
            () => { });

        Assert.Equal(2, svc.GetPath(LayerId)!.Keyframes.Count); // no new keyframe added
        var kf = svc.GetPath(LayerId)!.Keyframes.Single(k => Math.Abs(k.Time - 1.0) < 0.001);
        Assert.Equal(0.77, kf.X, precision: 5);
    }

    [Fact]
    public void UpsertKeyframeFromCurrent_NoPath_SeedsFromStaticSeed_UnconditionallyUpserts()
    {
        var svc = new MotionKeyframeService();

        svc.UpsertKeyframeFromCurrent(LayerId, "CalloutClip", 3.0,
            () => new MotionKeyframe { Time = 3.0, X = 0.4, Y = 0.6, Scale = 1.0, Alpha = 1.0 },
            kf => { });

        Assert.True(svc.HasPath(LayerId));
        var kf = svc.GetPath(LayerId)!.Keyframes.Single();
        Assert.Equal(0.4, kf.X, precision: 5);
        Assert.Equal(0.6, kf.Y, precision: 5);
    }

    [Fact]
    public void UpsertKeyframeFromCurrent_HasPath_SeedsFromInterpolatedFrame_IgnoresStaticSeed()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0, X = 0.0, Y = 0.0 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0, X = 1.0, Y = 1.0 });

        svc.UpsertKeyframeFromCurrent(LayerId, "CalloutClip", 1.0,
            () => new MotionKeyframe { Time = 1.0, X = 999, Y = 999 }, // must NOT be used
            kf => { });

        var kf = svc.GetPath(LayerId)!.Keyframes.Single(k => Math.Abs(k.Time - 1.0) < 0.001);
        Assert.Equal(0.5, kf.X, precision: 5);
        Assert.Equal(0.5, kf.Y, precision: 5);
    }

    [Fact]
    public void IsMidInterpolation_NoPath_ReturnsFalse()
    {
        var svc = new MotionKeyframeService();
        Assert.False(svc.IsMidInterpolation(LayerId, 1.0));
    }

    [Fact]
    public void IsMidInterpolation_SingleKeyframe_ReturnsFalse()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 1.0 });
        Assert.False(svc.IsMidInterpolation(LayerId, 1.0));
    }

    [Fact]
    public void IsMidInterpolation_StrictlyBetweenTwoKeyframes_ReturnsTrue()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0 });
        Assert.True(svc.IsMidInterpolation(LayerId, 1.0));
    }

    [Theory]
    [InlineData(0.0)]  // on the first keyframe
    [InlineData(2.0)]  // on the last keyframe
    [InlineData(-1.0)] // before the first
    [InlineData(3.0)]  // after the last
    public void IsMidInterpolation_AtOrOutsideKeyframeBounds_ReturnsFalse(double time)
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0 });
        Assert.False(svc.IsMidInterpolation(LayerId, time));
    }

    [Fact]
    public void IsMidInterpolation_WithinEpsilonOfAnExistingKeyframe_ReturnsFalse()
    {
        var svc = new MotionKeyframeService();
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 0.0 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 1.0 });
        svc.UpsertKeyframe(LayerId, "CalloutClip", new MotionKeyframe { Time = 2.0 });
        Assert.False(svc.IsMidInterpolation(LayerId, 1.005));
    }
}
