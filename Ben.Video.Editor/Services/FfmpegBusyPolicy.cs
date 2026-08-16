using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #67 fix — pure decision logic for whether a Server-tab import should wait out an ffmpeg
/// worker that's merely <see cref="FfmpegState.Processing"/>, versus failing immediately.
///
/// <para>Before this fix, <c>ClipBrowser</c> hard-rejected any import attempt whenever
/// <c>Ffmpeg.State != FfmpegState.Ready</c> — including <see cref="FfmpegState.Processing"/>, a
/// perfectly healthy (if slow) state, with a message that wrongly implied the worker needed
/// re-initializing. Live-confirmed during the phase 141-147 flakiness investigation: a two-clip
/// <c>concatClips</c> auto-preview re-render alone can legitimately take 44+ seconds.</para>
///
/// <para>The crux of the fix is this method returning <c>null</c> (no immediate failure — the
/// caller should wait) for <see cref="FfmpegState.Processing"/> as well as
/// <see cref="FfmpegState.Ready"/>, unlike the old blanket rejection. Only genuinely-not-going-to-
/// resolve-itself states (<see cref="FfmpegState.Idle"/>, <see cref="FfmpegState.LoadingCore"/>,
/// <see cref="FfmpegState.Error"/>) get an immediate, state-specific message.</para>
/// </summary>
public static class FfmpegBusyPolicy
{
    public const string NotInitializedMessage = "Click Initialize in the toolbar before importing.";
    public const string ErrorMessage = "ffmpeg hit an error — check the diagnostics panel, then try again.";
    public const string WedgedMessage = "ffmpeg appears stuck — use Reset in the diagnostics panel, then try again.";
    public const string TimedOutWaitingMessage = "ffmpeg is still busy — try again in a moment.";

    /// <summary>
    /// Returns the message to fail immediately with, or <c>null</c> when the caller should
    /// instead wait for <see cref="FfmpegState.Processing"/> to clear (or is already
    /// <see cref="FfmpegState.Ready"/>).
    /// </summary>
    public static string? ImmediateFailureMessage(FfmpegState state) => state switch
    {
        FfmpegState.Idle or FfmpegState.LoadingCore => NotInitializedMessage,
        FfmpegState.Error => ErrorMessage,
        _ => null, // Ready or Processing — never fail immediately
    };
}
