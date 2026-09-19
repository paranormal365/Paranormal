using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>Where a preview blob URL came from, which determines how it must be revoked.</summary>
public enum PreviewUrlOrigin
{
    /// <summary>Minted by <c>FfmpegService.CreatePreviewUrlAsync</c> from a MEMFS file.</summary>
    FfmpegWorker,
    /// <summary>Minted in JS by <c>sidecarInterop.js fetchAsBlobUrl</c>, with no MEMFS backing.</summary>
    Sidecar,
}

/// <summary>
/// Routes a preview URL's revoke to the right implementation — item #70 phase 161.
///
/// <para>Before this phase every preview URL came from the ffmpeg worker, so
/// <c>Ffmpeg.RevokePreviewUrlAsync</c> was unambiguously correct. Now <c>_timelinePreviewUrl</c>
/// can be sidecar-origin, and sending one of those through the worker path would take the
/// phase-142 worker mutex to run a one-line <c>URL.revokeObjectURL</c> — queueing cleanup behind
/// whatever encode currently owns the worker, which is precisely the coupling this arc removes.
/// (It would also register as an ownership mismatch in phase 144's <see cref="BlobUrlLifecycle"/>.)</para>
///
/// <para>Unknown URLs default to the worker route: everything that existed before this phase is
/// worker-origin, so the default keeps every pre-existing caller correct without having to find
/// and annotate each one.</para>
///
/// <para><b>This does not change *when* anything is revoked</b> — phase 144's swap-then-revoke
/// ordering (create new → attach to the element → only then revoke the old) is the caller's
/// concern and is deliberately untouched here. This only changes *how*.</para>
/// </summary>
public sealed class PreviewUrlRevoker(FfmpegService ffmpeg, IJSRuntime js, ErrorLogService errorLog)
{
    private const string ModulePath = "js/sidecarInterop.js";

    private readonly HashSet<string> _sidecarOrigin = [];

    /// <summary>Records that <paramref name="url"/> was created JS-side and must not be revoked
    /// through the ffmpeg worker.</summary>
    public void RegisterSidecarUrl(string url)
    {
        if (!string.IsNullOrEmpty(url)) _sidecarOrigin.Add(url);
    }

    public PreviewUrlOrigin OriginOf(string url) =>
        _sidecarOrigin.Contains(url) ? PreviewUrlOrigin.Sidecar : PreviewUrlOrigin.FfmpegWorker;

    /// <summary>Revokes via the route matching the URL's origin. Best-effort by design: a failed
    /// revoke leaks one object URL for the page's lifetime, which must never be allowed to break
    /// the caller's own swap-then-revoke flow.</summary>
    public async Task RevokeAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        if (!_sidecarOrigin.Remove(url))
        {
            await ffmpeg.RevokePreviewUrlAsync(url);
            return;
        }

        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
            try { await module.InvokeVoidAsync("revokeBlobUrl", url); }
            finally { await module.DisposeAsync(); }
        }
        catch (Exception ex)
        {
            errorLog.Log("PreviewUrlRevoker.RevokeAsync", ex);
        }
    }
}
