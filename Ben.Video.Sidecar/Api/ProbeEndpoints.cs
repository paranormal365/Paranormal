using System.Text.Json;
using Ben.Video.Core.SidecarContracts;
using Ben.Video.Sidecar.Jobs;
using Ben.Video.Sidecar.Storage;
using Ben.Video.Sidecar.Validation;

namespace Ben.Video.Sidecar.Api;

/// <summary>
/// Media probing — item #70 phase 159. The sidecar counterpart to the browser's
/// <c>FfmpegService.GetMetadataAsync</c>, which today runs ffprobe inside wasm on the single main
/// thread while holding the phase-142 worker mutex.
///
/// <para><b>Synchronous, unlike every other ffmpeg work in this process.</b> A probe is a
/// sub-second read-only metadata read, not an encode: the submit/poll/download job machinery would
/// add several round trips and a 400 ms polling floor to something that finishes faster than one
/// poll interval. It also gets its own concurrency limit rather than sharing the 2-slot encode
/// budget — queueing a probe behind two half-hour encodes would make the sidecar path strictly
/// slower than the wasm path it's replacing, which is the exact contention failure (item #66) this
/// whole arc exists to eliminate.</para>
/// </summary>
public static class ProbeEndpoints
{
    /// <summary>Generous relative to the 2-slot encode budget because these are cheap and short;
    /// still bounded, so a flood of probe requests can't spawn unbounded processes (threat T6).</summary>
    private static readonly SemaphoreSlim ProbeConcurrency = new(4, 4);

    /// <summary>A probe that takes longer than this is not answering — fail fast and let the
    /// browser fall back to wasm rather than stalling an import behind a wedged process.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public static void MapProbeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/probe", async (
            HttpRequest httpRequest, SpecValidator validator, SourceCache sources,
            FfmpegRunner ffmpeg, FfmpegLocator locator, SidecarPaths paths,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            // Fails closed on an unverified/absent ffprobe exactly as segment submission does for
            // ffmpeg. The browser shouldn't reach here anyway (the capability wouldn't be
            // advertised), but a stale capability list must not become a way to run an
            // unverified binary.
            if (!locator.VerifyIntegrity(FfmpegTool.Ffprobe))
                return Results.Problem(
                    "ffprobe is unavailable or failed integrity verification.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            MediaProbeRequest request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<MediaProbeRequest>(
                    httpRequest.Body, SidecarJsonOptions.Default, ct)
                    ?? throw new JsonException("Empty body.");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Malformed probe request.");
            }

            // ValidateExtension returns the NORMALIZED extension, or null when it isn't on the
            // allowlist — null is the rejection, not the success case.
            if (request.ClipId == Guid.Empty) return Results.BadRequest("ClipId is required.");
            if (validator.ValidateExtension(request.SourceExt) is null)
                return Results.BadRequest("Unsupported or missing SourceExt.");

            var inputPath = sources.GetPathIfExists(request.ClipId, request.SourceExt);
            if (inputPath is null)
                return Results.NotFound("Source clip not uploaded — PUT /v1/sources/{clipId} first.");

            var logger = loggerFactory.CreateLogger("ProbeEndpoints");
            await ProbeConcurrency.WaitAsync(ct);
            sources.MarkInUse(request.ClipId);
            try
            {
                // -show_format alongside -show_streams: some containers only carry a usable
                // duration at the format level. The parser prefers stream durations (matching the
                // browser exactly) and this is purely extra context for diagnostics.
                var args = new[]
                {
                    "-v", "quiet", "-print_format", "json",
                    "-show_streams", "-show_format", inputPath,
                };

                var result = await ffmpeg.RunAsync(
                    args, paths.JobsDir, ProbeTimeout, tool: FfmpegTool.Ffprobe, ct: ct);

                if (result.TimedOut || result.ExitCode != 0)
                {
                    logger.LogWarning(
                        "Probe failed for {ClipId}: timedOut={TimedOut} exit={Exit}",
                        request.ClipId, result.TimedOut, result.ExitCode);
                    return Results.Problem("ffprobe failed.", statusCode: StatusCodes.Status500InternalServerError);
                }

                var info = FfprobeOutputParser.TryParse(result.StdOut);
                if (info is null)
                    return Results.Problem("ffprobe produced unparseable output.", statusCode: StatusCodes.Status500InternalServerError);

                return Results.Json(info, SidecarJsonOptions.Default);
            }
            finally
            {
                sources.MarkNotInUse(request.ClipId);
                ProbeConcurrency.Release();
            }
        });
    }
}
