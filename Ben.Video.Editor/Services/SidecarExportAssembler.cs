using System.Net;
using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Runs an export's concat (+ audio mix) as one sidecar job — item #70 phase 162.
///
/// <para>Returns the MEMFS name the assembled body was written to, or null to fall back. The
/// result is streamed in as a <c>File</c> handle via <c>FfmpegService.WriteFileAsync</c>, never as
/// a <c>byte[]</c>: an export body is the largest artifact this app moves, so keeping it off the
/// WASM heap matters more here than anywhere else.</para>
///
/// <para>Fallback is free by construction — the segments are still in MEMFS (dual residency), so
/// the caller simply runs today's <c>ConcatSegmentsAsync</c>/<c>MixAudioTracksAsync</c> with no
/// rework and no re-render.</para>
/// </summary>
public sealed class SidecarExportAssembler(
    NativeSidecarService sidecar,
    SidecarSourceUploader uploader,
    FfmpegService ffmpeg,
    IJSRuntime js,
    SidecarTransport transport,
    ErrorLogService errorLog)
{
    private const string ModulePath = "js/sidecarInterop.js";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Generous relative to the preview's: an export is a deliberate, long operation the
    /// user is already waiting on, so giving up early and redoing it in wasm would be worse than
    /// waiting.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(30);

    public sealed record AudioSource(Guid ClipId, string Ext, double Start, double End, string FilterChain);

    /// <summary>Assembles and returns the MEMFS name of the result, or null to fall back.</summary>
    public async Task<string?> TryAssembleAsync(
        IReadOnlyList<Guid> segmentIds,
        ExportQualityDto quality,
        IReadOnlyList<AudioSource> audioSources,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        if (segmentIds.Count == 0) return null;

        var connection = await sidecar.GetConnectionAsync();
        if (connection is null) return null;
        var (port, token) = connection.Value;
        var baseUrl = $"http://127.0.0.1:{port}";

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(Deadline);
        var linkedCt = deadlineCts.Token;

        Guid? jobId = null;
        try
        {
            // Audio sources must be on the sidecar before the job runs. HEAD-before-PUT means a
            // clip already uploaded for a render isn't sent twice.
            foreach (var audio in audioSources)
                await uploader.EnsureUploadedAsync(baseUrl, token, audio.ClipId, audio.Ext, linkedCt);

            var request = new ExportAssembleRequest(
                segmentIds,
                quality,
                audioSources.Count == 0
                    ? null
                    : new ExportAudioMixDto([.. audioSources.Select(a =>
                        new AudioMixClipDto(a.ClipId, a.Ext, a.Start, a.End, a.FilterChain))]));

            var submitResponse = await transport.SendAsync(
                "POST", $"{baseUrl}/v1/jobs/export-assemble", token, request, ct: linkedCt);
            if (submitResponse.Status == (int)HttpStatusCode.Conflict)
            {
                // Something aged out between the gate check and submission. Falling back is
                // correct and cheap — no need to prune anything here, since an export doesn't
                // repeat the way a preview refresh does.
                errorLog.Log("SidecarExportAssembler", "Assemble rejected: inputs no longer retained.");
                return null;
            }
            submitResponse.EnsureSuccess();

            jobId = submitResponse.ReadJson<JobAcceptedBody>()?.JobId;
            if (jobId is null) return null;

            var final = await PollAsync(baseUrl, token, jobId.Value, progress, linkedCt);
            if (final.State != JobState.Succeeded)
            {
                errorLog.Log("SidecarExportAssembler",
                    $"Assemble failed: {final.ErrorMessage ?? "(no message)"}");
                return null;
            }

            // Stream straight into MEMFS as a File handle — no byte[] on the WASM heap.
            var outputName = $"assembled_{jobId.Value:N}.mp4";
            var module = await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
            try
            {
                var fileRef = await module.InvokeAsync<IJSObjectReference>(
                    "fetchResultAsFile", linkedCt,
                    $"{baseUrl}/v1/jobs/{jobId.Value:N}/result", token, outputName);
                try { await ffmpeg.WriteFileAsync(outputName, fileRef); }
                finally { await fileRef.DisposeAsync(); }
            }
            finally
            {
                await module.DisposeAsync();
            }

            return outputName;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the user cancelled the export itself
        }
        catch (Exception ex)
        {
            errorLog.Log("SidecarExportAssembler", ex);
            return null;
        }
        finally
        {
            if (jobId is { } id) _ = DeleteJobBestEffortAsync(baseUrl, token, id);
        }
    }

    private async Task<JobStatusInfo> PollAsync(
        string baseUrl, string token, Guid jobId,
        IProgress<int>? progress, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await transport.SendAsync(
                "GET", $"{baseUrl}/v1/jobs/{jobId:N}", token, ct: ct);

            if (response.Status == (int)HttpStatusCode.NotFound)
                throw new InvalidOperationException("Sidecar job disappeared (sidecar restarted?).");

            response.EnsureSuccess();
            var info = response.ReadJson<JobStatusInfo>()
                ?? throw new InvalidOperationException("Sidecar returned an empty job status.");

            progress?.Report(info.ProgressPercent);
            if (info.State != JobState.Running) return info;
            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task DeleteJobBestEffortAsync(string baseUrl, string token, Guid jobId)
    {
        try
        {
            await transport.SendAsync("DELETE", $"{baseUrl}/v1/jobs/{jobId:N}", token);
        }
        catch { /* JobRetention sweeps it */ }
    }

    private sealed record JobAcceptedBody(Guid JobId);
}
