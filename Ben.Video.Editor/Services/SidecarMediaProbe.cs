using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// The try-sidecar-first gate for import-time media inspection — item #70 phase 159.
///
/// <para><b>Every method returns null instead of throwing.</b> Null means exactly one thing to
/// the caller: "use the existing wasm path." Disconnected, capability absent, source upload
/// failed, sidecar errored, the deadline elapsed — all collapse to the same null, because the
/// caller's response is identical in every case and an import must never fail merely because a
/// companion process is unhealthy. This mirrors <see cref="NativeClipEncoder"/>'s established
/// pattern for the export path.</para>
///
/// <para>The client-side deadline matters as much as the sidecar's own timeouts: a sidecar that
/// accepts a request and then stops answering would otherwise hang an import indefinitely. Past
/// the deadline this gives up and lets wasm do the work, which is slower but always terminates.</para>
/// </summary>
public sealed class SidecarMediaProbe(
    NativeSidecarService sidecar,
    SidecarMediaClient media,
    BlobUrlLifecycle blobUrls,
    IJSRuntime js,
    ErrorLogService errorLog)
{
    private const string ModulePath = "js/sidecarInterop.js";

    /// <summary>Ownership tag for URLs minted by the sidecar path (phase 144's registry). Distinct
    /// from the wasm path's so a mis-routed revoke shows up as an ownership violation instead of
    /// silently doing the wrong thing.</summary>
    public const string ThumbnailOwner = "SidecarMediaProbe.thumbnail";

    /// <summary>
    /// Revokes a URL this class minted. <b>Deliberately not <c>FfmpegService.RevokePreviewUrlAsync</c></b>:
    /// that method acquires the phase-142 worker lock, so revoking a sidecar thumbnail through it
    /// would queue a one-line <c>URL.revokeObjectURL</c> behind whatever encode currently owns the
    /// wasm worker — reintroducing, for cleanup, precisely the main-thread coupling this phase
    /// removes. These URLs have no MEMFS backing file, so there is nothing the worker needs to do.
    /// </summary>
    public async Task RevokeThumbnailAsync(string url)
    {
        blobUrls.Revoking(url, ThumbnailOwner);
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
            try { await module.InvokeVoidAsync("revokeBlobUrl", url); }
            finally { await module.DisposeAsync(); }
        }
        catch (Exception ex)
        {
            errorLog.Log("SidecarMediaProbe.RevokeThumbnailAsync", ex);
        }
    }

    /// <summary>True when this URL was minted by the sidecar path, so callers holding a mixed set
    /// can route each URL to the right revoke.</summary>
    public bool OwnsThumbnail(string url) => _owned.Contains(url);

    private readonly HashSet<string> _owned = [];

    /// <summary>Deliberately well under the sidecar's own 10s probe timeout and its job timeout:
    /// if the native path hasn't produced an answer in this long it has already lost to wasm, and
    /// the point of the whole exercise is a faster, more responsive import.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    public Task<VideoMetadata?> TryGetMetadataAsync(Guid clipId, string? ext, CancellationToken ct) =>
        TryAsync(SidecarCapabilities.Probe, clipId, ext, ct, async (baseUrl, token, linkedCt) =>
        {
            var info = await media.ProbeAsync(baseUrl, token, clipId, ext!, linkedCt);
            return new VideoMetadata(info.Duration, info.Width, info.Height);
        });

    /// <summary>Returns blob: URLs identical in shape to the wasm path's, or null to fall back.</summary>
    public Task<string[]?> TryExtractThumbnailsAsync(
        Guid clipId, string? ext, int count, double duration, CancellationToken ct) =>
        TryAsync(SidecarCapabilities.Thumbnails, clipId, ext, ct, async (baseUrl, token, linkedCt) =>
        {
            var urls = await media.ExtractThumbnailUrlsAsync(
                baseUrl, token, clipId, ext!, count, duration, linkedCt);
            foreach (var url in urls)
            {
                blobUrls.Created(url, ThumbnailOwner);
                _owned.Add(url);
            }
            return urls.ToArray();
        });

    private async Task<T?> TryAsync<T>(
        string capability, Guid clipId, string? ext, CancellationToken ct,
        Func<string, string, CancellationToken, Task<T>> operation) where T : class
    {
        // No OPFS-backed source means there is nothing uploadable, so the sidecar cannot see this
        // clip at all — same structural limit NativeSidecarBackend documents.
        if (string.IsNullOrEmpty(ext)) return null;
        if (!sidecar.HasCapability(capability)) return null;

        var connection = await sidecar.GetConnectionAsync();
        if (connection is null) return null;
        var (port, token) = connection.Value;

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(Deadline);

        try
        {
            // No per-request timeout is passed down to the transport: the linked deadline above
            // governs the whole operation, exactly as Timeout.InfiniteTimeSpan on the old
            // HttpClient did, and it is what turns into an abort on the JS side.
            return await operation($"http://127.0.0.1:{port}", token, deadlineCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller's own cancellation (import aborted) — propagate, don't silently fall
            // back to a wasm path the caller no longer wants either.
            throw;
        }
        catch (Exception ex)
        {
            // Includes the deadline firing. Any failure here means the sidecar isn't usable for
            // this operation right now — tell NativeSidecarService so subsequent work routes to
            // wasm immediately rather than each call re-discovering a dead process, exactly as
            // NativeSidecarBackend does.
            sidecar.ReportConnectionLost();
            errorLog.Log($"SidecarMediaProbe.{capability}", ex);
            return null;
        }
    }
}
