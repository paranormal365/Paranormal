using Ben.Video.Sidecar.Jobs;
using Ben.Video.Sidecar.Lifetime;

namespace Ben.Video.Sidecar.Api;

/// <summary>
/// Stopping the sidecar on request - the "off" half of the switch in the editor.
/// </summary>
/// <remarks>
/// <para>Turning it back ON cannot be done here, for the obvious reason, and is not something a web
/// page can do directly either: the editor opens a registered <c>benvideo-sidecar:</c> link and
/// Windows starts the program. That is why the package manifest declares a protocol handler.</para>
///
/// <para>Token-gated and Origin-checked like everything else, by <c>SecurityMiddleware</c> - this
/// endpoint needs no gate of its own, and deliberately does not get an exemption. A page that
/// cannot render a segment cannot stop the process either.</para>
/// </remarks>
public static class LifetimeEndpoints
{
    public static void MapLifetimeEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/shutdown", (
            JobRegistry jobs, IHostApplicationLifetime lifetime, ILoggerFactory logs, bool? force) =>
        {
            var log = logs.CreateLogger(typeof(LifetimeEndpoints).FullName!);
            var active = jobs.ActiveCount;

            if (ShutdownPolicy.Decide(active, force ?? false) == ShutdownDecision.RefuseWorkInProgress)
            {
                log.LogInformation("Refused a shutdown request: {Active} job(s) running.", active);
                return Results.Conflict(new ShutdownRefused(active));
            }

            log.LogInformation("Shutting down on request ({Active} job(s) running).", active);

            // Stopped AFTER this response is written, not during it: StopApplication here would
            // race Kestrel finishing the reply, and the caller would see a dropped connection
            // rather than the answer to what it asked.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
                lifetime.StopApplication();
            });

            return Results.Accepted(value: new ShutdownAccepted(active));
        });
    }

    /// <param name="ActiveJobs">How many are running, so the editor can say "2 exports are still
    /// running" rather than "no".</param>
    public sealed record ShutdownRefused(int ActiveJobs)
    {
        public string Message { get; } = "Work is in progress. Ask again with force=true to stop anyway.";
    }

    /// <param name="ActiveJobs">What was thrown away, if anything - zero in the ordinary case.</param>
    public sealed record ShutdownAccepted(int ActiveJobs)
    {
        public string Message { get; } = "Stopping.";
    }
}
