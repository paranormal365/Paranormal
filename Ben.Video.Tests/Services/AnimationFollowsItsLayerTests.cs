using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A layer's animation belongs to the layer, and goes where it goes.
/// </summary>
/// <remarks>
/// Keyframes are stored in project seconds and nothing connected them to the layer they animate,
/// so dragging a callout two seconds later left its movement exactly where it was — playing over
/// whatever happened to be there instead. Removing the layer left the path behind, and the orphan
/// was then written into the project file (2026-09-05 audit, motion-3 and motion-18).
/// </remarks>
public sealed class AnimationFollowsItsLayerTests
{
    private static (ClipStore Clips, MotionKeyframeService Motion) Editor()
    {
        var clips  = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true }));
        var motion = new MotionKeyframeService();

        // The join the editor component makes.
        clips.ItemTimeShifted += motion.ShiftKeyframes;
        clips.ItemRemoved     += motion.RemovePath;

        return (clips, motion);
    }

    private static CalloutClip AnimatedCallout(ClipStore clips, MotionKeyframeService motion, double at)
    {
        var callout = new CalloutClip { Name = "callout", TimelinePosition = at, Duration = 5 };
        clips.AddCallout(callout);

        motion.UpsertKeyframe(callout.Id, "Callout", new MotionKeyframe { Time = at,       X = 0.1, Y = 0.1 });
        motion.UpsertKeyframe(callout.Id, "Callout", new MotionKeyframe { Time = at + 4,   X = 0.9, Y = 0.9 });

        return callout;
    }

    [Fact]
    public void Dragging_a_layer_takes_its_keyframes_along()
    {
        var (clips, motion) = Editor();
        var callout = AnimatedCallout(clips, motion, at: 2);

        callout.TimelinePosition = 12;
        clips.CommitDraggedPosition(callout.Id, 2);

        var times = motion.GetPath(callout.Id)!.Keyframes.Select(k => k.Time).ToList();
        Assert.Equal(new[] { 12.0, 16.0 }, times);
    }

    [Fact]
    public void A_layer_pushed_along_to_make_room_takes_them_too()
    {
        var (clips, motion) = Editor();
        var track = clips.Tracks[0];
        clips.AddClipToTrack(track.Id, new VideoClip { Name = "under", Duration = 10 });

        var callout = AnimatedCallout(clips, motion, at: 2);
        var before  = motion.GetPath(callout.Id)!.Keyframes.Select(k => k.Time).ToList();

        // Nothing moved the callout, so nothing moved its animation.
        Assert.Equal(before, motion.GetPath(callout.Id)!.Keyframes.Select(k => k.Time));
    }

    [Fact]
    public void Removing_a_layer_removes_its_animation()
    {
        var (clips, motion) = Editor();
        var callout = AnimatedCallout(clips, motion, at: 2);
        Assert.True(motion.HasPath(callout.Id));

        clips.RemoveCallout(callout.Id);

        Assert.False(motion.HasPath(callout.Id));
    }

    [Fact]
    public void Keyframes_never_slide_before_the_beginning()
    {
        var (clips, motion) = Editor();
        var callout = AnimatedCallout(clips, motion, at: 2);

        motion.ShiftKeyframes(callout.Id, -30);

        Assert.All(motion.GetPath(callout.Id)!.Keyframes, k => Assert.True(k.Time >= 0));
    }

    [Fact]
    public void A_shift_of_nothing_changes_nothing()
    {
        var (clips, motion) = Editor();
        var callout = AnimatedCallout(clips, motion, at: 2);
        var raised = 0;
        motion.OnChanged += () => raised++;

        motion.ShiftKeyframes(callout.Id, 0);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Shifting_a_layer_with_no_animation_is_harmless()
    {
        var (clips, motion) = Editor();

        motion.ShiftKeyframes(Guid.NewGuid(), 5);

        Assert.Empty(motion.AllPaths);
    }
}
