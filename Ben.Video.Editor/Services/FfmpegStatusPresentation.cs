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

    public static string Label(FfmpegState state, bool isWorkerBusy, int progressPercent, string? downloadLabel, string? lastError) =>
        state switch
        {
            FfmpegState.Idle        => "Not loaded",
            FfmpegState.LoadingCore => downloadLabel ?? "Loading ffmpeg…",
            FfmpegState.Ready       => IsBusyButNotProcessing(state, isWorkerBusy) ? "Busy…" : "Ready",
            FfmpegState.Processing  => $"Processing… {progressPercent}%",
            FfmpegState.Error       => $"Error: {lastError}",
            _                       => string.Empty,
        };

    /// <summary>The badge's CSS state modifier — <c>bv-status--&lt;this&gt;</c>. Both busy shapes
    /// (a real Processing exec and <see cref="IsBusyButNotProcessing"/>) deliberately collapse to
    /// the same modifier ("busy") so they get identical visual treatment: fixing the correctness
    /// gap without unifying the styling would just have produced two different-looking badges that
    /// both mean "not available right now", defeating the "unambiguous at a glance" goal.</summary>
    public static string CssModifier(FfmpegState state, bool isWorkerBusy) =>
        IsBusyButNotProcessing(state, isWorkerBusy) || state == FfmpegState.Processing
            ? "busy"
            : state.ToString().ToLowerInvariant();

    public static string Tooltip(FfmpegState state, bool isWorkerBusy, int progressPercent, string? downloadLabel, string? lastError) =>
        IsBusyButNotProcessing(state, isWorkerBusy)
            ? "ffmpeg is busy with a lighter operation (e.g. import) — it doesn't report a percent, but it's not available for a new command right now."
            : Label(state, isWorkerBusy, progressPercent, downloadLabel, lastError);
}
