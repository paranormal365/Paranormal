namespace Ben.Video.Editor.Services;

using Ben.Video.Editor.Models.Assets;

/// <summary>
/// Pluggable source of <see cref="VideoAssetCatalogItem"/> entries for the asset browser.
///
/// <para>Three built-in implementations are registered by default:</para>
/// <list type="bullet">
///   <item><see cref="LocalOpfsAssetProvider"/> — OPFS-cached files the user imported themselves</item>
///   <item><see cref="AccountLibraryAssetProvider"/> — user's personal media on the Ben server (MediaLibrary)</item>
///   <item><see cref="SharedCatalogAssetProvider"/> — managed clipart/callout catalog from Ben app</item>
/// </list>
///
/// <para>Additional providers can be registered before calling <c>AddBenVideoEditor()</c>:
/// <code>
/// services.AddScoped&lt;IAssetProvider, MyCustomAssetProvider&gt;();
/// services.AddBenVideoEditor(...);
/// </code>
/// </para>
/// </summary>
public interface IAssetProvider
{
    /// <summary>Human-readable name shown as a section header in the browser (e.g. "My Files").</summary>
    string ProviderName { get; }

    /// <summary>The <see cref="AssetSource"/> value this provider stamps on its items.</summary>
    AssetSource Source { get; }

    /// <summary>
    /// Whether this provider is currently enabled.
    /// Providers backed by an unconfigured URL return false so the browser
    /// hides the section rather than showing an empty list.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Fetch the current list of available assets from this source.
    /// Implementations should be fast on repeated calls (use a local cache where appropriate).
    /// </summary>
    Task<IReadOnlyList<VideoAssetCatalogItem>> GetAssetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensure the full asset file for <paramref name="assetId"/> is present in OPFS.
    /// No-op if already cached at the current version.
    /// </summary>
    /// <param name="assetId">The <see cref="VideoAssetCatalogItem.Id"/> to download.</param>
    /// <param name="progress">Optional 0–1 progress callback.</param>
    Task EnsureLocalAsync(string assetId, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the OPFS path where the full asset file is (or will be) cached,
    /// e.g. <c>"bv-assets/{assetId}.svg"</c>. Returns null if this provider
    /// does not use OPFS (e.g. streams directly).
    /// </summary>
    string? GetLocalPath(string assetId, VideoAssetFormat format);
}
