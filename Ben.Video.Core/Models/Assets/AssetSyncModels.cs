namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// Result returned by <see cref="Ben.Video.Editor.Services.VideoAssetCatalogService.SyncAsync"/>.
/// Describes what changed on the server relative to the locally cached catalog.
/// </summary>
public sealed record AssetSyncResult
{
    /// <summary>Assets present on the server that were not in the local cache.</summary>
    public IReadOnlyList<VideoAssetCatalogItem> Added { get; init; } = [];

    /// <summary>
    /// Assets whose server <see cref="VideoAssetCatalogItem.Version"/> differs
    /// from the locally stored version. The locally cached file (if any) should
    /// be re-downloaded.
    /// </summary>
    public IReadOnlyList<VideoAssetCatalogItem> Updated { get; init; } = [];

    /// <summary>
    /// Asset ids that existed in the local cache but are no longer returned by
    /// the server. OPFS copies are NOT deleted — projects that reference them
    /// can still render using the cached file.
    /// </summary>
    public IReadOnlyList<string> Removed { get; init; } = [];

    /// <summary>Number of assets that matched (version unchanged).</summary>
    public int Unchanged { get; init; }

    /// <summary>UTC time the sync completed.</summary>
    public DateTimeOffset SyncedAt { get; init; }

    /// <summary>
    /// True when the server's watermark config version differs from the
    /// locally cached watermark version, meaning the watermark file should
    /// be re-downloaded before the next export.
    /// </summary>
    public bool WatermarkChanged { get; init; }

    /// <summary>
    /// True when the sync could not reach the server.
    /// Catalog remains at its last-known state; exports using cached files still work.
    /// </summary>
    public bool IsOffline { get; init; }

    /// <summary>Error message when <see cref="IsOffline"/> is true, for diagnostic display.</summary>
    public string? OfflineReason { get; init; }

    /// <summary>Convenience — true when any assets changed or were added/removed.</summary>
    public bool HasChanges => Added.Count > 0 || Updated.Count > 0 || Removed.Count > 0 || WatermarkChanged;
}

/// <summary>
/// One entry in the local catalog persisted to localStorage.
/// Wraps a <see cref="VideoAssetCatalogItem"/> with local availability state.
/// </summary>
public sealed record LocalAssetEntry
{
    /// <summary>The full catalog item as last received from the server.</summary>
    public VideoAssetCatalogItem Item { get; init; } = null!;

    /// <summary>
    /// The <see cref="VideoAssetCatalogItem.Version"/> at the time the full
    /// asset file was last downloaded to OPFS. Empty string = not yet downloaded.
    /// </summary>
    public string LocalFileVersion { get; init; } = string.Empty;

    /// <summary>
    /// True when the full asset file is present in OPFS and matches <see cref="LocalFileVersion"/>.
    /// </summary>
    public bool IsLocalAvailable { get; init; }

    /// <summary>UTC time the full asset file was last downloaded. Null = never downloaded.</summary>
    public DateTimeOffset? LastDownloadedAt { get; init; }

    /// <summary>
    /// True when the server has removed this asset but the local OPFS copy has been retained
    /// so existing projects can still render.
    /// </summary>
    public bool IsServerRemoved { get; init; }
}
