using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Singleton service that owns all Origin Private File System (OPFS) I/O.
///
/// Source clips are stored under <c>bv-clips/{clipId}{ext}</c> so that saved
/// projects can be reopened in a future session without the user manually
/// re-importing files.
///
/// This service is completely independent of <see cref="FfmpegService"/> which
/// manages the volatile in-worker MEMFS.  The typical import flow is:
/// <list type="number">
///   <item>Write the source file to OPFS via <see cref="WriteAsync"/>.</item>
///   <item>Write the same file to MEMFS via <see cref="FfmpegService.WriteFileAsync"/>
///         so ffmpeg can process it immediately.</item>
///   <item>Store the extension on the clip's <c>OpfsExt</c> property.</item>
/// </list>
/// On project open, <see cref="ReadAsJSFileAsync"/> retrieves the persisted file
/// and passes it back to <see cref="FfmpegService.WriteFileAsync"/> to repopulate MEMFS.
/// </summary>
public sealed class OPFSService : IAsyncDisposable
{
    private readonly IJSRuntime        _js;
    private readonly ErrorLogService   _errorLog;
    private IJSObjectReference?        _module;

    private const string ModuleUrl = "js/opfsInterop.js";

    public bool IsAvailable { get; private set; }

    public OPFSService(IJSRuntime js, ErrorLogService errorLog)
    {
        _js = js;
        _errorLog = errorLog;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Lazy-loads the JS module and checks browser OPFS support.
    /// Safe to call multiple times; only initialises once.
    /// </summary>
    private Task? _init;

    public Task EnsureInitAsync() => _init ??= InitOnceAsync();

    /// <summary>
    /// Loads the module and asks whether storage is usable, exactly once.
    /// </summary>
    /// <remarks>
    /// The guard used to be <c>if (_module is not null) return;</c>, set before the availability
    /// answer came back. Two callers arriving together — which is now the ordinary case, since both
    /// the media panel and the startup storage check ask — meant the second returned immediately
    /// with <see cref="IsAvailable"/> still false, and the editor announced that this browser
    /// cannot keep your media on a browser that plainly can. Caching the task rather than the
    /// module makes the second caller wait for the first's answer instead of racing past it.
    /// </remarks>
    private async Task InitOnceAsync()
    {
        try
        {
            _module     = await _js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModuleUrl);
            IsAvailable = await _module.InvokeAsync<bool>("opfsIsAvailable");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            _errorLog.Log("OPFSService.EnsureInitAsync", ex);
        }
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Write a browser <c>File</c> object (from file picker) to OPFS.</summary>
    public async Task WriteAsync(Guid clipId, string ext, IJSObjectReference fileRef)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return;
        try { await _module.InvokeVoidAsync("opfsWrite", clipId.ToString(), ext, fileRef); }
        catch (Exception ex) { _errorLog.Log("OPFSService.WriteAsync", $"OPFS write failed for {clipId}{ext} (non-fatal — source may not survive a reload): {ex.Message}", ex.ToString()); }
    }

    /// <summary>Write raw bytes (e.g. downloaded from a web API) to OPFS.</summary>
    /// <summary>
    /// Streams a URL straight into storage, without the bytes passing through .NET.
    /// </summary>
    /// <returns>Bytes written, or -1 when it could not be done — the caller falls back.</returns>
    /// <remarks>
    /// The point is what it avoids under Blazor Server: fetching the file into the server's
    /// memory, copying it again, and shipping it over the circuit (2026-09-05 audit, site-2).
    /// </remarks>
    public async Task<long> DownloadToClipAsync(
        string url, Guid clipId, string ext,
        DotNetObjectReference<object>? progressTarget = null, string? progressMethod = null)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return -1;

        try
        {
            var result = await _module.InvokeAsync<DownloadResult>(
                "opfsDownloadToClip", url, clipId.ToString(), ext, progressTarget, progressMethod);

            if (result.Error is not null)
                _errorLog.Log("OPFSService.DownloadToClipAsync", result.Error);

            return result.Bytes;
        }
        catch (Exception ex)
        {
            _errorLog.Log("OPFSService.DownloadToClipAsync", ex);
            return -1;
        }
    }

    /// <summary>What the browser's own streaming download reported.</summary>
    private sealed record DownloadResult(long Bytes, string? Error);

    public async Task WriteFromBytesAsync(Guid clipId, string ext, byte[] bytes)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return;
        try { await _module.InvokeVoidAsync("opfsWriteBytes", clipId.ToString(), ext, bytes); }
        catch (Exception ex) { _errorLog.Log("OPFSService.WriteFromBytesAsync", $"OPFS write failed for {clipId}{ext} (non-fatal — source may not survive a reload): {ex.Message}", ex.ToString()); }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when a clip file exists in OPFS.
    /// Always returns <c>false</c> when OPFS is unavailable.
    /// </summary>
    public async Task<bool> ExistsAsync(Guid clipId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return false;
        try { return await _module.InvokeAsync<bool>("opfsExists", clipId.ToString(), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.ExistsAsync", $"OPFS existence check failed for {clipId}{ext}: {ex.Message}", ex.ToString()); return false; }
    }

    /// <summary>
    /// How much of the browser's storage this site is using, and how much it may use.
    /// </summary>
    /// <remarks>
    /// Nothing read this. Every import writes a copy of the file into that storage, nothing ever
    /// freed one, and the first anybody knew about the quota was a save that quietly failed
    /// (2026-09-05 audit, media-2). Both figures are null where the browser declines to say.
    /// </remarks>
    public async Task<(long? Usage, long? Quota)> EstimateAsync()
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return (null, null);

        try
        {
            var estimate = await _module.InvokeAsync<StorageEstimate>("opfsEstimate");
            return (estimate.Usage, estimate.Quota);
        }
        catch (Exception ex)
        {
            _errorLog.Log("OPFSService.EstimateAsync", ex);
            return (null, null);
        }
    }

    /// <summary>
    /// Lists all files stored in the OPFS bv-clips/ directory.
    /// Returns an empty list when OPFS is unavailable or the directory is empty.
    /// </summary>
    public async Task<IReadOnlyList<OpfsClipEntry>> ListClipsAsync(CancellationToken ct = default)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return [];
        try
        {
            return await _module.InvokeAsync<OpfsClipEntry[]>("opfsListClips") ?? [];
        }
        catch (Exception ex) { _errorLog.Log("OPFSService.ListClipsAsync", ex); return []; }
    }

    /// <summary>
    /// Reads a clip file from OPFS and returns a JS <c>File</c> reference that can be
    /// passed directly to <see cref="FfmpegService.WriteFileAsync"/>.
    /// 
    /// Returns <c>null</c> if the file does not exist or OPFS is unavailable.
    /// </summary>
    public async Task<IJSObjectReference?> ReadAsJSFileAsync(Guid clipId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return null;
        try { return await _module.InvokeAsync<IJSObjectReference>("opfsReadAsFile", clipId.ToString(), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.ReadAsJSFileAsync", $"OPFS read failed for {clipId}{ext}: {ex.Message}", ex.ToString()); return null; }
    }

    /// <summary>
    /// Reads a clip file from OPFS as a UTF-8 string.
    /// Intended for text-based assets such as SVG source files.
    /// Returns <c>null</c> if the file does not exist or OPFS is unavailable.
    /// </summary>
    public async Task<string?> ReadAsTextAsync(Guid clipId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return null;
        try { return await _module.InvokeAsync<string>("opfsReadAsText", clipId.ToString(), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.ReadAsTextAsync", $"OPFS read failed for {clipId}{ext}: {ex.Message}", ex.ToString()); return null; }
    }

    /// <summary>
    /// Reads a clip file from OPFS and returns a browser <c>blob:</c> URL suitable for an
    /// <c>&lt;img src&gt;</c> or similar. Call <see cref="RevokeBlobUrlAsync"/> once the URL is no
    /// longer displayed, to avoid leaking memory (blob URLs otherwise stay alive until page unload).
    /// Returns <c>null</c> if the file does not exist or OPFS is unavailable.
    /// </summary>
    public async Task<string?> ReadAsBlobUrlAsync(Guid clipId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return null;
        try { return await _module.InvokeAsync<string>("opfsReadAsBlobUrl", clipId.ToString(), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.ReadAsBlobUrlAsync", $"OPFS blob-URL read failed for {clipId}{ext}: {ex.Message}", ex.ToString()); return null; }
    }

    /// <summary>Revokes a blob: URL previously returned by <see cref="ReadAsBlobUrlAsync"/>.</summary>
    public async Task RevokeBlobUrlAsync(string url)
    {
        if (_module is null) return;
        try { await _module.InvokeVoidAsync("opfsRevokeBlobUrl", url); }
        catch (Exception ex) { _errorLog.Log("OPFSService.RevokeBlobUrlAsync", $"revoke failed for {url} (non-fatal): {ex.Message}", ex.ToString()); }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a clip file from OPFS. Silent no-op if the file does not exist.
    /// </summary>
    public async Task DeleteAsync(Guid clipId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return;
        try { await _module.InvokeVoidAsync("opfsDelete", clipId.ToString(), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.DeleteAsync", $"OPFS delete failed for {clipId}{ext} (non-fatal): {ex.Message}", ex.ToString()); }
    }

    // ── Exports (item #38 phase D) ───────────────────────────────────────────────
    //
    // A separate OPFS area (bv-exports/) from the bv-clips/ methods above — finished export
    // output, keyed by an ExportJob's own Guid + real output extension, not a source clip. Written
    // by FfmpegService.ExportToOpfsAsync (entirely JS-side); these two methods only cover reading
    // it back out (download / full-quality preview) and deleting it once done — see
    // README-phase-119.md for why no write method lives here (the write never touches .NET) and
    // why no retention policy exists yet.

    /// <summary>Reads an export file from OPFS and returns a <c>blob:</c> URL, or <c>null</c> if
    /// OPFS is unavailable or the export doesn't exist. Caller must revoke it via
    /// <see cref="RevokeBlobUrlAsync"/> once done (shared with the bv-clips/ path above).</summary>
    public async Task<string?> ReadExportAsBlobUrlAsync(Guid exportId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return null;
        try { return await _module.InvokeAsync<string>("opfsExportsReadAsBlobUrl", exportId.ToString("N"), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.ReadExportAsBlobUrlAsync", $"OPFS export blob-URL read failed for {exportId}{ext}: {ex.Message}", ex.ToString()); return null; }
    }

    /// <summary>Deletes an export file from OPFS. Silent no-op if it does not exist.</summary>
    public async Task DeleteExportAsync(Guid exportId, string ext)
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return;
        try { await _module.InvokeVoidAsync("opfsExportsDelete", exportId.ToString("N"), ext); }
        catch (Exception ex) { _errorLog.Log("OPFSService.DeleteExportAsync", $"OPFS export delete failed for {exportId}{ext} (non-fatal): {ex.Message}", ex.ToString()); }
    }

    // ── Quota ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns storage quota information, or <c>null</c> if the Quota API is unavailable.
    /// </summary>
    public async Task<OPFSQuota?> GetQuotaAsync()
    {
        await EnsureInitAsync();
        if (!IsAvailable || _module is null) return null;
        try { return await _module.InvokeAsync<OPFSQuota?>("opfsGetQuota"); }
        catch (Exception ex) { _errorLog.Log("OPFSService.GetQuotaAsync", ex); return null; }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}

/// <summary>Browser storage quota snapshot.</summary>
public sealed record OPFSQuota(long UsedBytes, long TotalBytes)
{
    public double PercentUsed   => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
    public string FormattedUsed  => FormatBytes(UsedBytes);
    public string FormattedTotal => FormatBytes(TotalBytes);

    private static string FormatBytes(long b) => b switch
    {
        < 1_024             => $"{b} B",
        < 1_048_576         => $"{b / 1_024.0:F1} KB",
        < 1_073_741_824     => $"{b / 1_048_576.0:F1} MB",
        _                   => $"{b / 1_073_741_824.0:F1} GB",
    };
}

/// <summary>An entry returned by <see cref="OPFSService.ListClipsAsync"/>.</summary>
/// <summary>What the browser reports about its own storage. Either figure may be absent.</summary>
public sealed record StorageEstimate(long? Usage, long? Quota);

public sealed record OpfsClipEntry(string ClipId, string Ext, long SizeBytes)
{
    /// <summary>The OPFS file name: <c>{ClipId}{Ext}</c>.</summary>
    public string FileName => $"{ClipId}{Ext}";
}
