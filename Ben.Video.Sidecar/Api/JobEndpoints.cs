using System.Text.Json;
using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Ben.Video.Sidecar.Jobs;
using Ben.Video.Sidecar.Storage;
using Ben.Video.Sidecar.Validation;

namespace Ben.Video.Sidecar.Api;

/// <summary>
/// Segment-render job endpoints — item #38 phase 123 (F). <c>POST</c> accepts a typed
/// <see cref="SegmentRenderSpec"/> (never argv, never a filter string — see
/// <see cref="ArgvFactory"/>), returns a job id immediately, and runs the actual encode on a
/// background task (<see cref="SegmentJobRunner"/>). The browser polls <c>GET /v1/jobs/{id}</c>
/// for progress rather than a push channel (SSE/WebSocket) — see <see cref="JobStatusInfo"/>'s doc
/// comment for why.
/// </summary>
public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/jobs/segment", async (
            HttpRequest request, SpecValidator validator, ClipEffectRegistry registry,
            FfmpegLocator locator, SegmentJobRunner runner, CancellationToken ct) =>
        {
            // Threat T7 (supply chain), enforced here as the plan requires: a bundled binary that
            // fails its SHA-256 check must never be allowed to run, so job submission fails
            // closed rather than silently falling through to whatever's on ExecutablePath.
            if (!locator.VerifyIntegrity())
                return Results.Problem(
                    "ffmpeg binary failed integrity verification.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            SegmentRenderSpec spec;
            try
            {
                spec = await JsonSerializer.DeserializeAsync<SegmentRenderSpec>(request.Body, SidecarJsonOptions.Default, ct)
                    ?? throw new JsonException("Empty body.");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Malformed job spec.");
            }

            var error = validator.ValidateSegmentSpec(spec, registry);
            if (error is not null) return Results.BadRequest(error);

            var jobId = runner.Start(spec);
            return Results.Accepted($"/v1/jobs/{jobId}", new { jobId });
        });

        // Item #70 phase 159 — thumbnail-strip extraction. Same submit/poll lifecycle as a segment
        // job and the same shared encode budget, but produces N files instead of one (see the
        // multi-file result endpoints below).
        app.MapPost("/v1/jobs/thumbnails", async (
            HttpRequest request, SpecValidator validator, FfmpegLocator locator,
            ThumbnailJobRunner runner, CancellationToken ct) =>
        {
            if (!locator.VerifyIntegrity())
                return Results.Problem(
                    "ffmpeg binary failed integrity verification.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            ThumbnailJobRequest thumbRequest;
            try
            {
                thumbRequest = await JsonSerializer.DeserializeAsync<ThumbnailJobRequest>(
                    request.Body, SidecarJsonOptions.Default, ct)
                    ?? throw new JsonException("Empty body.");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Malformed thumbnail request.");
            }

            var error = validator.ValidateThumbnailRequest(thumbRequest);
            if (error is not null) return Results.BadRequest(error);

            var jobId = runner.Start(thumbRequest);
            return Results.Accepted($"/v1/jobs/{jobId}", new { jobId });
        });

        // Item #70 phase 160 — concatenate segments the sidecar ALREADY holds (dual residency),
        // so no bytes cross the loopback for the inputs. Capability-gated as "concat".
        app.MapPost("/v1/jobs/concat", async (
            HttpRequest request, FfmpegLocator locator, ConcatJobRunner runner, CancellationToken ct) =>
        {
            if (!locator.VerifyIntegrity())
                return Results.Problem(
                    "ffmpeg binary failed integrity verification.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            ConcatJobRequest concatRequest;
            try
            {
                concatRequest = await JsonSerializer.DeserializeAsync<ConcatJobRequest>(
                    request.Body, SidecarJsonOptions.Default, ct)
                    ?? throw new JsonException("Empty body.");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Malformed concat request.");
            }

            if (concatRequest.SegmentIds is not { Count: > 0 })
                return Results.BadRequest("At least one segment id is required.");
            if (concatRequest.SegmentIds.Count > 1000)
                return Results.BadRequest("Too many segments in one concat request.");
            if (concatRequest.SegmentIds.Any(id => id == Guid.Empty))
                return Results.BadRequest("Segment ids must be non-empty GUIDs.");

            // 409 with the FULL missing list rather than a bare failure: the caller can then
            // re-render exactly those segments instead of guessing which ones went away (or
            // redoing all of them).
            var missing = runner.FindMissing(concatRequest.SegmentIds);
            if (missing.Count > 0)
            {
                return Results.Json(
                    new MissingSegmentsInfo(missing), SidecarJsonOptions.Default,
                    statusCode: StatusCodes.Status409Conflict);
            }

            var concatJobId = runner.Start(concatRequest);
            return Results.Accepted($"/v1/jobs/{concatJobId}", new { jobId = concatJobId });
        });

        // Item #70 phase 162 — concat + optional audio mix as ONE job producing one result.
        // Splitting them would mean downloading and re-uploading the large intermediate.
        app.MapPost("/v1/jobs/export-assemble", async (
            HttpRequest request, SpecValidator validator, FfmpegLocator locator,
            ExportAssembleJobRunner runner, CancellationToken ct) =>
        {
            if (!locator.VerifyIntegrity())
                return Results.Problem(
                    "ffmpeg binary failed integrity verification.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            ExportAssembleRequest assembleRequest;
            try
            {
                assembleRequest = await JsonSerializer.DeserializeAsync<ExportAssembleRequest>(
                    request.Body, SidecarJsonOptions.Default, ct)
                    ?? throw new JsonException("Empty body.");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Malformed assemble request.");
            }

            var error = validator.ValidateExportAssembleRequest(assembleRequest);
            if (error is not null) return Results.BadRequest(error);

            // Same 409-with-the-list contract as concat: the caller can re-render exactly what's
            // gone (or re-upload exactly the audio sources that aged out) instead of guessing.
            var missingSegments = runner.FindMissingSegments(assembleRequest.SegmentIds);
            if (missingSegments.Count > 0)
                return Results.Json(new MissingSegmentsInfo(missingSegments), SidecarJsonOptions.Default,
                    statusCode: StatusCodes.Status409Conflict);

            var missingAudio = runner.FindMissingAudioSources(assembleRequest.Audio);
            if (missingAudio.Count > 0)
                return Results.Json(new MissingSegmentsInfo(missingAudio), SidecarJsonOptions.Default,
                    statusCode: StatusCodes.Status409Conflict);

            var assembleJobId = runner.Start(assembleRequest);
            return Results.Accepted($"/v1/jobs/{assembleJobId}", new { jobId = assembleJobId });
        });

        app.MapGet("/v1/jobs/{jobId:guid}", (Guid jobId, SegmentJobStore store) =>
        {
            var record = store.Get(jobId);
            if (record is null) return Results.NotFound();

            var info = new JobStatusInfo(
                record.Id, record.State, record.ProgressPercent, record.ErrorMessage, record.ResultSizeBytes,
                record.RetainedSegmentId);
            return Results.Json(info, SidecarJsonOptions.Default);
        });

        app.MapGet("/v1/jobs/{jobId:guid}/result", (Guid jobId, SegmentJobStore store) =>
        {
            var record = store.Get(jobId);
            if (record is null) return Results.NotFound();

            return record.State switch
            {
                JobState.Failed => Results.Problem(record.ErrorMessage ?? "Job failed.", statusCode: StatusCodes.Status500InternalServerError),
                // 425 Too Early — no StatusCodes constant for this one.
                JobState.Running => Results.StatusCode(425),
                // Item #70 phase 159 — a multi-file kind (thumbnails) answers with a manifest
                // instead of a file; each entry is then fetched from result/{name} below.
                // Single-file kinds are completely unchanged from phase 123.
                _ when record.ResultFileNames is { Count: > 0 } names && record.ResultDirectory is not null =>
                    Results.Json(
                        new ResultManifest([.. names.Select(n =>
                            new ResultFileInfo(n, new FileInfo(Path.Combine(record.ResultDirectory, n)).Length))]),
                        SidecarJsonOptions.Default),
                _ when record.ResultPath is null => Results.StatusCode(425),
                _ => Results.File(record.ResultPath, "video/mp4", enableRangeProcessing: false),
            };
        });

        // Item #70 phase 159 — one file out of a multi-file result.
        //
        // The requested name is matched against the job's OWN recorded manifest rather than being
        // combined into a path: an unlisted or crafted name (traversal, absolute path, a file this
        // job didn't produce) simply isn't in ResultFileNames and 404s before any filesystem call.
        // That keeps the "no raw request string ever reaches a filesystem path" property the rest
        // of this API holds (see SpecValidator's doc comment).
        app.MapGet("/v1/jobs/{jobId:guid}/result/{fileName}", (Guid jobId, string fileName, SegmentJobStore store) =>
        {
            var record = store.Get(jobId);
            if (record?.ResultFileNames is not { } names || record.ResultDirectory is null)
                return Results.NotFound();

            if (!names.Contains(fileName, StringComparer.Ordinal)) return Results.NotFound();

            var path = Path.Combine(record.ResultDirectory, fileName);
            if (!File.Exists(path)) return Results.NotFound();

            return Results.File(path, "image/webp", enableRangeProcessing: false);
        });

        app.MapDelete("/v1/jobs/{jobId:guid}", (Guid jobId, SegmentJobStore store, SidecarPaths paths) =>
        {
            var record = store.Get(jobId);
            record?.Cts.Cancel();
            store.Remove(jobId);

            var workDir = Path.Combine(paths.JobsDir, $"{jobId:N}");
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch { /* best-effort */ }

            return Results.NoContent();
        });
    }
}
