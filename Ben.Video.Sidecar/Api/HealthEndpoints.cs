using Ben.Video.Core.SidecarContracts;
using Ben.Video.Sidecar.Jobs;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Api;

public static class HealthEndpoints
{
    /// <summary>Bumped to 3 by item #70 phase 158 (adds <c>GET /v1/capabilities</c>). The version
    /// itself stays informational — the browser gates features on the capability list, not on this
    /// number, so a sidecar that grows a capability mid-version-3 needs no further bump.</summary>
    public const int ProtocolVersion = 3;

    /// <summary>The sidecar's own release version — informational only, never used for any
    /// security or compatibility decision.</summary>
    public static readonly string AppVersion =
        typeof(HealthEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/health", async (
            FfmpegRunner runner, FfmpegLocator locator, JobRegistry jobs, CancellationToken ct) =>
        {
            var integrityOk = locator.VerifyIntegrity();
            var version = integrityOk ? await runner.TryGetVersionAsync(ct) : null;
            if (version is not null) jobs.LastKnownFfmpegVersion = version;

            return Results.Ok(new HealthInfo(
                ProtocolVersion: ProtocolVersion,
                AppVersion: AppVersion,
                FfmpegVersion: version,
                FfmpegIntegrityOk: integrityOk,
                RequiresPairing: true));
        });

        app.MapGet("/v1/status", (JobRegistry jobs, IOptions<SidecarOptions> options) =>
        {
            return Results.Ok(new StatusInfo(
                ProtocolVersion: ProtocolVersion,
                AppVersion: AppVersion,
                FfmpegVersion: jobs.LastKnownFfmpegVersion,
                ActiveJobCount: jobs.ActiveCount,
                MaxConcurrentJobs: options.Value.MaxConcurrentJobs));
        });

        // Item #70 phase 158 — what this sidecar can actually do, so a newer browser can light up
        // features against a newer sidecar while still working unchanged against an older one.
        // Token-gated automatically (SecurityMiddleware exempts only /v1/health), and deliberately
        // NOT folded into /v1/health — see CapabilitiesInfo's doc comment for the Disallow-skew
        // reason that would silently break old clients.
        app.MapGet("/v1/capabilities", (JobRegistry jobs, FfmpegLocator locator) =>
        {
            // Item #70 phase 160 — concat needs only ffmpeg (it's a stream copy over segments the
            // sidecar already holds), so it rides alongside "segment" rather than being gated on
            // ffprobe like probe/thumbnails are. Advertising it is also what tells the client it's
            // safe to send Retain=true: an older sidecar would hard-400 that unknown field.
            var capabilities = new List<string> { SidecarCapabilities.Segment, SidecarCapabilities.Concat, SidecarCapabilities.ExportAssemble };

            // Probe/thumbnails need a real, integrity-verified ffprobe. Without one this list just
            // omits them and the browser keeps doing that work in wasm — the pre-158 behavior.
            if (locator.VerifyIntegrity(FfmpegTool.Ffprobe))
            {
                capabilities.Add(SidecarCapabilities.Probe);
                capabilities.Add(SidecarCapabilities.Thumbnails);
            }

            return Results.Ok(new CapabilitiesInfo(
                ProtocolVersion: ProtocolVersion,
                InstanceId: jobs.InstanceId,
                Capabilities: capabilities));
        });
    }
}
