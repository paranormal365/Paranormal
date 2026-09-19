using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Telling a bad command apart from a dead engine, and both from a full one.
/// </summary>
/// <remarks>
/// Every failure used to be treated the same: the state went to Error and stayed there until
/// somebody pressed Initialize again. Nothing said that had happened, so after a crash the editor
/// went quiet — the preview stopped refreshing, exports refused to start, and the only clue was a
/// status chip most people never look at (2026-09-05 audit, F7).
/// </remarks>
public sealed class WorkerFailureClassifierTests
{
    // Messages below are the shapes ffmpeg.wasm and emscripten actually produce.

    [Theory]
    [InlineData("RuntimeError: memory access out of bounds")]
    [InlineData("RuntimeError: unreachable")]
    [InlineData("Aborted()")]
    [InlineData("abort(undefined)")]
    [InlineData("null function or function signature mismatch")]
    [InlineData("table index is out of bounds")]
    public void A_trapped_instance_is_a_crash(string message)
    {
        Assert.Equal(WorkerFailureKind.Crashed, WorkerFailureClassifier.Classify(message));
        Assert.True(WorkerFailureClassifier.NeedsReload(WorkerFailureKind.Crashed));
    }

    [Theory]
    [InlineData("Cannot enlarge memory arrays to size 2147483648 bytes")]
    [InlineData("Aborted(Cannot enlarge memory arrays)")]
    [InlineData("wasm memory allocation of 4294967296 bytes failed")]
    [InlineData("JS heap out of memory")]
    public void A_full_heap_is_told_apart_from_a_trap(string message)
    {
        Assert.Equal(WorkerFailureKind.OutOfMemory, WorkerFailureClassifier.Classify(message));
    }

    /// <summary>
    /// The distinction has to survive the wrapping, because a full heap usually arrives inside an
    /// abort — and it is the memory half that decides what the person is told.
    /// </summary>
    [Fact]
    public void A_memory_abort_reads_as_memory_not_as_a_trap()
    {
        var message = "Aborted(). Build with -sASSERTIONS. Cannot enlarge memory arrays.";

        Assert.Equal(WorkerFailureKind.OutOfMemory, WorkerFailureClassifier.Classify(message));
    }

    [Theory]
    [InlineData("ffmpeg exited with code 1. Recent log: Invalid argument")]
    [InlineData("No such file or directory")]
    [InlineData("")]
    [InlineData(null)]
    public void An_ordinary_failure_leaves_the_engine_alone(string? message)
    {
        Assert.Equal(WorkerFailureKind.Recoverable, WorkerFailureClassifier.Classify(message));
        Assert.False(WorkerFailureClassifier.NeedsReload(WorkerFailureKind.Recoverable));
    }

    /// <summary>
    /// Restarting will not help a file that is simply too large, so the message points at the
    /// helper that runs outside the browser's limits instead of promising a retry.
    /// </summary>
    [Fact]
    public void Running_out_of_memory_points_at_the_helper_when_there_is_one()
    {
        var withHelper = WorkerFailureClassifier.Explain(WorkerFailureKind.OutOfMemory, sidecarAvailable: true);
        var without    = WorkerFailureClassifier.Explain(WorkerFailureKind.OutOfMemory, sidecarAvailable: false);

        Assert.Contains("native helper", withHelper);
        Assert.DoesNotContain("native helper", without);
        Assert.Contains("shorter", without);
    }

    [Fact]
    public void A_crash_says_the_project_is_safe()
    {
        Assert.Contains("project is untouched",
            WorkerFailureClassifier.Explain(WorkerFailureKind.Crashed, sidecarAvailable: false));
    }
}

/// <summary>
/// When the editor restarts the engine by itself, and when it must not.
/// </summary>
public sealed class FfmpegCrashRecoveryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_first_crash_is_restarted_at_once()
    {
        Assert.True(FfmpegCrashRecoveryPolicy.ShouldRestart(WorkerFailureKind.Crashed, null, Now));
    }

    /// <summary>
    /// Whatever crashed the engine is usually still on the timeline, so an automatic restart
    /// followed by an automatic re-render crashes it again. A tight loop is worse than staying
    /// broken: it burns the machine and buries the one message that would explain what happened.
    /// </summary>
    [Fact]
    public void A_second_crash_within_the_minute_is_not()
    {
        var justNow = Now.AddSeconds(-5);

        Assert.False(FfmpegCrashRecoveryPolicy.ShouldRestart(WorkerFailureKind.Crashed, justNow, Now));
    }

    [Fact]
    public void A_crash_after_the_interval_is_restarted_again()
    {
        var aWhileBack = Now.AddSeconds(-FfmpegCrashRecoveryPolicy.MinimumSecondsBetweenAttempts);

        Assert.True(FfmpegCrashRecoveryPolicy.ShouldRestart(WorkerFailureKind.Crashed, aWhileBack, Now));
    }

    /// <summary>
    /// A fresh engine has exactly as much memory as the one that just filled up.
    /// </summary>
    [Theory]
    [InlineData(WorkerFailureKind.OutOfMemory)]
    [InlineData(WorkerFailureKind.Recoverable)]
    public void Only_a_crash_is_worth_restarting_for(WorkerFailureKind kind)
    {
        Assert.False(FfmpegCrashRecoveryPolicy.ShouldRestart(kind, null, Now));
    }

    /// <summary>
    /// Declining silently is the failure this replaces: the second crash in a minute would restart
    /// nothing and say nothing, so the editor looked dead for no stated reason.
    /// </summary>
    [Fact]
    public void Declining_says_how_long_and_why()
    {
        var message = FfmpegCrashRecoveryPolicy.DeclinedMessage(Now.AddSeconds(-20), Now);

        Assert.Contains("40s", message);
        Assert.Contains("timeline", message);
    }
}

/// <summary>
/// A stuck engine is visible to everyone, and offers a way out.
/// </summary>
/// <remarks>
/// The wedge showed only on the diagnostics chip, which is an operator tool the host switches on
/// per user. For everybody else a stuck engine looked exactly like a slow one, indefinitely, and
/// no control anywhere offered to restart it (2026-09-05 audit, F7).
/// </remarks>
public sealed class WedgedStatusPresentationTests
{
    [Fact]
    public void A_wedge_says_so_whatever_state_it_interrupted()
    {
        var label = FfmpegStatusPresentation.Label(
            FfmpegState.Processing, isWorkerBusy: true, progressPercent: 40,
            downloadLabel: null, lastError: null, isWedged: true);

        Assert.Equal("Stuck — reset it", label);
    }

    [Fact]
    public void A_wedge_does_not_look_like_progress()
    {
        Assert.Equal("wedged", FfmpegStatusPresentation.CssModifier(
            FfmpegState.Processing, isWorkerBusy: true, isWedged: true));

        Assert.Equal("busy", FfmpegStatusPresentation.CssModifier(
            FfmpegState.Processing, isWorkerBusy: true, isWedged: false));
    }

    [Fact]
    public void A_wedge_explains_that_restarting_is_safe()
    {
        var tooltip = FfmpegStatusPresentation.Tooltip(
            FfmpegState.Processing, isWorkerBusy: true, progressPercent: 40,
            downloadLabel: null, lastError: null, isWedged: true);

        Assert.Contains("project is untouched", tooltip);
    }

    [Theory]
    [InlineData(FfmpegState.Ready,      false, false)]
    [InlineData(FfmpegState.Processing, false, false)]
    [InlineData(FfmpegState.Processing, true,  true)]
    [InlineData(FfmpegState.Error,      false, true)]
    public void The_restart_control_is_offered_exactly_when_it_is_needed(
        FfmpegState state, bool wedged, bool expected)
    {
        Assert.Equal(expected, FfmpegStatusPresentation.ShouldOfferReset(state, wedged));
    }

    /// <summary>
    /// Nothing about the ordinary states changed.
    /// </summary>
    [Fact]
    public void An_engine_that_is_fine_still_reads_as_fine()
    {
        Assert.Equal("Ready", FfmpegStatusPresentation.Label(
            FfmpegState.Ready, false, 0, null, null));
    }
}
