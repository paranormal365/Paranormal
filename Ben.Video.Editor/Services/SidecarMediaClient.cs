using System.Net;
using Ben.Video.Core.SidecarContracts;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Sidecar-side media inspection — <c>POST /v1/probe</c> and <c>POST /v1/jobs/thumbnails</c>, item
/// #70 phase 159. The counterpart to <see cref="SidecarSegmentClient"/>, which handles the encode
/// job kinds; this one handles the two operations an <i>import</i> needs.
///
/// <para>Both methods throw on any failure rather than returning null. That is deliberate: the
/// caller (<see cref="FfmpegService"/>) catches and falls back to the in-browser wasm path, so a
/// sidecar problem must be loud enough to trigger that fallback and never quietly become a
/// zero-duration clip or an empty thumbnail strip.</para>
///
/// <para>Probe is a single synchronous request (no job id, no polling) because the sidecar answers
/// it inline — see <c>ProbeEndpoints</c> for why a sub-second metadata read doesn't go through the
/// job machinery. Thumbnails do use the job lifecycle, and additionally fetch a manifest before
/// downloading each frame.</para>
/// </summary>
public sealed class SidecarMediaClient(SidecarSourceUploader uploader, SidecarTransport transport, IJSRuntime js)
{
    private const string ModulePath = "js/sidecarInterop.js";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    public async Task<MediaProbeInfo> ProbeAsync(
        string baseUrl, string token, Guid clipId, string ext, CancellationToken ct)
    {
        await uploader.EnsureUploadedAsync(baseUrl, token, clipId, ext, ct);

        var response = await transport.SendAsync(
            "POST", $"{baseUrl}/v1/probe", token, new MediaProbeRequest(clipId, ext), ct: ct);
        response.EnsureSuccess();

        return response.ReadJson<MediaProbeInfo>()
            ?? throw new InvalidOperationException("Sidecar returned an empty probe result.");
    }

    /// <summary>
    /// Returns one blob: URL per extracted frame, in order — the same shape
    /// <c>ffmpegInterop.js extractThumbnails</c> returns, so callers can't tell which path
    /// produced them.
    ///
    /// <para><b>The frame bytes never enter the WASM heap.</b> Each file is fetched by
    /// <c>sidecarInterop.js fetchAsBlobUrl</c> and wrapped into an object URL entirely in JS; only
    /// the URL strings cross into Blazor. Marshalling N webp payloads through C# would put them on
    /// the exact single-threaded heap this whole item exists to relieve.</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractThumbnailUrlsAsync(
        string baseUrl, string token, Guid clipId, string ext,
        int count, double duration, CancellationToken ct)
    {
        await uploader.EnsureUploadedAsync(baseUrl, token, clipId, ext, ct);

        var jobId = await PostThumbnailJobAsync(baseUrl, token, new ThumbnailJobRequest(clipId, ext, count, duration), ct);

        var module = await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
        var urls = new List<string>(count);
        try
        {
            var final = await PollUntilDoneAsync(baseUrl, token, jobId, ct);
            if (final.State != JobState.Succeeded)
                throw new InvalidOperationException(final.ErrorMessage ?? "Native thumbnail extraction failed.");

            var manifest = await GetManifestAsync(baseUrl, token, jobId, ct);

            foreach (var file in manifest.Files)
            {
                var url = $"{baseUrl}/v1/jobs/{jobId:N}/result/{Uri.EscapeDataString(file.Name)}";
                urls.Add(await module.InvokeAsync<string>("fetchAsBlobUrl", ct, url, token));
            }
            return urls;
        }
        catch
        {
            // Don't leak the URLs already minted before the failure — the caller never receives
            // them, so nothing else can revoke them.
            foreach (var url in urls)
            {
                try { await module.InvokeVoidAsync("revokeBlobUrl", url); } catch { /* best-effort */ }
            }
            throw;
        }
        finally
        {
            await module.DisposeAsync();
            // Unlike a segment job (one file the caller keeps), a thumbnail job's workspace holds N
            // frames that are useless the moment they're fetched — delete eagerly rather than
            // waiting out JobRetention, since an import can produce several of these in a row.
            _ = DeleteJobBestEffortAsync(baseUrl, token, jobId);
        }
    }

    private async Task<Guid> PostThumbnailJobAsync(
        string baseUrl, string token, ThumbnailJobRequest body, CancellationToken ct)
    {
        var response = await transport.SendAsync(
            "POST", $"{baseUrl}/v1/jobs/thumbnails", token, body, ct: ct);
        response.EnsureSuccess();

        var accepted = response.ReadJson<JobAcceptedBody>();
        return accepted?.JobId ?? throw new InvalidOperationException("Sidecar accepted the job but returned no job id.");
    }

    private async Task<JobStatusInfo> PollUntilDoneAsync(
        string baseUrl, string token, Guid jobId, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await transport.SendAsync(
                "GET", $"{baseUrl}/v1/jobs/{jobId:N}", token, ct: ct);

            // A sidecar restart drops every in-memory job (SegmentJobStore is deliberately not
            // persisted), so a 404 mid-poll means "gone, never coming back" — surface it instead
            // of spinning until the caller's own timeout.
            if (response.Status == (int)HttpStatusCode.NotFound)
                throw new InvalidOperationException("Sidecar job disappeared (sidecar restarted?).");

            response.EnsureSuccess();
            var info = response.ReadJson<JobStatusInfo>()
                ?? throw new InvalidOperationException("Sidecar returned an empty job status.");

            if (info.State != JobState.Running) return info;
            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task<ResultManifest> GetManifestAsync(
        string baseUrl, string token, Guid jobId, CancellationToken ct)
    {
        var response = await transport.SendAsync(
            "GET", $"{baseUrl}/v1/jobs/{jobId:N}/result", token, ct: ct);
        response.EnsureSuccess();
        return response.ReadJson<ResultManifest>()
            ?? throw new InvalidOperationException("Sidecar returned an empty result manifest.");
    }

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
