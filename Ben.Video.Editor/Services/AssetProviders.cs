using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Asset provider that surfaces files the user has already imported into OPFS
/// (<c>bv-clips/</c>) as browseable <see cref="VideoAssetCatalogItem"/> entries.
///
/// <para>These are always <see cref="AssetSource.LocalOpfs"/>, always available offline,
/// and shown in the browser under "My Imported Files".</para>
/// </summary>
public sealed class LocalOpfsAssetProvider : IAssetProvider
{
    private readonly OPFSService _opfs;

    public string ProviderName => "My Imported Files";
    public AssetSource Source   => AssetSource.LocalOpfs;

    /// <summary>Always enabled — local files exist regardless of server connectivity.</summary>
    public bool IsEnabled => true;

    public LocalOpfsAssetProvider(OPFSService opfs)
    {
        _opfs = opfs;
    }

    /// <summary>Extensions this provider can actually offer as artwork.</summary>
    /// <remarks>
    /// Storage holds every clip ever imported — a person's whole footage library sits in the same
    /// directory. Listing all of it made "My Imported Files" offer .mp4s and .m4as as clip art,
    /// each labelled PNG because that is what the format guess falls back to, and each one drawing
    /// nothing when placed (2026-09-05 audit, callouts-6).
    /// </remarks>
    private static readonly HashSet<string> ArtworkExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".svg", ".png", ".webp", ".avif", ".gif", ".jpg", ".jpeg" };

    public async Task<IReadOnlyList<VideoAssetCatalogItem>> GetAssetsAsync(CancellationToken ct = default)
    {
        var files = await _opfs.ListClipsAsync(ct);

        return files
            .Where(f => ArtworkExtensions.Contains(f.Ext))
            .Select(f => new VideoAssetCatalogItem
        {
            Id               = f.ClipId,
            Name             = f.FileName,
            Source           = AssetSource.LocalOpfs,
            Type             = GuessType(f.Ext),
            Format           = GuessFormat(f.Ext),
            ThumbnailUrl     = string.Empty,   // thumbnails generated on first view in AssetBrowser
            Version          = f.ClipId,       // local files: version = stable id
            FileSizeBytes    = f.SizeBytes,
            IsLocalAvailable = true,
            Settings         = new VideoAssetSettings
            {
                AllowResize   = true,
                AllowOpacity  = true,
                AllowRotation = true,
                AllowMotion   = true,
                AllowEasing   = true,
                FlattenOnExport = true,
            },
            }).ToList();
    }

    /// <summary>Local files are already in OPFS — nothing to download.</summary>
    public Task EnsureLocalAsync(string assetId, IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public string? GetLocalPath(string assetId, VideoAssetFormat format)
    {
        if (!Guid.TryParse(assetId, out var guid)) return null;
        return $"bv-clips/{guid}.{FormatExt(format)}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VideoAssetType GuessType(string ext) => ext.ToLowerInvariant() switch
    {
        ".svg"              => VideoAssetType.Clipart,
        ".avif" or ".webp"
            or ".png"       => VideoAssetType.Clipart,
        _                   => VideoAssetType.Clipart,
    };

    private static VideoAssetFormat GuessFormat(string ext) => ext.ToLowerInvariant() switch
    {
        ".svg"  => VideoAssetFormat.Svg,
        ".avif" => VideoAssetFormat.Avif,
        ".webp" => VideoAssetFormat.WebP,
        ".gif"  => VideoAssetFormat.Gif,
        _       => VideoAssetFormat.Png,
    };

    private static string FormatExt(VideoAssetFormat f) => f switch
    {
        VideoAssetFormat.Svg    => "svg",
        VideoAssetFormat.Avif   => "avif",
        VideoAssetFormat.WebP   => "webp",
        VideoAssetFormat.Gif    => "gif",
        VideoAssetFormat.Lottie => "json",
        _                       => "png",
    };
}

/// <summary>
/// Asset provider that wraps the existing media-library API (the user's personal
/// account files on the Ben server) and exposes them as <see cref="AssetSource.AccountLibrary"/>
/// entries in the unified asset browser.
/// </summary>
public sealed class AccountLibraryAssetProvider : IAssetProvider
{
    private readonly IMediaLibraryProvider _inner;
    private readonly OPFSService _opfs;
    private readonly IOptions<VideoEditorOptions> _options;

    public string ProviderName => "My Account Library";
    public AssetSource Source   => AssetSource.AccountLibrary;

    public bool IsEnabled =>
        _options.Value.MediaLibrary &&
        !string.IsNullOrEmpty(_options.Value.MediaLibraryBaseUrl);

    public AccountLibraryAssetProvider(
        IMediaLibraryProvider inner,
        OPFSService opfs,
        IOptions<VideoEditorOptions> options)
    {
        _inner   = inner;
        _opfs    = opfs;
        _options = options;
    }

    public async Task<IReadOnlyList<VideoAssetCatalogItem>> GetAssetsAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return [];

        var items = await _inner.GetFilesAsync(cancellationToken: ct);
        return items.Select(m => new VideoAssetCatalogItem
        {
            Id            = m.Id.ToString(),
            Name          = m.FileName ?? m.Id.ToString(),
            Source        = AssetSource.AccountLibrary,
            Type          = VideoAssetType.Clipart,
            Format        = GuessFormat(System.IO.Path.GetExtension(m.FileName ?? string.Empty)),
            ThumbnailUrl  = string.Empty,
            Version       = m.DateCreated.Ticks.ToString(),
            FileSizeBytes = m.FileSize,
            IsLocalAvailable = false,
            Settings      = new VideoAssetSettings
            {
                AllowResize   = true,
                AllowOpacity  = true,
                AllowRotation = true,
                AllowMotion   = true,
                AllowEasing   = true,
                FlattenOnExport = true,
            },
        }).ToList();
    }

    public async Task EnsureLocalAsync(string assetId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(assetId, out var guid)) return;
        // Download bytes via the existing media library provider
        var bytes = await _inner.DownloadFileAsync(guid, cancellationToken: ct);
        // Store in OPFS using the existing WriteFromBytesAsync
        var format = VideoAssetFormat.Png; // best-effort; full type unknown without re-fetch
        var ext    = FormatExt(format);
        await _opfs.WriteFromBytesAsync(guid, ext, bytes);
    }

    public string? GetLocalPath(string assetId, VideoAssetFormat format)
    {
        // OPFSService.WriteFromBytesAsync stores under bv-clips/{guid}.{ext}
        if (!Guid.TryParse(assetId, out _)) return null;
        return $"bv-clips/{assetId}.{FormatExt(format)}";
    }

    private static VideoAssetFormat GuessFormat(string? ext) => (ext ?? string.Empty).ToLowerInvariant() switch
    {
        ".svg"  => VideoAssetFormat.Svg,
        ".avif" => VideoAssetFormat.Avif,
        ".webp" => VideoAssetFormat.WebP,
        ".gif"  => VideoAssetFormat.Gif,
        _       => VideoAssetFormat.Png,
    };

    private static string FormatExt(VideoAssetFormat f) => f switch
    {
        VideoAssetFormat.Svg   => "svg",
        VideoAssetFormat.Avif  => "avif",
        VideoAssetFormat.WebP  => "webp",
        VideoAssetFormat.Gif   => "gif",
        VideoAssetFormat.Lottie => "json",
        _                      => "png",
    };
}

/// <summary>
/// Asset provider for the shared Ben app clipart/callout/shape catalog.
/// Fetches from <c>GET /api/video-assets</c>, persists the catalog locally,
/// and detects version changes on sync.
/// </summary>
public sealed class SharedCatalogAssetProvider : IAssetProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OPFSService _opfs;
    private readonly IOptions<VideoEditorOptions> _options;

    // In-memory cache of the last-fetched catalog
    private List<VideoAssetCatalogItem>? _catalog;

    public string ProviderName => "Clipart & Callout Library";
    public AssetSource Source   => AssetSource.SharedCatalog;

    public bool IsEnabled =>
        !string.IsNullOrEmpty(_options.Value.AssetCatalogUrl);

    public SharedCatalogAssetProvider(
        IHttpClientFactory httpFactory,
        OPFSService opfs,
        IOptions<VideoEditorOptions> options)
    {
        _httpFactory = httpFactory;
        _opfs        = opfs;
        _options     = options;
    }

    public async Task<IReadOnlyList<VideoAssetCatalogItem>> GetAssetsAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return [];
        if (_catalog is not null) return _catalog;

        // Attempt server fetch; fall back to empty on failure
        try
        {
            await FetchFromServerAsync(ct);
        }
        catch { /* offline — return whatever is in _catalog */ }

        return _catalog ?? [];
    }

    /// <summary>
    /// Fetches the full catalog from the server, diffs against the cached version list,
    /// and returns what changed. Called by <see cref="VideoAssetCatalogService.SyncAsync"/>.
    /// </summary>
    public async Task<AssetSyncResult> SyncWithServerAsync(CancellationToken ct = default)
    {
        var previous = _catalog?.ToDictionary(i => i.Id) ?? [];

        await FetchFromServerAsync(ct);

        var current = (_catalog ?? []).ToDictionary(i => i.Id);

        var added    = current.Values.Where(i => !previous.ContainsKey(i.Id)).ToList();
        var updated  = current.Values.Where(i =>
            previous.TryGetValue(i.Id, out var old) && old.Version != i.Version).ToList();
        var removed  = previous.Keys.Where(id => !current.ContainsKey(id)).ToList();
        var unchanged = current.Count - added.Count - updated.Count;

        return new AssetSyncResult
        {
            Added     = added,
            Updated   = updated,
            Removed   = removed,
            Unchanged = Math.Max(0, unchanged),
            SyncedAt  = DateTimeOffset.UtcNow,
        };
    }

    public async Task EnsureLocalAsync(string assetId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var item = _catalog?.FirstOrDefault(i => i.Id == assetId);
        if (item is null) return;

        if (!Guid.TryParse(assetId, out var guid)) return;
        var ext = FormatExt(item.Format);
        if (await _opfs.ExistsAsync(guid, ext)) return;

        var baseUrl  = _options.Value.AssetCatalogUrl!.TrimEnd('/');
        var fileUrl  = $"{baseUrl}/api/video-assets/{assetId}/file";
        var http     = _httpFactory.CreateClient(Extensions.ServiceCollectionExtensions.AssetCatalogHttpClientName);
        var response = await http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? item.FileSizeBytes;
        var bytes = await ReadWithProgressAsync(response, total, progress, ct);
        await _opfs.WriteFromBytesAsync(guid, ext, bytes);
    }

    public string? GetLocalPath(string assetId, VideoAssetFormat format)
        => Guid.TryParse(assetId, out _) ? $"bv-assets/{assetId}.{FormatExt(format)}" : null;

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task FetchFromServerAsync(CancellationToken ct)
    {
        var http    = _httpFactory.CreateClient(Extensions.ServiceCollectionExtensions.AssetCatalogHttpClientName);
        var baseUrl = _options.Value.AssetCatalogUrl!.TrimEnd('/');
        var items   = await http.GetFromJsonAsync<List<VideoAssetCatalogItem>>(
            $"{baseUrl}/api/video-assets", ct) ?? [];

        // Stamp source and check local availability
        var stamped = new List<VideoAssetCatalogItem>(items.Count);
        foreach (var item in items)
        {
            var ext     = FormatExt(item.Format);
            if (!Guid.TryParse(item.Id, out var guid))
            {
                stamped.Add(item with { Source = AssetSource.SharedCatalog });
                continue;
            }
            var isLocal = await _opfs.ExistsAsync(guid, ext);
            stamped.Add(item with
            {
                Source           = AssetSource.SharedCatalog,
                IsLocalAvailable = isLocal,
            });
        }

        _catalog = stamped;
    }

    private static async Task<byte[]> ReadWithProgressAsync(
        HttpResponseMessage response,
        long total,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer   = new byte[81920];
        var ms       = new MemoryStream(total > 0 ? (int)total : 81920);
        long read    = 0;
        int  bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await ms.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            read += bytesRead;
            if (total > 0)
                progress?.Report((double)read / total);
        }

        progress?.Report(1.0);
        return ms.ToArray();
    }

    private static string FormatExt(VideoAssetFormat f) => f switch
    {
        VideoAssetFormat.Svg    => "svg",
        VideoAssetFormat.Avif   => "avif",
        VideoAssetFormat.WebP   => "webp",
        VideoAssetFormat.Gif    => "gif",
        VideoAssetFormat.Lottie => "json",
        _                       => "png",
    };
}
