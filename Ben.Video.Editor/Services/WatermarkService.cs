using Ben.Video.Editor.Models.Assets;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Manages downloading and applying the server-controlled export watermark.
///
/// <para>The watermark configuration is fetched from the Ben app WebAPI via
/// <see cref="VideoAssetCatalogService.GetWatermarkConfigAsync"/>. When
/// <see cref="VideoWatermarkConfig.Enabled"/> is true, every export must
/// include the watermark — the user cannot opt out.</para>
///
/// <para>The watermark file is cached in OPFS as <c>bv-watermark.{ext}</c>
/// using the existing <see cref="OPFSService"/> infrastructure. On each sync
/// the version hash is checked; if stale the file is re-downloaded.</para>
/// </summary>
public sealed class WatermarkService
{
    private readonly VideoAssetCatalogService _catalog;
    private readonly OPFSService             _opfs;
    private readonly IHttpClientFactory       _httpFactory;

    private const string OPFSId  = "00000000-0000-0000-0000-000000000001"; // stable synthetic id
    private const string OPFSExt = ".wm";                                  // sentinel extension

    public WatermarkService(
        VideoAssetCatalogService catalog,
        OPFSService              opfs,
        IHttpClientFactory       httpFactory)
    {
        _catalog     = catalog;
        _opfs        = opfs;
        _httpFactory = httpFactory;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current watermark config, or null if the catalog URL is not
    /// configured or the server has not set a watermark.
    /// </summary>
    public Task<VideoWatermarkConfig?> GetConfigAsync(CancellationToken ct = default)
        => _catalog.GetWatermarkConfigAsync(ct);

    /// <summary>
    /// Ensures the watermark file is downloaded to OPFS and up-to-date.
    /// <para>Returns the local OPFS <c>(id, ext)</c> tuple when the file is ready.</para>
    /// <para>Returns <c>null</c> — silently skipping the watermark — when any of:</para>
    /// <list type="bullet">
    ///   <item><see cref="VideoWatermarkConfig.Enabled"/> is false or <see cref="VideoWatermarkConfig.FileUrl"/> is empty</item>
    ///   <item>The file does not exist on the server (404 or network failure)</item>
    ///   <item>The download or OPFS write fails for any reason</item>
    /// </list>
    /// The caller should treat <c>null</c> as "no watermark" and proceed without one.
    /// </summary>
    public async Task<(Guid id, string ext)?> EnsureLocalAsync(
        VideoWatermarkConfig config,
        CancellationToken ct = default)
    {
        if (!config.Enabled || string.IsNullOrEmpty(config.FileUrl))
            return null;

        var id  = new Guid(OPFSId);
        var ext = GetExtension(config.FileUrl);

        // Check if OPFS already has a current copy via a version marker file
        var versionExt = $"{OPFSExt}{GetVersionSuffix(config.Version)}";
        var isUpToDate = await _opfs.ExistsAsync(id, versionExt);
        if (!isUpToDate)
        {
            // Attempt to download — any failure means no watermark this export
            try
            {
                var http  = _httpFactory.CreateClient(Extensions.ServiceCollectionExtensions.AssetCatalogHttpClientName);
                var bytes = await http.GetByteArrayAsync(config.FileUrl, ct);
                await _opfs.WriteFromBytesAsync(id, ext, bytes);
                // Write the version marker so subsequent exports skip the download
                await _opfs.WriteFromBytesAsync(id, versionExt, []);
            }
            catch
            {
                // File not available (not created yet, 404, network error, etc.) — skip watermark silently
                return null;
            }
        }

        return (id, ext);
    }

    /// <summary>
    /// Build the ffmpeg overlay filter that composites the watermark over
    /// the current output stream at the position defined by <paramref name="config"/>.
    /// </summary>
    /// <param name="config">Server-provided watermark settings.</param>
    /// <param name="watermarkMemFsName">MEMFS file name of the watermark image.</param>
    /// <param name="videoWidth">Frame width in pixels.</param>
    /// <param name="videoHeight">Frame height in pixels.</param>
    /// <returns>
    /// A filter_complex string that composites [1:v] (the watermark) over [0:v].
    /// Output label is [out].
    /// </returns>
    public static string BuildOverlayFilter(
        VideoWatermarkConfig config,
        string watermarkMemFsName,
        int videoWidth,
        int videoHeight)
    {
        // Scale watermark to configured fraction of frame width; preserve aspect ratio
        var wmWidth  = (int)(config.ScaleFraction * videoWidth);
        var wmHeight = -1; // -1 = preserve aspect ratio

        var (overlayX, overlayY) = ComputePosition(config, videoWidth, videoHeight, wmWidth);
        var opacity = config.Opacity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        // scale + colorchannelmixer for opacity → [wm], overlay [wm] over [0:v]
        return $"[1:v]scale={wmWidth}:{wmHeight},colorchannelmixer=aa={opacity}[wm];" +
               $"[0:v][wm]overlay={overlayX}:{overlayY}[out]";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (int x, int y) ComputePosition(
        VideoWatermarkConfig config,
        int videoWidth, int videoHeight,
        int wmWidth)
    {
        var mx = config.MarginX;
        var my = config.MarginY;

        // Estimate watermark height for bottom/middle calculations
        // (aspect ratio unknown, approximate 50% of width for a typical logo)
        var estimatedH = wmWidth / 2;

        var x = config.Position switch
        {
            WatermarkPosition.TopLeft    or
            WatermarkPosition.MiddleLeft or
            WatermarkPosition.BottomLeft  => mx,

            WatermarkPosition.TopCenter   or
            WatermarkPosition.Center      or
            WatermarkPosition.BottomCenter => (videoWidth - wmWidth) / 2,

            _ => videoWidth - wmWidth - mx,   // Right-aligned
        };

        var y = config.Position switch
        {
            WatermarkPosition.TopLeft    or
            WatermarkPosition.TopCenter  or
            WatermarkPosition.TopRight    => my,

            WatermarkPosition.MiddleLeft or
            WatermarkPosition.Center     or
            WatermarkPosition.MiddleRight => (videoHeight - estimatedH) / 2,

            _ => videoHeight - estimatedH - my,  // Bottom-aligned
        };

        return (Math.Max(0, x), Math.Max(0, y));
    }

    private static string GetExtension(string url)
    {
        var path = new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(url).AbsolutePath
            : url;
        var dot = path.LastIndexOf('.');
        return dot >= 0 ? path[dot..].ToLowerInvariant() : ".png";
    }

    private static string GetVersionSuffix(string? version)
        => string.IsNullOrEmpty(version) ? "v0" : $"v{version[..Math.Min(8, version.Length)]}";
}
