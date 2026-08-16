using System.Net;
using Ben.Video.Core.SidecarContracts;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Assembles the Working Window preview as a single sidecar concat job — item #70 phase 161.
///
/// <para>This is the first phase a user can actually feel: when it engages, the assembled preview
/// <b>never crosses the WASM heap, never takes the phase-142 worker mutex, and never touches
/// MEMFS</b>. The inputs are already on the sidecar (phase 160's dual residency), the concat is a
/// stream copy, and the result comes back as a blob URL minted in JS.</para>
///
/// <para>Returns null on any failure, like <see cref="SidecarMediaProbe"/> and
/// <see cref="NativeClipEncoder"/>: the caller falls through to the existing in-browser assembly
/// <i>within the same refresh pass</i>, so a sidecar problem costs time, never a blank preview.</para>
/// </summary>
public sealed class SidecarPreviewAssembler(
    NativeSidecarService sidecar,
    RemoteSegmentIndex remoteSegments,
    PreviewUrlRevoker revoker,
    IJSRuntime js,
    SidecarTransport transport,
    ErrorLogService errorLog)
{
    private const string ModulePath = "/_content/Ben.Video.Editor/js/sidecarInterop.js";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Beyond this the native path has already lost to wasm for an interactive preview,
    /// so give up and let the caller re-assemble in-browser rather than keep the user waiting.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Concatenates <paramref name="remoteIds"/> (in order) and returns a blob URL for the result,
    /// already registered with <see cref="PreviewUrlRevoker"/> so the caller's swap-then-revoke
    /// takes the JS route rather than the worker one. Null means "fall back to wasm".
    /// </summary>
    public async Task<string?> TryAssembleAsync(
        IReadOnlyList<Guid> remoteIds, IReadOnlyList<string> segmentNames, CancellationToken ct)
    {
        if (remoteIds.Count == 0) return null;

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
            jobId = await SubmitAsync(baseUrl, token, remoteIds, segmentNames, linkedCt);
            if (jobId is null) return null; // 409 — handled (index pruned) inside SubmitAsync

            var final = await PollAsync(baseUrl, token, jobId.Value, linkedCt);
            if (final.State != JobState.Succeeded)
            {
                errorLog.Log("SidecarPreviewAssembler",
                    $"Concat job failed: {final.ErrorMessage ?? "(no message)"}");
                return null;
            }

            // Straight to a blob URL in JS — the assembled preview's bytes never enter the WASM
            // heap, which is the entire point of this phase.
            var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            try
            {
                var url = await module.InvokeAsync<string>(
                    "fetchAsBlobUrl", linkedCt, $"{baseUrl}/v1/jobs/{jobId.Value:N}/result", token);
                revoker.RegisterSidecarUrl(url);
                return url;
            }
            finally
            {
                await module.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller's own cancellation, not our deadline
        }
        catch (Exception ex)
        {
            // Includes the deadline. Don't ReportConnectionLost here: unlike a render, a failed
            // preview assembly is recoverable in-browser immediately, and tearing down the
            // connection would also disable probe/thumbnail offload for the rest of the session
            // over what may be one bad job.
            errorLog.Log("SidecarPreviewAssembler", ex);
            return null;
        }
        finally
        {
            if (jobId is { } id) _ = DeleteJobBestEffortAsync(baseUrl, token, id);
        }
    }

    private async Task<Guid?> SubmitAsync(
        string baseUrl, string token,
        IReadOnlyList<Guid> remoteIds, IReadOnlyList<string> segmentNames, CancellationToken ct)
    {
        var response = await transport.SendAsync(
            "POST", $"{baseUrl}/v1/jobs/concat", token, new ConcatJobRequest(remoteIds), ct: ct);

        // 409 means some ids the index still believed in are gone (LRU eviction, or a delete that
        // raced this). Prune exactly those so the next refresh doesn't retry the same doomed job,
        // then fall back for this pass.
        if (response.Status == (int)HttpStatusCode.Conflict)
        {
            var missing = response.ReadJson<MissingSegmentsInfo>();
            if (missing is not null) PruneMissing(missing.MissingSegmentIds, remoteIds, segmentNames);
            return null;
        }

        response.EnsureSuccess();
        return response.ReadJson<JobAcceptedBody>()?.JobId;
    }

    /// <summary>Drops index entries for ids the sidecar says it no longer has, so a later refresh
    /// re-renders those segments instead of repeatedly submitting a concat that can't succeed.</summary>
    private void PruneMissing(
        IReadOnlyList<Guid> missingIds, IReadOnlyList<Guid> remoteIds, IReadOnlyList<string> segmentNames)
    {
        var missing = missingIds.ToHashSet();
        for (var i = 0; i < remoteIds.Count && i < segmentNames.Count; i++)
        {
            if (missing.Contains(remoteIds[i])) remoteSegments.Remove(segmentNames[i]);
        }
    }

    private async Task<JobStatusInfo> PollAsync(
        string baseUrl, string token, Guid jobId, CancellationToken ct)
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
        catch { /* the sidecar's own JobRetention sweep handles it */ }
    }

    private sealed record JobAcceptedBody(Guid JobId);
}
