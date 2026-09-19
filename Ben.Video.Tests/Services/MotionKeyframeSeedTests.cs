using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Adding or removing a keyframe leaves the rest of the animation alone.
/// </summary>
public sealed class MotionKeyframeSeedTests
{
    private static MotionKeyframe At(double time, double scaleX, double scaleY, double? rotation) => new()
    {
        Time = time, X = 0.5, Y = 0.5, Scale = 1.0, Alpha = 1.0,
        ScaleX = scaleX, ScaleY = scaleY, Rotation = rotation,
    };

    /// <summary>
    /// A keyframe added part-way through an animation keeps what was already happening.
    /// </summary>
    /// <remarks>
    /// The seed dropped per-axis scale and rotation, so adding a keyframe mid-animation said
    /// "uniform, upright" because nothing had told it otherwise — and the layer stopped stretching
    /// and turning from that point on (2026-09-05 audit, motion-4).
    /// </remarks>
    [Fact]
    public void A_keyframe_added_mid_animation_keeps_the_stretch_and_the_turn()
    {
        var motion = new MotionKeyframeService();
        var id     = Guid.NewGuid();

        motion.UpsertKeyframe(id, "ClipArtClip", At(0.0, 1.0, 1.0, 0));
        motion.UpsertKeyframe(id, "ClipArtClip", At(2.0, 3.0, 0.5, 90));

        motion.UpsertKeyframeFromCurrent(id, "ClipArtClip", 1.0,
            staticSeed: () => At(1.0, 1.0, 1.0, null),
            mutateKeyframe: _ => { });

        var added = motion.GetPath(id)!.Keyframes.Single(k => Math.Abs(k.Time - 1.0) < 0.001);

        Assert.Equal(2.0, added.ScaleX!.Value, precision: 3);
        Assert.Equal(0.75, added.ScaleY!.Value, precision: 3);
        Assert.Equal(45.0, added.Rotation!.Value, precision: 3);
    }

    /// <summary>
    /// Removing a keyframe removes the one nearest the time asked for.
    /// </summary>
    /// <remarks>
    /// It removed the first one within reach instead, so with two keyframes closer together than
    /// the tolerance — ordinary on a short animation — asking to remove the second removed the
    /// first (2026-09-05 audit, motion-2).
    /// </remarks>
    [Fact]
    public void Removing_a_keyframe_removes_the_nearest_one()
    {
        var motion = new MotionKeyframeService();
        var id     = Guid.NewGuid();

        motion.UpsertKeyframe(id, "ClipArtClip", At(1.00, 1.0, 1.0, null));
        motion.UpsertKeyframe(id, "ClipArtClip", At(1.06, 2.0, 2.0, null));

        motion.RemoveKeyframe(id, 1.06);

        var left = Assert.Single(motion.GetPath(id)!.Keyframes);
        Assert.Equal(1.00, left.Time, precision: 3);
    }

    [Fact]
    public void A_time_with_no_keyframe_near_it_removes_nothing()
    {
        var motion = new MotionKeyframeService();
        var id     = Guid.NewGuid();

        motion.UpsertKeyframe(id, "ClipArtClip", At(1.0, 1.0, 1.0, null));
        motion.RemoveKeyframe(id, 5.0);

        Assert.Single(motion.GetPath(id)!.Keyframes);
    }
}
