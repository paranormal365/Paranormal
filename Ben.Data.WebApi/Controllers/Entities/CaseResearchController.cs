using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/research")]
[Authorize]
public sealed class CaseResearchController : BenControllerBase
{
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;

    private readonly IMediaIngestService _mediaIngest;
    private readonly IAvMetadataStripper _avStripper;

    public CaseResearchController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage,
        IMediaIngestService mediaIngest, IAvMetadataStripper avStripper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _fileStorage = fileStorage; _mediaIngest = mediaIngest;
        _avStripper  = avStripper; _security = security; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseResearchEntryDto>>> GetAll(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var entries = await db.CaseResearchEntries.AsNoTracking()
            .Include(e => e.UploadFile)
            .Where(e => e.CaseId == caseId)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DateCreated)
            .ToListAsync(ct);

        return Ok(entries.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<CaseResearchEntryDto>> Create(
        Guid orgId, Guid caseId, [FromBody] UpsertResearchRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var maxOrder = await db.CaseResearchEntries.Where(e => e.CaseId == caseId).MaxAsync(e => (int?)e.SortOrder, ct) ?? 0;
        var entry = new CaseResearchEntry
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            ResearchType = request.ResearchType, Title = request.Title.Trim(),
            Body = request.Body?.Trim(), Url = request.Url?.Trim(),
            SortOrder = maxOrder + 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseResearchEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(entry));
    }

    [HttpPost("files")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<CaseResearchEntryDto>> UploadFile(
        Guid orgId, Guid caseId, [FromForm] string title, [FromForm] string? description,
        IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"research/{storedName}");
        // Ben's rule (2026-08-24): strip on ANY upload, keep what came off in the metadata table.
        // The ORIGINAL stays untouched; the derivative is what serve paths return (item 179).
        var uploadFileId = Guid.NewGuid();
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct,
                (await MediaStrippingPolicy.ForOrganizationAsync(db, _avStripper, orgId, ct)).Strips);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        var evidenceTypeId = new Guid("20000000-0000-0000-0000-000000000001"); // Case Evidence upload type
        var uploadFile = new UploadFile
        {
            Id = uploadFileId, UploadFileTypeId = evidenceTypeId, AppUserId = userId,
            FileName = file.FileName, StoredFileName = storedName,
            ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        db.UploadFileMetadata.Add(ingested.Metadata);

        var maxOrder = await db.CaseResearchEntries.Where(e => e.CaseId == caseId).MaxAsync(e => (int?)e.SortOrder, ct) ?? 0;
        var entry = new CaseResearchEntry
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            ResearchType = CaseResearchType.File,
            Title = title.Trim(), Body = description?.Trim(),
            UploadFileId = uploadFile.Id,
            SortOrder = maxOrder + 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseResearchEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        entry.UploadFile = uploadFile;
        return Ok(ToDto(entry));
    }

    [HttpPut("{entryId:guid}")]
    public async Task<ActionResult<CaseResearchEntryDto>> Update(
        Guid orgId, Guid caseId, Guid entryId, [FromBody] UpsertResearchRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var entry = await db.CaseResearchEntries.Include(e => e.UploadFile)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null) return NotFound();

        entry.Title = request.Title.Trim();
        entry.Body  = request.Body?.Trim();
        entry.Url   = request.Url?.Trim();
        entry.DateUpdated = DateTime.UtcNow;
        entry.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(entry));
    }

    [HttpDelete("{entryId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var entry = await db.CaseResearchEntries.Include(e => e.UploadFile)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null) return NotFound();

        var storagePath = entry.UploadFile?.StoragePath;
        db.CaseResearchEntries.Remove(entry);
        if (entry.UploadFile is not null) db.UploadFiles.Remove(entry.UploadFile);
        await db.SaveChangesAsync(ct);
        if (storagePath is not null) await _fileStorage.DeleteAsync(storagePath, ct);

        return NoContent();
    }

    // Item 156 Phase D: bare membership stopped being the rule here — see CaseFileController.
    private async Task<bool> IsOrgMember(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(userId, orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);

    private static CaseResearchEntryDto ToDto(CaseResearchEntry e) => new(
        e.Id, e.CaseId, e.ResearchType, e.Title, e.Body, e.Url,
        e.UploadFile is null ? null : new ResearchFileInfo(e.UploadFile.Id, e.UploadFile.FileName, e.UploadFile.ContentType, e.UploadFile.FileSize),
        e.SortOrder, e.DateCreated);
}

public sealed record UpsertResearchRequest(
    Ben.Data.Common.Enums.CaseResearchType ResearchType,
    string  Title,
    string? Body,
    string? Url);

public sealed record CaseResearchEntryDto(
    Guid                                        Id,
    Guid                                        CaseId,
    Ben.Data.Common.Enums.CaseResearchType      ResearchType,
    string                                      Title,
    string?                                     Body,
    string?                                     Url,
    ResearchFileInfo?                           File,
    int                                         SortOrder,
    DateTime                                    DateCreated);

public sealed record ResearchFileInfo(Guid FileId, string FileName, string ContentType, long FileSize);
