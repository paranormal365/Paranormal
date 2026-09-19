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

    /// <summary>
    /// The idle timeout to actually arm: the configured one where something will start this process
    /// again when the editor next looks for it, and none at all anywhere else.
    /// </summary>
    /// <remarks>
    /// <para>Stopping is only safe as the first half of a pair. On macOS launchd holds the socket and
    /// starts this again on the next connection, so an idle exit costs nothing. <b>Windows has no such
    /// thing</b>: its installer starts the sidecar once, at login, from a Run key. 1.1.0 armed the
    /// timeout everywhere, so on Windows the sidecar stopped fifteen quiet minutes after login and
    /// stayed stopped until the next one. Anybody who did not open the editor straight away lost the
    /// sidecar for the day, and the editor quietly fell back to doing the work in the browser.</para>
    ///
    /// <para>So the question is not "which operating system" but "will anything bring it back".
    /// Today only a launchd-owned socket answers yes. If Windows ever gains an on-demand start - a
    /// protocol link the editor opens, say - it passes true here and gets the timeout too.</para>
    /// </remarks>
    /// <param name="configured">The timeout from configuration, or the default.</param>
    /// <param name="restartedOnDemand">Whether something starts this process again when it is wanted.</param>
    public static TimeSpan EffectiveTimeout(TimeSpan configured, bool restartedOnDemand)
        => restartedOnDemand ? configured : TimeSpan.Zero;
}
