using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// SuperAdmin-only endpoints for viewing extracted file metadata and occurrence IP logs.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminFileMetadataController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminFileMetadataController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Returns extracted metadata for a specific uploaded file.</summary>
    [HttpGet("files/{fileId:guid}/metadata")]
    public async Task<ActionResult<FileMetadataResponse>> GetFileMetadata(Guid fileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var meta = await db.UploadFileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UploadFileId == fileId, ct);
        if (meta is null) return NotFound("No metadata extracted for this file.");

        return Ok(new FileMetadataResponse(
            meta.Id, meta.UploadFileId, meta.MediaKind,
            meta.DurationSeconds, meta.SampleRateHz, meta.BitRateKbps, meta.Channels, meta.AudioCodec,
            meta.WidthPixels, meta.HeightPixels,
            meta.CapturedAtUtc, meta.GpsLatitude, meta.GpsLongitude, meta.GpsAltitudeMeters,
            meta.CameraManufacturer, meta.CameraModel,
            meta.RawMetadataJson, meta.ExtractedAtUtc));
    }

    /// <summary>Returns occurrence entries with logged IP addresses for a case. SuperAdmin only.</summary>
    [HttpGet("cases/{caseId:guid}/occurrence-ips")]
    public async Task<ActionResult<IEnumerable<OccurrenceIpRecord>>> GetOccurrenceIps(Guid caseId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var entries = await db.CaseTimelineEntries.AsNoTracking()
            .Where(e => e.CaseId == caseId && e.IpAddress != null)
            .OrderByDescending(e => e.DateCreated)
            .Select(e => new OccurrenceIpRecord(e.Id, e.EntryType.ToString(), e.DateCreated, e.IpAddress!))
            .ToListAsync(ct);
        return Ok(entries);
    }
}

public sealed record FileMetadataResponse(
    Guid      Id,
    Guid      UploadFileId,
    string    MediaKind,
    double?   DurationSeconds,
    int?      SampleRateHz,
    int?      BitRateKbps,
    int?      Channels,
    string?   AudioCodec,
    int?      WidthPixels,
    int?      HeightPixels,
    DateTime? CapturedAtUtc,
    double?   GpsLatitude,
    double?   GpsLongitude,
    double?   GpsAltitudeMeters,
    string?   CameraManufacturer,
    string?   CameraModel,
    string?   RawMetadataJson,
    DateTime  ExtractedAtUtc);

public sealed record OccurrenceIpRecord(
    Guid     EntryId,
    string   EntryType,
    DateTime DateCreated,
    string   IpAddress);
