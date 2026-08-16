using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Curates the sitewide clipart catalog. SuperAdmin only — one library shared by every group is
/// exactly the kind of thing no single group should be able to edit.
/// </summary>
[ApiController]
[Authorize(Roles = RoleNames.SuperAdmin)]
[Route("api/admin/video-assets")]
public sealed class AdminVideoAssetController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;

    public AdminVideoAssetController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IFileStorageService fileStorage,
        IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
    }

    /// <summary>Every asset, active or retired, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VideoAssetAdminRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var assets = await db.VideoAssets.AsNoTracking()
            .OrderBy(a => a.SortOrder).ThenByDescending(a => a.DateCreated)
            .ToListAsync(ct);
        return Ok(assets.Select(ToRecord));
    }

    /// <summary>Publishes an already-uploaded file into the catalog.</summary>
    [HttpPost]
    public async Task<ActionResult<VideoAssetAdminRecord>> Create(
        [FromBody] CreateVideoAssetRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.UploadFileId, ct);
        if (file is null) return NotFound("Upload file not found.");

        // The format is derived from the file, not taken from the caller: the editor picks its
        // decoder from this, so a mislabelled asset fails at render time in the browser rather
        // than here where it can be refused.
        var format = DetectFormat(file.FileName, file.ContentType);
        if (format is null)
            return BadRequest($"'{Path.GetExtension(file.FileName)}' isn't a supported asset format. Use SVG, PNG, WebP, AVIF, GIF or Lottie JSON.");

        var (hash, size) = await HashAsync(file, ct);
        if (hash is null) return BadRequest("That file has no readable content.");

        var asset = new VideoAsset
        {
            Id                    = Guid.NewGuid(),
            Name                  = request.Name.Trim(),
            Description           = request.Description?.Trim(),
            Category              = request.Category?.Trim(),
            Tags                  = request.Tags?.Trim(),
            Type                  = request.Type,
            Format                = format.Value,
            UploadFileId          = request.UploadFileId,
            ThumbnailUploadFileId = request.ThumbnailUploadFileId,
            ContentHash           = hash,
            FileSizeBytes         = size,
            NativeWidth           = request.NativeWidth,
            NativeHeight          = request.NativeHeight,
            // SVG is the only format the editor can meaningfully recolor or animate per-element.
            AllowRecolor          = format == VideoAssetFormat.Svg,
            AllowControlPoints    = format == VideoAssetFormat.Svg,
            SortOrder             = request.SortOrder,
            IsActive              = true,
            DateCreated           = DateTime.UtcNow,
            CreatedByAppUserId    = userId,
        };
        db.VideoAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(
            nameof(VideoAsset), asset.Id, asset, userId, AppSources.WebApi));

        return Ok(ToRecord(asset));
    }

    /// <summary>Edits catalog metadata. The binary and its hash are not changed here.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VideoAssetAdminRecord>> Update(
        Guid id, [FromBody] UpdateVideoAssetRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var asset = await db.VideoAssets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();

        var before = ToRecord(asset);

        asset.Name                  = request.Name.Trim();
        asset.Description           = request.Description?.Trim();
        asset.Category              = request.Category?.Trim();
        asset.Tags                  = request.Tags?.Trim();
        asset.Type                  = request.Type;
        asset.IsActive              = request.IsActive;
        asset.ThumbnailUploadFileId = request.ThumbnailUploadFileId;
        asset.NativeWidth           = request.NativeWidth;
        asset.NativeHeight          = request.NativeHeight;
        asset.SortOrder             = request.SortOrder;
        asset.DateUpdated           = DateTime.UtcNow;
        asset.UpdatedByAppUserId    = userId;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(VideoAsset), asset.Id, before, ToRecord(asset), userId, AppSources.WebApi));

        return Ok(ToRecord(asset));
    }

    /// <summary>
    /// Retires an asset. Deliberately not a delete: projects reference assets by id, and removing
    /// the row would break every project already using it. Retired assets leave the catalog but
    /// stay downloadable.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var asset = await db.VideoAssets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();

        var before = ToRecord(asset);
        asset.IsActive           = false;
        asset.DateUpdated        = DateTime.UtcNow;
        asset.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(VideoAsset), asset.Id, before, ToRecord(asset), userId, AppSources.WebApi));
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 of the binary plus its length. This becomes the catalog's <c>Version</c>, which is
    /// how the editor decides a cached copy is stale — so it has to be of the actual bytes, not
    /// of a timestamp that changes when nothing did.
    /// </summary>
    private async Task<(string? Hash, long Size)> HashAsync(UploadFile file, CancellationToken ct)
    {
        // Hashed incrementally off the stored file. Reading a whole video into a byte[] just to
        // digest it costs as much memory as the asset is large, and SHA256 never needs more than
        // its block at a time.
        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            await using var stream = await _fileStorage.OpenReadAsync(file.StoragePath, ct);
            var hash = await SHA256.HashDataAsync(stream, ct);
            return (Convert.ToHexString(hash).ToLowerInvariant(), stream.Length);
        }

        if (file.FileData is not null)
            return (Convert.ToHexString(SHA256.HashData(file.FileData)).ToLowerInvariant(), file.FileData.LongLength);

        return (null, 0);
    }

    /// <summary>
    /// Maps a file to a catalog format, preferring the extension and falling back to content type.
    /// Returns null for anything the editor can't render.
    /// </summary>
    internal static VideoAssetFormat? DetectFormat(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var byExtension = ext switch
        {
            ".svg"  => VideoAssetFormat.Svg,
            ".png"  => VideoAssetFormat.Png,
            ".webp" => VideoAssetFormat.WebP,
            ".avif" => VideoAssetFormat.Avif,
            ".gif"  => VideoAssetFormat.Gif,
            ".json" => VideoAssetFormat.Lottie,
            _       => (VideoAssetFormat?)null,
        };
        if (byExtension is not null) return byExtension;

        var type = contentType?.ToLowerInvariant() ?? "";
        if (type.Contains("svg"))  return VideoAssetFormat.Svg;
        if (type.Contains("png"))  return VideoAssetFormat.Png;
        if (type.Contains("webp")) return VideoAssetFormat.WebP;
        if (type.Contains("avif")) return VideoAssetFormat.Avif;
        if (type.Contains("gif"))  return VideoAssetFormat.Gif;
        return null;
    }

    private static VideoAssetAdminRecord ToRecord(VideoAsset a) => new()
    {
        Id = a.Id, Name = a.Name, Description = a.Description, Category = a.Category,
        Tags = a.Tags, Type = a.Type, Format = a.Format,
        UploadFileId = a.UploadFileId, ThumbnailUploadFileId = a.ThumbnailUploadFileId,
        ContentHash = a.ContentHash, FileSizeBytes = a.FileSizeBytes,
        NativeWidth = a.NativeWidth, NativeHeight = a.NativeHeight,
        IsActive = a.IsActive, SortOrder = a.SortOrder, DateCreated = a.DateCreated,
    };
}
