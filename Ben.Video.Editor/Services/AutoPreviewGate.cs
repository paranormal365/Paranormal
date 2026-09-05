using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>What the editor should do about a pending Working Window rebuild.</summary>
public enum AutoPreviewDecision
{
    /// <summary>Rebuild it now.</summary>
    Run,

    /// <summary>Come back later — the engine is not free yet.</summary>
    Wait,

    /// <summary>Give up on this one. Something else will ask again.</summary>
    Abandon,
}

/// <summary>
/// Decides whether now is the moment to rebuild the Working Window.
/// </summary>
/// <remarks>
/// <para>The rebuild is debounced but otherwise unconditional, so it would fire in the middle of an
/// export and take its turn on the same engine — inserting its own encodes between the export's,
/// which makes a long render longer for a preview nobody is watching while they wait for the file
/// (2026-09-05 audit, preview-20).</para>
///
/// <para>An engine that has stopped is the other half. Waiting on it is pointless, and the loop
/// that used to do the waiting had no bound at all: with the worker in Error and no further edit to
/// supersede it, it polled every quarter second forever, for nothing.</para>
///
/// <para>Pure, so "when does the preview rebuild" can be checked without an engine, an export or a
/// clock.</para>
/// </remarks>
public static class AutoPreviewGate
{
    /// <summary>How long to keep waiting for a busy engine before giving up on this rebuild.</summary>
    public const int MaximumWaitMs = 60_000;

    /// <summary>
    /// Whether to rebuild now.
    /// </summary>
    /// <param name="state">What the engine is doing.</param>
    /// <param name="exportRunning">Whether an export the person asked for is under way.</param>
    /// <param name="waitedMs">How long this rebuild has already been waiting.</param>
    public static AutoPreviewDecision Decide(FfmpegState state, bool exportRunning, int waitedMs)
    {
        // An export is work somebody asked for and is waiting on. A preview refresh is not, so it
        // yields — and it yields for as long as the export takes rather than counting against the
        // wait budget, because "the export is still going" is not a stall.
        if (exportRunning) return AutoPreviewDecision.Wait;

        // A stopped engine will not run this, and something has to restart it first. The crash
        // handler reschedules once it has.
        if (state == FfmpegState.Error) return AutoPreviewDecision.Abandon;

        if (state == FfmpegState.Ready) return AutoPreviewDecision.Run;

        return waitedMs >= MaximumWaitMs ? AutoPreviewDecision.Abandon : AutoPreviewDecision.Wait;
    }
}
