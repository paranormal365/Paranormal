using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Aggregates <see cref="VideoAssetCatalogItem"/> entries from all registered
/// <see cref="IAssetProvider"/> implementations (local OPFS, account media library,
/// and the shared clipart/callout catalog) into a single queryable collection.
///
/// <para>This service is the single point of contact for the asset browser panel.
/// It handles local-cache sync, version-based staleness detection, and watermark config.</para>
/// </summary>
public sealed class VideoAssetCatalogService
{
    private readonly IEnumerable<IAssetProvider> _providers;
    private readonly IOptions<VideoEditorOptions> _options;
    private readonly IHttpClientFactory _httpFactory;

    // In-memory cache of last aggregated result — rebuilt on each sync
    private IReadOnlyList<VideoAssetCatalogItem>? _cached;
    private VideoWatermarkConfig? _watermark;
    private string? _lastWatermarkVersion;

    private const string LocalStorageKey   = "bv-shared-catalog";
    private const string WatermarkCacheKey = "bv-watermark-version";

    public VideoAssetCatalogService(
        IEnumerable<IAssetProvider> providers,
        IOptions<VideoEditorOptions> options,
        IHttpClientFactory httpFactory)
    {
        _providers   = providers;
        _options     = options;
        _httpFactory = httpFactory;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the combined asset list from all enabled providers.
    /// Uses the in-memory cache if available; call <see cref="SyncAsync"/> to refresh.
    /// </summary>
    public async Task<IReadOnlyList<VideoAssetCatalogItem>> GetAllAssetsAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var all = new List<VideoAssetCatalogItem>();
        foreach (var provider in _providers.Where(p => p.IsEnabled))
        {
            var items = await provider.GetAssetsAsync(ct);
            all.AddRange(items);
        }

        _cached = all;
        return _cached;
    }

    /// <summary>
    /// Returns only assets from a specific <see cref="AssetSource"/>.
    /// </summary>
    public async Task<IReadOnlyList<VideoAssetCatalogItem>> GetBySourceAsync(
        AssetSource source, CancellationToken ct = default)
    {
        var all = await GetAllAssetsAsync(ct);
        return all.Where(a => a.Source == source).ToList();
    }

    /// <summary>
    /// Returns assets matching an optional type filter and/or free-text search.
    /// </summary>
    public async Task<IReadOnlyList<VideoAssetCatalogItem>> SearchAsync(
        string? query             = null,
        VideoAssetType? typeFilter = null,
        AssetSource? sourceFilter  = null,
        CancellationToken ct       = default)
    {
        var all = await GetAllAssetsAsync(ct);

        IEnumerable<VideoAssetCatalogItem> results = all;

        if (sourceFilter.HasValue)
            results = results.Where(a => a.Source == sourceFilter.Value);

        if (typeFilter.HasValue)
            results = results.Where(a => a.Type == typeFilter.Value);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            results = results.Where(a =>
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)         ||
                (a.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                a.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return results.ToList();
    }

    /// <summary>
    /// Synchronises all provider caches with their upstream sources.
    /// For the shared catalog: hits <c>GET /api/video-assets</c>, compares versions,
    /// and marks stale entries. For local OPFS and account library: re-fetches current state.
    /// Watermark config is also refreshed.
    /// </summary>
    public async Task<AssetSyncResult> SyncAsync(CancellationToken ct = default)
    {
        _cached = null; // invalidate in-memory cache

        var added    = new List<VideoAssetCatalogItem>();
        var updated  = new List<VideoAssetCatalogItem>();
        var removed  = new List<string>();
        var unchanged = 0;
        var watermarkChanged = false;
        string? offlineReason = null;

        try
        {
            // Sync shared catalog if URL is configured
            if (!string.IsNullOrEmpty(_options.Value.AssetCatalogUrl))
            {
                var sharedProvider = _providers
                    .OfType<SharedCatalogAssetProvider>()
                    .FirstOrDefault();

                if (sharedProvider is not null)
                {
                    var diff = await sharedProvider.SyncWithServerAsync(ct);
                    added.AddRange(diff.Added);
                    updated.AddRange(diff.Updated);
                    removed.AddRange(diff.Removed);
                    unchanged += diff.Unchanged;
                }

                // Sync watermark config
                var wm = await FetchWatermarkConfigAsync(ct);
                if (wm is not null)
                {
                    watermarkChanged = wm.Version != _lastWatermarkVersion;
                    _lastWatermarkVersion = wm.Version;
                    _watermark = wm;
                }
            }
        }
        catch (Exception ex)
        {
            offlineReason = ex.Message;
        }

        // Always re-aggregate so local and account sources are fresh
        await GetAllAssetsAsync(ct);

        return new AssetSyncResult
        {
            Added            = added,
            Updated          = updated,
            Removed          = removed,
            Unchanged        = unchanged,
            SyncedAt         = DateTimeOffset.UtcNow,
            WatermarkChanged = watermarkChanged,
            IsOffline        = offlineReason is not null,
            OfflineReason    = offlineReason,
        };
    }

    /// <summary>
    /// Ensures the full asset file is present in OPFS for the given item.
    /// Delegates to the appropriate provider based on <see cref="VideoAssetCatalogItem.Source"/>.
    /// </summary>
    public async Task EnsureLocalAsync(
        VideoAssetCatalogItem item,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.Source == item.Source && p.IsEnabled);
        if (provider is null) return;
        await provider.EnsureLocalAsync(item.Id, progress, ct);
    }

    /// <summary>
    /// Returns the OPFS local path for the given asset, or null if not applicable.
    /// </summary>
    public string? GetLocalPath(VideoAssetCatalogItem item)
    {
        var provider = _providers.FirstOrDefault(p => p.Source == item.Source && p.IsEnabled);
        return provider?.GetLocalPath(item.Id, item.Format);
    }

    // ── Watermark ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current watermark configuration from the server, or null if
    /// <see cref="VideoEditorOptions.AssetCatalogUrl"/> is not set.
    /// Result is cached in memory for the session; call <see cref="SyncAsync"/> to refresh.
    /// </summary>
    public async Task<VideoWatermarkConfig?> GetWatermarkConfigAsync(CancellationToken ct = default)
    {
        if (_watermark is not null) return _watermark;
        if (string.IsNullOrEmpty(_options.Value.AssetCatalogUrl)) return null;

        _watermark = await FetchWatermarkConfigAsync(ct);
        return _watermark;
    }

    // ── Provider enumeration (for UI section headers) ─────────────────────────

    /// <summary>Returns metadata for each enabled provider — used to render browser section headers.</summary>
    public IReadOnlyList<(string Name, AssetSource Source)> GetEnabledProviders()
        => _providers
            .Where(p => p.IsEnabled)
            .Select(p => (p.ProviderName, p.Source))
            .ToList();

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<VideoWatermarkConfig?> FetchWatermarkConfigAsync(CancellationToken ct)
    {
        try
        {
            var http    = _httpFactory.CreateClient(Extensions.ServiceCollectionExtensions.AssetCatalogHttpClientName);
            var baseUrl = _options.Value.AssetCatalogUrl!.TrimEnd('/');
            return await http.GetFromJsonAsync<VideoWatermarkConfig>(
                $"{baseUrl}/api/video-assets/watermark-config", ct);
        }
        catch
        {
            return null;
        }
    }
}
