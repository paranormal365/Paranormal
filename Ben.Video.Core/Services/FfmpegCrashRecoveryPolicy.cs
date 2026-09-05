namespace Ben.Video.Editor.Services;

/// <summary>
/// Decides whether the editor should restart the video engine by itself after it crashed.
/// </summary>
/// <remarks>
/// <para>A trapped engine left the editor quietly broken: the preview stopped refreshing, every
/// export refused to start, and the only way back was for somebody to notice the status chip and
/// press Initialize. Restarting is something the editor can do without being asked (2026-09-05
/// audit, F7).</para>
///
/// <para>What it must not do is retry in a loop. If whatever crashed the engine is still on the
/// timeline, an automatic restart followed by an automatic re-render crashes it again immediately,
/// and a tight loop is worse than staying broken — it burns the machine and buries the one message
/// that would explain what happened. So: one restart per minute, and running out of memory is
/// never worth restarting for, because a fresh engine has exactly as much memory as the one that
/// just filled up.</para>
///
/// <para>Pure and clock-injected, so the interval is testable without waiting a minute.</para>
/// </remarks>
public static class FfmpegCrashRecoveryPolicy
{
    /// <summary>The shortest gap between two automatic restarts.</summary>
    public const int MinimumSecondsBetweenAttempts = 60;

    /// <summary>
    /// Whether to restart the engine now.
    /// </summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="lastAttempt">When the last automatic restart happened, or null for never.</param>
    /// <param name="now">The current time.</param>
    public static bool ShouldRestart(
        WorkerFailureKind kind, DateTimeOffset? lastAttempt, DateTimeOffset now)
    {
        // A bad command did not break anything, and a fresh engine has no more memory than the
        // one that just ran out.
        if (kind != WorkerFailureKind.Crashed) return false;

        if (lastAttempt is not { } last) return true;

        return (now - last).TotalSeconds >= MinimumSecondsBetweenAttempts;
    }

    /// <summary>
    /// What to say when a restart is declined because one just happened.
    /// </summary>
    /// <remarks>
    /// Silence here is the failure mode this replaces: the second crash in a minute would restart
    /// nothing and say nothing, so the editor looked dead for no stated reason.
    /// </remarks>
    public static string DeclinedMessage(DateTimeOffset? lastAttempt, DateTimeOffset now)
    {
        if (lastAttempt is not { } last)
            return "The video engine stopped and could not be restarted.";

        var wait = MinimumSecondsBetweenAttempts - (int)(now - last).TotalSeconds;
        return wait > 0
            ? $"The video engine stopped again. Waiting {wait}s before restarting it — something on "
              + "the timeline is likely to be the cause."
            : "The video engine stopped again.";
    }
}
