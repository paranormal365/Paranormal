using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Pure decision logic for the toolbar's ffmpeg status badge — item #71. Extracted so the actual
/// bug fix here (see <see cref="IsBusyButNotProcessing"/>) is testable independent of the
/// Blazor component, matching item #67's <see cref="FfmpegBusyPolicy"/> precedent.
///
/// <para>The crux: <see cref="FfmpegService.IsWorkerBusy"/> (built phase 142) is accurate for
/// every worker-touching call, including lighter ones — <c>GetMetadataAsync</c>/
/// <c>WriteFileAsync</c>/<c>ExtractThumbnailsAsync</c>, all heavily used during import — that hold
/// the worker lock the same as a full <c>exec</c> but never set
/// <see cref="FfmpegState.Processing"/>. Before this fix the badge simply never read
/// <see cref="FfmpegService.IsWorkerBusy"/> at all, so it showed "Ready" the entire time one of
/// these calls was genuinely blocking the worker — not a low-contrast complaint, an incorrect
/// one.</para>
/// </summary>
public static class FfmpegStatusPresentation
{
    /// <summary>True when the worker is genuinely busy but <see cref="FfmpegState"/> alone
    /// wouldn't show it — a lighter call in flight while State is still Ready.</summary>
    public static bool IsBusyButNotProcessing(FfmpegState state, bool isWorkerBusy) =>
        state == FfmpegState.Ready && isWorkerBusy;

    /// <summary>
    /// The label everyone sees, wedge included.
    /// </summary>
    /// <param name="isWedged">
    /// Whether the watchdog has flagged the in-flight command as stuck. A wedge used to show only
    /// on the diagnostics chip, which is an operator tool the host switches on per user — so for
    /// everybody else a stuck engine looked like a slow one, indefinitely, with nothing offering a
    /// way out (2026-09-05 audit, F7).
    /// </param>
    public static string Label(
        FfmpegState state, bool isWorkerBusy, int progressPercent, string? downloadLabel,
        string? lastError, bool isWedged = false)
    {
        if (isWedged) return "Stuck — reset it";

        return state switch
        {
            FfmpegState.Idle        => "Not loaded",
            FfmpegState.LoadingCore => downloadLabel ?? "Loading ffmpeg…",
            FfmpegState.Ready       => IsBusyButNotProcessing(state, isWorkerBusy) ? "Busy…" : "Ready",
            FfmpegState.Processing  => $"Processing… {progressPercent}%",
            FfmpegState.Error       => $"Error: {lastError}",
            _                       => string.Empty,
        };
    }

    /// <summary>
    /// Whether to offer the person a way to restart the engine.
    /// </summary>
    /// <remarks>
    /// A wedge never clears itself, and an engine in Error will not run another command, so in both
    /// cases the editor stays stuck until something restarts it. Offering that in the toolbar is
    /// the difference between "the editor is broken" and "press this".
    /// </remarks>
    public static bool ShouldOfferReset(FfmpegState state, bool isWedged) =>
        isWedged || state == FfmpegState.Error;

    /// <summary>The badge's CSS state modifier — <c>bv-status--&lt;this&gt;</c>. Both busy shapes
    /// (a real Processing exec and <see cref="IsBusyButNotProcessing"/>) deliberately collapse to
    /// the same modifier ("busy") so they get identical visual treatment: fixing the correctness
    /// gap without unifying the styling would just have produced two different-looking badges that
    /// both mean "not available right now", defeating the "unambiguous at a glance" goal.</summary>
    public static string CssModifier(FfmpegState state, bool isWorkerBusy, bool isWedged = false)
    {
        if (isWedged) return "wedged";

        return IsBusyButNotProcessing(state, isWorkerBusy) || state == FfmpegState.Processing
            ? "busy"
            : state.ToString().ToLowerInvariant();
    }

    public static string Tooltip(
        FfmpegState state, bool isWorkerBusy, int progressPercent, string? downloadLabel,
        string? lastError, bool isWedged = false)
    {
        if (isWedged)
            return "The video engine has not responded for a while. Restarting it is safe — your "
                 + "project is untouched, and only the step it was in the middle of is lost.";

        return IsBusyButNotProcessing(state, isWorkerBusy)
            ? "ffmpeg is busy with a lighter operation (e.g. import) — it doesn't report a percent, but it's not available for a new command right now."
            : Label(state, isWorkerBusy, progressPercent, downloadLabel, lastError);
    }
}
