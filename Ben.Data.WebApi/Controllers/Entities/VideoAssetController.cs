using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The sitewide clipart and callout catalog, served to Ben.Video.Editor.
/// </summary>
/// <remarks>
/// <para><b>Anonymous by design.</b> The editor registers its catalog HttpClient with no auth
/// handler (<c>services.AddHttpClient(AssetCatalogHttpClientName)</c> in Ben.Video's
/// ServiceCollectionExtensions), so requiring a bearer token here would leave the provider
/// permanently and silently disabled. That is acceptable because the catalog is curated stock
/// artwork with nothing personal in it, and it is meant to be available to everyone in every
/// group. The editor's own contract already documents thumbnails as served without auth.</para>
///
/// <para>Only active assets are listed. Retired ones stay reachable by id so a project that
/// already references one still renders — the catalog stops offering them, it doesn't delete them.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/video-assets")]
public sealed class VideoAssetController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IFileStorageService _fileStorage;

    public VideoAssetController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IFileStorageService fileStorage)
    {
        _dbContextFactory = dbContextFactory;
        _fileStorage = fileStorage;
    }

    /// <summary>The full catalog. Returned as a bare JSON array — the editor deserialises a List.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VideoAssetCatalogItemRecord>>> GetCatalog(CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Watermarks are not timeline assets — the export pipeline composites them, and the enum
        // says so. Listing them put "add this to your video" cards in the Assets tab for artwork
        // that is meant to be applied automatically (2026-09-05 audit, callouts-23).
        var assets = await db.VideoAssets.AsNoTracking()
            .Where(a => a.IsActive && a.Type != VideoAssetType.Watermark)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);

        return Ok(assets.Select(ToCatalogItem));
    }

    /// <summary>
    /// Whether exports carry a watermark, and which one.
    /// </summary>
    /// <remarks>
    /// <para>The editor asks for this before every render (<c>WatermarkService.GetConfigAsync</c>,
    /// via <c>ExportService.RunPipelineAsync</c>). Nothing served it, so the request 404'd, the
    /// editor's catch swallowed it and the answer was always "no watermark" — a feature with an
    /// admin upload screen, a client-side compositor and an export phase that could never turn on
    /// (2026-09-05 audit, F16).</para>
    ///
    /// <para>Always 200, because "no watermark configured" is an answer rather than an error, and
    /// a 404 here is indistinguishable from a misrouted request. The active watermark asset with
    /// the lowest sort order wins; there is deliberately no admin screen for choosing between
    /// several, so retiring one is how you switch.</para>
    /// </remarks>
    [HttpGet("watermark-config")]
    public async Task<ActionResult<VideoWatermarkConfigRecord>> GetWatermarkConfig(CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var watermark = await db.VideoAssets.AsNoTracking()
            .Where(a => a.IsActive && a.Type == VideoAssetType.Watermark)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .FirstOrDefaultAsync(ct);

        if (watermark is null)
            return Ok(new VideoWatermarkConfigRecord { Enabled = false });

        return Ok(new VideoWatermarkConfigRecord
        {
            Enabled = true,
            // Absolute for the same reason the catalog's thumbnails are: the editor may be served
            // from another origin and hands this straight to fetch.
            FileUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/video-assets/{watermark.Id}/file",
            Version = watermark.ContentHash,
        });
    }

    /// <summary>The asset binary. The editor caches this in OPFS keyed by the catalog Version.</summary>
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Not filtered on IsActive: a retired asset must still download, or every project that
        // already uses it breaks the moment an admin retires it.
        var asset = await db.VideoAssets.AsNoTracking()
            .Include(a => a.UploadFile)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset?.UploadFile is null) return NotFound();

        return await ServeAsync(asset.UploadFile, ct);
    }

    /// <summary>The thumbnail, falling back to the asset itself when none was set.</summary>
    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> GetThumbnail(Guid id, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var asset = await db.VideoAssets.AsNoTracking()
            .Include(a => a.UploadFile)
            .Include(a => a.ThumbnailUploadFile)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();

        var file = asset.ThumbnailUploadFile ?? asset.UploadFile;
        return file is null ? NotFound() : await ServeAsync(file, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IActionResult> ServeAsync(UploadFile file, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            var stream = await _fileStorage.OpenReadAsync(file.StoragePath, ct);
            return File(stream, file.ContentType);
        }
        return file.FileData is not null ? File(file.FileData, file.ContentType) : NotFound();
    }

    private VideoAssetCatalogItemRecord ToCatalogItem(VideoAsset a) => new()
    {
        Id          = a.Id.ToString(),
        Name        = a.Name,
        Description = a.Description,
        Category    = a.Category,
        Tags        = SplitList(a.Tags),
        Source      = AssetSource.SharedCatalog,
        Type        = a.Type,
        Format      = a.Format,
        // Absolute, because the editor may be hosted somewhere other than this API and drops
        // this straight into an <img src>.
        ThumbnailUrl  = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/video-assets/{a.Id}/thumbnail",
        Version       = a.ContentHash,
        UpdatedAt     = new DateTimeOffset(DateTime.SpecifyKind(a.DateUpdated ?? a.DateCreated, DateTimeKind.Utc)),
        FileSizeBytes = a.FileSizeBytes,
        NativeWidth   = a.NativeWidth,
        NativeHeight  = a.NativeHeight,
        Settings = new VideoAssetSettingsRecord
        {
            AllowRecolor       = a.AllowRecolor,
            AllowResize        = a.AllowResize,
            AllowOpacity       = a.AllowOpacity,
            AllowRotation      = a.AllowRotation,
            AllowEffects       = a.AllowEffects,
            AllowEasing        = a.AllowEasing,
            AllowMotion        = a.AllowMotion,
            AllowControlPoints = a.AllowControlPoints,
            PresetColors       = a.PresetColors is null ? null : SplitList(a.PresetColors),
            MinScale           = a.MinScale,
            MaxScale           = a.MaxScale,
            FlattenOnExport    = a.FlattenOnExport,
        },
    };

    /// <summary>Splits a comma-separated column, dropping blanks so trailing commas are harmless.</summary>
    internal static List<string> SplitList(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
