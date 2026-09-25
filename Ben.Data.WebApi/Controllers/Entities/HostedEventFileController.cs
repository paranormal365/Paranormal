using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The host's side of an event's files (item 235 phase 11).
/// </summary>
/// <remarks>
/// <para><b>Who may add them</b> is whoever may edit the event, or a helper handed <i>the event's
/// files</i> on the staff list — the switch phase 7 put there, now with something behind it.</para>
///
/// <para><b>Every file says who it is for</b>, and a new one starts as <c>Staff</c>: a file that is
/// wrongly private is noticed the first time a guest asks for it, and one that is wrongly public
/// is never un-published.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/files")]
public sealed class HostedEventFileController : OrgCmsControllerBase
{
    /// <summary>The event-files type is the all-extensions evidence type, as the tour gallery uses.</summary>
    private static readonly Guid FileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly Services.Access.HostedEventAccess _access;
    private readonly IMediaIngestService _ingest;
    private readonly IFileStorageService _storage;

    public HostedEventFileController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, IMediaIngestService ingest, IFileStorageService storage)
        : base(dbFactory, mapper, security)
    { _access = access; _ingest = ingest; _storage = storage; }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HostedEventFileRecord>>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageFilesAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        return Ok(await ListAsync(db, eventId, null, ct));
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<ActionResult<IReadOnlyList<HostedEventFileRecord>>> Upload(
        Guid orgId, Guid eventId, IFormFile? file,
        [FromForm] string? folder, [FromForm] string? description, [FromForm] EventFileAudience audience,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageFilesAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        if (file is null || file.Length == 0) return BadRequest("Choose a file to add.");
        if (!Enum.IsDefined(audience)) return BadRequest("Say who the file is for.");

        var limits = await UploadLimitsReader.ReadAsync(db, ct);
        if (file.Length > limits.MaxFileBytes)
            return BadRequest($"That file is larger than the {limits.MaxFileBytes / (1024 * 1024):N0} MB this site accepts.");

        if (await EventStorage.WhyItDoesNotFitAsync(db, eventId, file.Length, ct) is { } full) return BadRequest(full);

        // An SVG is a document that can carry script, and a guest opening one from a venue's page
        // would run whatever it says. Everything else is served as a download, never inline.
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension == ".svg" || file.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            return BadRequest("SVG files can't be added here. Save it as a PDF or a PNG instead.");

        var now = DateTime.UtcNow;
        var uploadId = Guid.NewGuid();
        var storedName = $"{uploadId:N}{extension}";
        var storagePath = _storage.OrgFilePath(orgId, $"events/{eventId:N}/{storedName}");

        // Through the ingest, like every upload: a photo of the fire exits taken on somebody's phone
        // carries where they were standing, and that is stripped before a guest can download it.
        IngestedMedia ingested;
        try
        {
            ingested = await _ingest.IngestAsync(file, storagePath, uploadId, ct);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        db.UploadFiles.Add(new UploadFile
        {
            Id = uploadId, UploadFileTypeId = FileTypeId, OwnerOrganizationId = orgId,
            FileName = ingested.ServedFileName(Path.GetFileName(file.FileName)), StoredFileName = storedName,
            ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = now, CreatedByAppUserId = userId.Value,
        });
        db.UploadFileMetadata.Add(ingested.Metadata);
        db.HostedEventFiles.Add(new HostedEventFile
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, UploadFileId = uploadId,
            Folder = Folder(folder), Description = Trim(description, 500), Audience = audience,
            SortOrder = await db.HostedEventFiles.CountAsync(f => f.HostedEventId == eventId, ct),
            DateCreated = now, CreatedByAppUserId = userId.Value,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await _ingest.DeleteAllAsync(storagePath, CancellationToken.None);
            throw;
        }

        return Ok(await ListAsync(db, eventId, null, ct));
    }

    [HttpPut("{fileId:guid}")]
    public async Task<ActionResult<IReadOnlyList<HostedEventFileRecord>>> Update(
        Guid orgId, Guid eventId, Guid fileId, [FromBody] UpdateHostedEventFileRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageFilesAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!Enum.IsDefined(request.Audience)) return BadRequest("Say who the file is for.");

        var row = await db.HostedEventFiles.FirstOrDefaultAsync(
            f => f.Id == fileId && f.HostedEventId == eventId && f.HostedEvent.OrganizationId == orgId, ct);
        if (row is null) return NotFound();

        row.Folder = Folder(request.Folder);
        row.Description = Trim(request.Description, 500);
        row.Audience = request.Audience;
        row.SortOrder = request.SortOrder;
        row.DateUpdated = DateTime.UtcNow;
        row.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, eventId, null, ct));
    }

    [HttpDelete("{fileId:guid}")]
    public async Task<ActionResult<IReadOnlyList<HostedEventFileRecord>>> Delete(Guid orgId, Guid eventId, Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageFilesAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var row = await db.HostedEventFiles.Include(f => f.UploadFile).FirstOrDefaultAsync(
            f => f.Id == fileId && f.HostedEventId == eventId && f.HostedEvent.OrganizationId == orgId, ct);
        if (row is null) return NotFound();

        var path = row.UploadFile.StoragePath;
        db.HostedEventFiles.Remove(row);
        await db.SaveChangesAsync(ct);

        // The row first, then the bytes: bytes with no row are an orphan a sweep can find; a row
        // with no bytes is a download that fails in front of a guest.
        if (await Services.Admin.UploadFileRows.TryDeleteAsync(db, row.UploadFileId, ct) && path is not null)
            await _ingest.DeleteAllAsync(path, ct);

        return Ok(await ListAsync(db, eventId, null, ct));
    }

    // ── shared with the guest's side ─────────────────────────────────────────

    internal static async Task<IReadOnlyList<HostedEventFileRecord>> ListAsync(
        BenDataContext db, Guid eventId, EventFileAudience? widest, CancellationToken ct)
    {
        var rows = await db.HostedEventFiles.AsNoTracking()
            .Where(f => f.HostedEventId == eventId)
            .OrderBy(f => f.Folder != null).ThenBy(f => f.Folder).ThenBy(f => f.SortOrder).ThenBy(f => f.DateCreated)
            .Select(f => new HostedEventFileRecord(
                f.Id, f.UploadFileId, f.UploadFile.FileName, f.UploadFile.ContentType, f.UploadFile.FileSize,
                f.Folder, f.Description, f.Audience, f.SortOrder, f.DateCreated))
            .ToListAsync(ct);

        return widest is null ? rows : [.. rows.Where(r => EventFileAudiences.Reaches(r.Audience, widest))];
    }

    private static string? Folder(string? value) => Trim(value, 80);

    private static string? Trim(string? value, int max)
        => value?.Trim() is { Length: > 0 } v ? (v.Length > max ? v[..max] : v) : null;
}
