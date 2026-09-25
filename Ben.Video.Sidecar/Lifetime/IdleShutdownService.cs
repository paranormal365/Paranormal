using Ben.Video.Sidecar.Jobs;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Lifetime;

/// <summary>
/// Ends the process once nobody is using it, so it is not a service that runs for ever.
/// </summary>
/// <remarks>
/// <para>Installed, this is started BY launchd when something connects to its port and is expected
/// to go away again afterwards — so stopping is normal operation, not a failure. The LaunchAgent's
/// KeepAlive is SuccessfulExit=false precisely so a deliberate exit stays exited while a crash
/// still comes back.</para>
///
/// <para>It writes a line saying why before it goes. A helper that vanishes silently is
/// indistinguishable from one that crashed, and the log is the only place anybody would look.</para>
/// </remarks>
public sealed class IdleShutdownService(
    IdleWatch idle,
    JobRegistry jobs,
    IOptions<SidecarOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<IdleShutdownService> log) : BackgroundService
{
    /// <summary>Often enough to be prompt, rarely enough to cost nothing.</summary>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeout = options.Value.IdleTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            log.LogInformation("Idle shutdown is off; this sidecar will run until it is stopped.");
            return;
        }

        log.LogInformation("Idle shutdown armed: stopping after {Minutes} quiet minutes with no job running.",
            timeout.TotalMinutes);

        using var timer = new PeriodicTimer(CheckEvery);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var idleFor = idle.IdleFor;
            if (!IdleShutdownPolicy.ShouldStop(idleFor, timeout, jobs.ActiveCount)) continue;

            log.LogInformation(
                "Nothing has asked for anything in {Minutes:F1} minutes and no job is running — stopping. " +
                "launchd will start this again when the editor next looks for it.",
                idleFor.TotalMinutes);

            lifetime.StopApplication();
            return;
        }
    }
}
