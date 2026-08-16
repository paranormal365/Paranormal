using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Editor.Services;

/// <summary>
/// The HTTP/JS-interop plumbing shared by every caller that submits one <see cref="SegmentRenderSpec"/>
/// to the sidecar and wants the finished bytes back — item #38 phases F/124.
/// <see cref="NativeSidecarBackend"/> (background preview render, Rough/Fine) and
/// <see cref="NativeClipEncoder"/> (real export, phase 124's per-clip native offload) both build
/// their own spec for their own reasons and call <see cref="RunAsync"/> — extracted here so the
/// upload/submit/poll/download/cleanup sequence exists in exactly one place. Throws on any
/// failure; callers decide what "the connection is gone" means for their own case (e.g.
/// <see cref="NativeSidecarService.ReportConnectionLost"/>).
/// </summary>
public sealed class SidecarSegmentClient(SidecarSourceUploader uploader, SidecarTransport transport)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Bytes plus, when the spec asked for retention (item #70 phase 160), the id of the
    /// sidecar's own retained copy — null when retention wasn't requested or the sidecar declined
    /// to keep one.</summary>
    public sealed record SegmentRunResult(byte[] Bytes, Guid? RetainedSegmentId);

    public async Task<SegmentRunResult> RunAsync(
        string baseUrl, string token, Guid clipId, string ext,
        SegmentRenderSpec spec, IProgress<int>? progress, CancellationToken ct)
    {
        // Item #70 phase 159 — the HEAD/PUT source upload moved to SidecarSourceUploader once
        // probe and thumbnail jobs became additional callers of the identical step.
        await uploader.EnsureUploadedAsync(baseUrl, token, clipId, ext, ct);

        var jobId = await PostJobAsync(baseUrl, token, spec, ct);
        var final = await PollUntilDoneAsync(baseUrl, token, jobId, progress, ct);

        if (final.State != JobState.Succeeded)
            throw new InvalidOperationException(final.ErrorMessage ?? "Native render failed.");

        var bytes = await GetResultBytesAsync(baseUrl, token, jobId, ct);
        _ = DeleteJobBestEffortAsync(baseUrl, token, jobId);
        return new SegmentRunResult(bytes, final.RetainedSegmentId);
    }

    private async Task<Guid> PostJobAsync(
        string baseUrl, string token, SegmentRenderSpec spec, CancellationToken ct)
    {
        var response = await transport.SendAsync(
            "POST", $"{baseUrl}/v1/jobs/segment", token, spec, ct: ct);
        response.EnsureSuccess();

        // LenientResponses for anything the sidecar SENDS (item #70 phase 158) — a newer sidecar
        // may add fields to these bodies (phase 160 adds a retained-segment id to job status) and
        // an older browser build must ignore them, not throw. Requests above stay strict.
        var body = response.ReadJson<JobAcceptedBody>();
        return body?.JobId ?? throw new InvalidOperationException("Sidecar accepted the job but returned no job id.");
    }

    private async Task<JobStatusInfo> PollUntilDoneAsync(
        string baseUrl, string token, Guid jobId, IProgress<int>? progress, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await transport.SendAsync(
                "GET", $"{baseUrl}/v1/jobs/{jobId:N}", token, ct: ct);
            response.EnsureSuccess();
            var info = response.ReadJson<JobStatusInfo>()
                ?? throw new InvalidOperationException("Sidecar returned an empty job status.");

            progress?.Report(info.ProgressPercent);
            if (info.State != JobState.Running) return info;

            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task<byte[]> GetResultBytesAsync(
        string baseUrl, string token, Guid jobId, CancellationToken ct) =>
        await transport.GetBytesAsync($"{baseUrl}/v1/jobs/{jobId:N}/result", token, ct: ct);

    private async Task DeleteJobBestEffortAsync(string baseUrl, string token, Guid jobId)
    {
        try
        {
            await transport.SendAsync("DELETE", $"{baseUrl}/v1/jobs/{jobId:N}", token);
        }
        catch { /* the sidecar's own JobRetention sweep cleans this up eventually either way */ }
    }

    private sealed record JobAcceptedBody(Guid JobId);
}
