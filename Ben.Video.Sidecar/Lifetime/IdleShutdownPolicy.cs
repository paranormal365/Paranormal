namespace Ben.Video.Sidecar.Lifetime;

/// <summary>
/// Whether a sidecar with nothing to do should stop.
/// </summary>
/// <remarks>
/// Ben, 2026-09-19: "I do not want the sidecar to always run. I only want it to run when the video
/// editor is running. I want it to end if the end user is not using the video editor in the app."
///
/// <para>Stopping is only half of that — launchd starts it again on the next connection to its
/// port, so the editor finding it is what brings it back. See the installer's Sockets key and
/// LaunchdSockets.</para>
///
/// <para>A clean exit matters: the LaunchAgent's KeepAlive is SuccessfulExit=false, so a process
/// that ends deliberately stays ended, while one that crashes comes back.</para>
/// </remarks>
public static class IdleShutdownPolicy
{
    /// <summary>
    /// Long enough that a pause in editing does not cost a re-render. A new process gets a new
    /// JobRegistry.InstanceId, and the editor drops its whole remote-segment index when that
    /// changes, so every retained segment would have to be made again. The editor's heartbeat
    /// keeps this from mattering while the editor is actually open.
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Should the process stop now?
    /// </summary>
    /// <param name="idleFor">Time since the last request of any kind.</param>
    /// <param name="idleTimeout">How long idle is allowed to last. Zero or less disables stopping.</param>
    /// <param name="activeJobs">Jobs running right now — a render in flight is never interrupted.</param>
    public static bool ShouldStop(TimeSpan idleFor, TimeSpan idleTimeout, int activeJobs)
    {
        if (idleTimeout <= TimeSpan.Zero) return false;   // switched off
        if (activeJobs > 0) return false;                 // someone is waiting on a render
        return idleFor >= idleTimeout;
    }
}
