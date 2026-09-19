using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// When the Working Window rebuilds itself, and when it gets out of the way.
/// </summary>
public sealed class AutoPreviewGateTests
{
    [Fact]
    public void A_free_engine_rebuilds_at_once()
    {
        Assert.Equal(AutoPreviewDecision.Run,
            AutoPreviewGate.Decide(FfmpegState.Ready, exportRunning: false, waitedMs: 0));
    }

    /// <summary>
    /// An export is work somebody asked for and is waiting on; a preview refresh is not.
    /// </summary>
    /// <remarks>
    /// The rebuild was debounced but otherwise unconditional, so it took its turn on the same
    /// engine mid-render — interleaving its own encodes with the export's and making a render
    /// somebody was waiting on take longer, for a preview they were not watching (2026-09-05
    /// audit, preview-20).
    /// </remarks>
    [Fact]
    public void An_export_takes_precedence_over_a_refresh()
    {
        Assert.Equal(AutoPreviewDecision.Wait,
            AutoPreviewGate.Decide(FfmpegState.Ready, exportRunning: true, waitedMs: 0));
    }

    /// <summary>
    /// An export can legitimately run for many minutes, so waiting for one must not count against
    /// the give-up budget — otherwise a long render would silently cancel the refresh that follows it.
    /// </summary>
    [Fact]
    public void A_long_export_never_exhausts_the_waiting_budget()
    {
        Assert.Equal(AutoPreviewDecision.Wait,
            AutoPreviewGate.Decide(FfmpegState.Ready, exportRunning: true,
                                   waitedMs: AutoPreviewGate.MaximumWaitMs * 10));
    }

    [Fact]
    public void A_busy_engine_is_waited_for()
    {
        Assert.Equal(AutoPreviewDecision.Wait,
            AutoPreviewGate.Decide(FfmpegState.Processing, exportRunning: false, waitedMs: 0));
    }

    /// <summary>
    /// Waiting has a bound. Before there was one, a worker left in Error with no further edit to
    /// supersede the pending refresh polled every quarter second forever, for nothing.
    /// </summary>
    [Fact]
    public void A_refresh_gives_up_rather_than_polling_forever()
    {
        Assert.Equal(AutoPreviewDecision.Abandon,
            AutoPreviewGate.Decide(FfmpegState.Processing, exportRunning: false,
                                   waitedMs: AutoPreviewGate.MaximumWaitMs));
    }

    /// <summary>
    /// A stopped engine will not run this, and something has to restart it first. The crash
    /// handler asks again once it has.
    /// </summary>
    [Fact]
    public void A_stopped_engine_is_not_waited_for_at_all()
    {
        Assert.Equal(AutoPreviewDecision.Abandon,
            AutoPreviewGate.Decide(FfmpegState.Error, exportRunning: false, waitedMs: 0));
    }

    [Fact]
    public void An_engine_that_has_not_loaded_yet_is_waited_for()
    {
        Assert.Equal(AutoPreviewDecision.Wait,
            AutoPreviewGate.Decide(FfmpegState.LoadingCore, exportRunning: false, waitedMs: 0));
    }
}

/// <summary>
/// What goes into the audio mix, trimmed and positioned.
/// </summary>
/// <remarks>
/// The arithmetic used to live inside the export, which is why the Working Window had no sound at
/// all: a separate audio track was silent while you edited, and the only way to hear whether the
/// music sat right against the picture was the full-quality Preview, which re-renders the whole
/// timeline (2026-09-05 audit, audio-6).
/// </remarks>
public sealed class AudioMixPlannerTests
{
    private static AudioClip Clip(
        string source = "a.mp3", double position = 0, double duration = 30,
        double startTrim = 0, double endTrim = 0) =>
        new()
        {
            Name = "audio", MemFsName = source, Duration = duration,
            TimelinePosition = position, StartTrim = startTrim, EndTrim = endTrim,
        };

    [Fact]
    public void A_clips_trims_decide_what_is_rendered()
    {
        var plan = AudioMixPlanner.Plan([Clip(startTrim: 12, endTrim: 20)]);

        Assert.Single(plan);
        Assert.Equal(12, plan[0].Start);
        Assert.Equal(20, plan[0].End);
    }

    [Fact]
    public void An_untrimmed_clip_runs_its_whole_length()
    {
        var plan = AudioMixPlanner.Plan([Clip(duration: 186)]);

        Assert.Equal(0, plan[0].Start);
        Assert.Equal(186, plan[0].End);
    }

    /// <summary>
    /// The delay is part of the clip's own filter rather than an offset for the mix to apply,
    /// which is what lets amix combine the segments with no position arithmetic of its own.
    /// </summary>
    [Fact]
    public void A_clips_position_on_the_timeline_becomes_a_delay()
    {
        var plan = AudioMixPlanner.Plan([Clip(position: 2.5)]);

        Assert.Contains("adelay=2500:all=1", plan[0].Filter);
    }

    [Fact]
    public void A_clip_at_the_beginning_gets_no_delay()
    {
        var plan = AudioMixPlanner.Plan([Clip(position: 0)]);

        Assert.DoesNotContain("adelay", plan[0].Filter);
    }

    /// <summary>
    /// Asking ffmpeg for a zero-length segment fails the whole mix rather than quietly producing
    /// silence, so a clip trimmed to nothing is left out.
    /// </summary>
    [Fact]
    public void A_clip_trimmed_to_nothing_is_left_out()
    {
        // Trimmed from its very end: nothing is left to render.
        var plan = AudioMixPlanner.Plan([Clip(duration: 30, startTrim: 30)]);

        Assert.Empty(plan);
    }

    /// <summary>
    /// An end trim of zero means "no end trim", not "trim to zero" — the clip's full length is
    /// what is left. Reading it the other way would silently drop every untrimmed clip.
    /// </summary>
    [Fact]
    public void An_end_trim_of_zero_means_the_whole_clip()
    {
        var plan = AudioMixPlanner.Plan([Clip(duration: 30, startTrim: 10, endTrim: 0)]);

        Assert.Equal(10, plan[0].Start);
        Assert.Equal(30, plan[0].End);
    }

    [Fact]
    public void A_clip_with_no_loaded_media_is_left_out()
    {
        var clip = Clip();
        clip.MemFsName = null;

        Assert.Empty(AudioMixPlanner.Plan([clip]));
    }

    [Fact]
    public void The_order_given_is_the_order_planned()
    {
        var plan = AudioMixPlanner.Plan([Clip("first.mp3"), Clip("second.mp3")]);

        Assert.Equal(["first.mp3", "second.mp3"], plan.Select(p => p.Source));
    }
}
