using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Equipment as a group sees it: the group's own gear, and the personal gear its members have
/// shared with it.
/// </summary>
/// <remarks>
/// <para>Two different authorities, deliberately. <b>Reading</b> — either list — needs only active
/// membership. <b>Changing</b> the group's own gear needs the <see cref="OrganizationSecurityTable.Equipment"/>
/// permission, which also unlocks serial numbers. Members' shared gear is never editable here at
/// all: it belongs to its owner.</para>
///
/// <para>The permission verdict is resolved <i>once per request</i> and passed into the flag
/// computation, never asked per row — the N+1 that <c>OrganizationController</c>'s own comments
/// warn about.</para>
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/equipment")]
[Authorize]
public sealed class OrganizationEquipmentController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;
    private readonly IMediaIngestService _mediaIngest;

    public OrganizationEquipmentController(
        IDbContextFactory<BenDataContext> db,
        IOrganizationSecurityService security,
        IFileStorageService fileStorage,
        IAuditLogService auditLog,
        IMediaIngestService mediaIngest)
    {
        _db          = db;
        _security    = security;
        _fileStorage = fileStorage;
        _auditLog    = auditLog;
        _mediaIngest = mediaIngest;
    }

    private bool IsSuperAdmin() => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

    private Task<bool> IsActiveMemberAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => db.OrganizationUserMemberships.AsNoTracking()
             .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

    private Task<bool> CanManageAsync(Guid userId, Guid orgId, OrganizationSecurityAction action, CancellationToken ct)
        => EquipmentAccess.CanManageOrgEquipmentAsync(_security, userId, orgId, IsSuperAdmin(), action, ct);

    /// <summary>
    /// Whether this caller may see how much interest a piece has attracted.
    /// </summary>
    /// <remarks>
    /// The membership ROLE, deliberately — not the Equipment permission. Ben's audience is
    /// administrators, not whoever a group happened to hand an equipment role to, and those are
    /// different sets of people.
    /// </remarks>
    private async Task<bool> CanSeeCountersAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => IsSuperAdmin()
           || await db.OrganizationUserMemberships.AsNoTracking()
               .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
                           && (m.Role == OrganizationMemberRole.Owner
                               || m.Role == OrganizationMemberRole.Administrator), ct);

    /// <summary>
    /// Personal gear members have shared with this group.
    /// </summary>
    /// <remarks>
    /// Membership is re-checked here rather than trusted from the share row, so a share left behind
    /// by someone who has since left the group grants nothing. Answers 404 for a non-member: whether
    /// a group exists is not something this endpoint should confirm to outsiders.
    /// </remarks>
    [HttpGet("shared")]
    public async Task<ActionResult<IEnumerable<SharedEquipmentItemRecord>>> GetSharedWithOrg(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);
        var isMember = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
        if (!isMember && !isSuperAdmin) return NotFound();

        // A share only counts while its owner is still an active member of this group.
        var items = await db.EquipmentItemShares.AsNoTracking()
            .Where(s => s.OrganizationId == orgId)
            .Join(db.EquipmentItems.AsNoTracking()
                    .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
                    .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
                    .Include(i => i.Photos),
                  s => s.EquipmentItemId, i => i.Id, (s, i) => i)
            .Where(i => !i.IsRetired
                     && i.OwnerAppUserId != null
                     && db.OrganizationUserMemberships.Any(m =>
                            m.OrganizationId == orgId && m.AppUserId == i.OwnerAppUserId && m.IsActive))
            .Select(i => new SharedEquipmentItemRecord(
                i.Id,
                i.OwnerAppUserId!.Value,
                db.AppUsers.Where(u => u.Id == i.OwnerAppUserId).Select(u => u.DisplayName).FirstOrDefault(),
                i.DisplayName,
                i.EquipmentModel.EquipmentBrand.Name,
                i.EquipmentModel.Name,
                i.EquipmentModel.EquipmentCategory.Name,
                i.Notes,
                i.LoanAudience,
                i.IsRetired,
                i.Photos.OrderBy(p => p.SortOrder)
                    .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder, p.ExcludeFromCatalog))
                    .ToList()))
            .ToListAsync(ct);

        return Ok(items.OrderBy(i => i.OwnerDisplayName).ThenBy(i => i.DisplayName));
    }

    // ── The group's own equipment ────────────────────────────────────────────

    /// <summary>
    /// The group's own gear. Any active member can read it; serial numbers appear only for those
    /// who can manage equipment.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OrgEquipmentListRecord>> GetOrgEquipment(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsActiveMemberAsync(db, orgId, userId, ct) && !IsSuperAdmin()) return NotFound();

        // One verdict for the whole list, not one per row.
        var canManage = await CanManageAsync(userId, orgId, OrganizationSecurityAction.Read, ct);
        var canSeeCounters = await CanSeeCountersAsync(db, orgId, userId, ct);

        var items = await db.EquipmentItems.AsNoTracking()
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .Where(i => i.OwningOrganizationId == orgId)
            .OrderBy(i => i.IsRetired).ThenBy(i => i.DisplayName)
            .ToListAsync(ct);

        var holderNames = await HolderNamesAsync(db, items, ct);

        // The verdict rides alongside the list, not inside it: an empty list still has to be able
        // to say "you may add the first piece."
        return Ok(new OrgEquipmentListRecord(
            canManage,
            [.. items.Select(i => ToOrgRecord(i, canManage, holderNames, null, canSeeCounters))]));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EquipmentItemRecord>> GetOrgEquipmentItem(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsActiveMemberAsync(db, orgId, userId, ct) && !IsSuperAdmin()) return NotFound();

        var item = await db.EquipmentItems.AsNoTracking()
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (item is null) return NotFound();

        var canManage = await CanManageAsync(userId, orgId, OrganizationSecurityAction.Read, ct);
        var holderNames = await HolderNamesAsync(db, [item], ct);

        return Ok(ToOrgRecord(item, canManage, holderNames));
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentItemRecord>> CreateOrgEquipment(
        Guid orgId, [FromBody] UpsertOrgEquipmentItemRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Create, ct)) return Forbid();

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) return BadRequest("Display name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentModels.AnyAsync(m => m.Id == request.EquipmentModelId, ct))
            return BadRequest("Equipment model not found.");

        var entity = new EquipmentItem
        {
            Id                     = Guid.NewGuid(),
            // Owned by the group, never a person — the XOR the entity documents.
            OwningOrganizationId   = orgId,
            OwnerAppUserId         = null,
            EquipmentModelId       = request.EquipmentModelId,
            DisplayName            = displayName,
            SerialNumber           = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim(),
            AcquisitionDate        = request.AcquisitionDate,
            Notes                  = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsRetired              = false,
            IncludeInGlobalCatalog = request.IncludeInGlobalCatalog,
            WebsiteUrl             = MyEquipmentController.NormalizeWebsiteUrl(request.WebsiteUrl),
            DateCreated            = DateTime.UtcNow,
            CreatedByAppUserId     = userId,
        };
        db.EquipmentItems.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentItem), entity.Id, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        await db.Entry(entity).Reference(i => i.EquipmentModel).LoadAsync(ct);
        await db.Entry(entity.EquipmentModel).Reference(m => m.EquipmentBrand).LoadAsync(ct);
        await db.Entry(entity.EquipmentModel).Reference(m => m.EquipmentCategory).LoadAsync(ct);

        return Ok(ToOrgRecord(entity, canManage: true, holderNames: []));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EquipmentItemRecord>> UpdateOrgEquipment(
        Guid orgId, Guid id, [FromBody] UpsertOrgEquipmentItemRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) return BadRequest("Display name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var before = await db.EquipmentItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (before is null) return NotFound();

        if (request.EquipmentModelId != before.EquipmentModelId
            && !await db.EquipmentModels.AnyAsync(m => m.Id == request.EquipmentModelId, ct))
            return BadRequest("Equipment model not found.");

        var entity = await db.EquipmentItems
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .FirstAsync(i => i.Id == id, ct);

        entity.EquipmentModelId       = request.EquipmentModelId;
        entity.DisplayName            = displayName;
        entity.SerialNumber           = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim();
        entity.AcquisitionDate        = request.AcquisitionDate;
        entity.Notes                  = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        entity.IncludeInGlobalCatalog = request.IncludeInGlobalCatalog;
        entity.WebsiteUrl             = MyEquipmentController.NormalizeWebsiteUrl(request.WebsiteUrl);
        entity.DateUpdated            = DateTime.UtcNow;
        entity.UpdatedByAppUserId     = userId;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(EquipmentItem), entity.Id, before, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        var holderNames = await HolderNamesAsync(db, [entity], ct);
        return Ok(ToOrgRecord(entity, canManage: true, holderNames));
    }

    /// <summary>Takes a piece of group gear out of service, keeping its history.</summary>
    /// <remarks>See the personal equivalent — this is the action the delete refusal points at.</remarks>
    [HttpPost("{id:guid}/retire")]
    public Task<IActionResult> Retire(Guid orgId, Guid id, CancellationToken ct)
        => SetRetiredAsync(orgId, id, true, ct);

    /// <summary>Puts a retired piece of group gear back into service.</summary>
    [HttpPost("{id:guid}/unretire")]
    public Task<IActionResult> Unretire(Guid orgId, Guid id, CancellationToken ct)
        => SetRetiredAsync(orgId, id, false, ct);

    private async Task<IActionResult> SetRetiredAsync(Guid orgId, Guid id, bool retired, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentItems
            .FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        if (entity.IsRetired == retired)
            return Conflict(retired ? "That item is already retired." : "That item is not retired.");

        if (retired && await db.EquipmentCheckouts.AnyAsync(c =>
                c.EquipmentItemId == id
                && (c.Status == EquipmentCheckoutStatus.Approved
                    || c.Status == EquipmentCheckoutStatus.CheckedOut), ct))
            return Conflict("This equipment is out on loan. It has to come back before it can be retired.");

        var before = new { entity.IsRetired };
        entity.IsRetired          = retired;
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(EquipmentItem), id, before, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        return NoContent();
    }

    /// <summary>
    /// Deletes a piece of group gear, or refuses when it has a history worth keeping.
    /// </summary>
    /// <remarks>
    /// Serial-numbered property accumulates a record — a service log now, loans from phase 4. Once
    /// any of that exists the answer is <c>Retire</c>, not delete: destroying the item would take
    /// the account of what happened to it along with it.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteOrgEquipment(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Delete, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentItems
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        if (await db.EquipmentCheckouts.AnyAsync(c => c.EquipmentItemId == id, ct)
            || await db.EquipmentServiceLogs.AnyAsync(l => l.EquipmentItemId == id, ct))
            return Conflict("This item has loan or service history. Retire it instead of deleting it.");

        var storagePaths = await db.UploadFiles.AsNoTracking()
            .Where(f => entity.Photos.Select(p => p.UploadFileId).Contains(f.Id))
            .Select(f => f.StoragePath)
            .ToListAsync(ct);

        db.EquipmentItems.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(EquipmentItem), id, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        foreach (var path in storagePaths.Where(p => p is not null))
        {
            try { await _fileStorage.DeleteAsync(path!, ct); }
            catch { /* the row is gone; a stranded blob is not worth failing the request over */ }
        }

        return NoContent();
    }

    /// <summary>
    /// Records who is currently holding a piece of the group's gear, or clears it with a null id.
    /// </summary>
    /// <remarks>
    /// A manual override that exists alongside phase 4's checkout flow — kit gets handed over in a
    /// car park without anyone opening the app, and the holder field should still be able to tell
    /// the truth. The holder must be an active member: gear cannot be assigned to a stranger.
    /// </remarks>
    [HttpPut("{id:guid}/holder")]
    public async Task<ActionResult<EquipmentItemRecord>> SetHolder(
        Guid orgId, Guid id, [FromBody] SetEquipmentHolderRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentItems
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        if (request.AppUserId is not null
            && !await IsActiveMemberAsync(db, orgId, request.AppUserId.Value, ct))
            return BadRequest("Equipment can only be held by an active member of the group.");

        entity.CurrentHolderAppUserId = request.AppUserId;
        entity.DateUpdated            = DateTime.UtcNow;
        entity.UpdatedByAppUserId     = userId;
        await db.SaveChangesAsync(ct);

        var holderNames = await HolderNamesAsync(db, [entity], ct);
        return Ok(ToOrgRecord(entity, canManage: true, holderNames));
    }

    // ── Photos ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches a photo to a piece of the group's gear.
    /// </summary>
    /// <remarks>
    /// Group equipment had no photo capability at all until now, while the editor and the help docs
    /// both said it did — and the projections already read a photo collection that could never be
    /// non-empty. Same ingest pipeline as personal gear: metadata to its own table, original kept,
    /// sanitized copy served.
    /// </remarks>
    [HttpPost("{id:guid}/photos")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<EquipmentItemPhotoRecord>> AttachPhoto(
        Guid orgId, Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is empty.");
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (item is null) return NotFound();

        var storedName   = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath  = _fileStorage.OrgFilePath(orgId, storedName);
        var uploadFileId = Guid.NewGuid();

        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        db.UploadFiles.Add(new UploadFile
        {
            Id                 = uploadFileId,
            UploadFileTypeId   = UploadFileTypeSeeder.EquipmentPhotoFileTypeId,
            AppUserId          = userId,
            FileName           = file.FileName,
            StoredFileName     = storedName,
            ContentType        = ingested.ServedContentType,
            FileSize           = ingested.ServedFileSize,
            StoragePath        = storagePath,
            IsPublic           = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        db.UploadFileMetadata.Add(ingested.Metadata);

        var photo = new EquipmentItemPhoto
        {
            Id                 = Guid.NewGuid(),
            EquipmentItemId    = id,
            UploadFileId       = uploadFileId,
            IsPrimary          = item.Photos.Count == 0,
            SortOrder          = item.Photos.Count,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.EquipmentItemPhotos.Add(photo);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentItemPhoto), photo.Id, photo, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        return Ok(new EquipmentItemPhotoRecord(photo.Id, photo.EquipmentItemId, photo.UploadFileId, photo.IsPrimary, photo.Caption, photo.SortOrder, photo.ExcludeFromCatalog));
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DetachPhoto(Guid orgId, Guid id, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentItems.AnyAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct))
            return NotFound();

        var photo = await db.EquipmentItemPhotos
            .Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.EquipmentItemId == id, ct);
        if (photo is null) return NotFound();

        var storagePath = photo.UploadFile.StoragePath;
        db.EquipmentItemPhotos.Remove(photo);
        db.UploadFiles.Remove(photo.UploadFile);
        await db.SaveChangesAsync(ct);

        if (storagePath is not null) await _mediaIngest.DeleteAllAsync(storagePath, ct);
        return NoContent();
    }

    /// <summary>Hides one photo of the group's gear from the make/model page, or puts it back.</summary>
    [HttpPut("{id:guid}/photos/{photoId:guid}/catalog-exclusion")]
    public async Task<IActionResult> SetPhotoCatalogExclusion(
        Guid orgId, Guid id, Guid photoId, [FromBody] SetPhotoCatalogExclusionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentItems.AnyAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct))
            return NotFound();

        var photo = await db.EquipmentItemPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.EquipmentItemId == id, ct);
        if (photo is null) return NotFound();

        photo.ExcludeFromCatalog = request.Exclude;
        photo.DateUpdated        = DateTime.UtcNow;
        photo.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Makes one photo the one shown first.</summary>
    [HttpPut("{id:guid}/photos/{photoId:guid}/primary")]
    public async Task<IActionResult> SetPrimaryPhoto(Guid orgId, Guid id, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentItems.AnyAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct))
            return NotFound();

        var photos = await db.EquipmentItemPhotos.Where(p => p.EquipmentItemId == id).ToListAsync(ct);
        if (photos.All(p => p.Id != photoId)) return NotFound();

        foreach (var p in photos) p.IsPrimary = p.Id == photoId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Service and defect log ───────────────────────────────────────────────

    /// <summary>The item's service and defect history. Any active member can read it.</summary>
    [HttpGet("{id:guid}/service-log")]
    public async Task<ActionResult<IEnumerable<EquipmentServiceLogRecord>>> GetServiceLog(
        Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsActiveMemberAsync(db, orgId, userId, ct) && !IsSuperAdmin()) return NotFound();

        if (!await db.EquipmentItems.AnyAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct))
            return NotFound();

        var entries = await db.EquipmentServiceLogs.AsNoTracking()
            .Where(l => l.EquipmentItemId == id)
            .OrderByDescending(l => l.EntryDate).ThenByDescending(l => l.DateCreated)
            .Select(l => new EquipmentServiceLogRecord(
                l.Id, l.EquipmentItemId, l.EntryType, l.EntryDate, l.Notes,
                l.PerformedByAppUserId,
                db.AppUsers.Where(u => u.Id == l.PerformedByAppUserId).Select(u => u.DisplayName).FirstOrDefault(),
                l.DateCreated, l.CreatedByAppUserId,
                db.AppUsers.Where(u => u.Id == l.CreatedByAppUserId).Select(u => u.DisplayName).FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>
    /// Adds a service-log entry, and applies its consequence to the item in the same save.
    /// </summary>
    /// <remarks>
    /// The entry and its effect commit together or not at all: a defect report that did not mark
    /// the item as faulty, or a service entry that left the last-serviced date stale, would be a
    /// log that disagrees with the thing it describes.
    /// </remarks>
    [HttpPost("{id:guid}/service-log")]
    public async Task<ActionResult<EquipmentServiceLogRecord>> AddServiceLogEntry(
        Guid orgId, Guid id, [FromBody] AddEquipmentServiceLogRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await CanManageAsync(userId, orgId, OrganizationSecurityAction.Update, ct)) return Forbid();

        var notes = request.Notes?.Trim();
        if (string.IsNullOrWhiteSpace(notes)) return BadRequest("A note is required on a service entry.");
        if (!Enum.IsDefined(request.EntryType)) return BadRequest("Unknown service entry type.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.FirstOrDefaultAsync(i => i.Id == id && i.OwningOrganizationId == orgId, ct);
        if (item is null) return NotFound();

        if (request.PerformedByAppUserId is not null
            && !await IsActiveMemberAsync(db, orgId, request.PerformedByAppUserId.Value, ct))
            return BadRequest("The person who did the work must be an active member of the group.");

        var entry = new EquipmentServiceLog
        {
            Id                   = Guid.NewGuid(),
            EquipmentItemId      = id,
            EntryType            = request.EntryType,
            EntryDate            = request.EntryDate == default ? DateTime.UtcNow : request.EntryDate,
            Notes                = notes,
            PerformedByAppUserId = request.PerformedByAppUserId,
            DateCreated          = DateTime.UtcNow,
            CreatedByAppUserId   = userId,
        };
        db.EquipmentServiceLogs.Add(entry);

        // The item's own fields are a cache of the log's latest word — see EquipmentServiceLog.
        switch (request.EntryType)
        {
            case EquipmentServiceLogType.Service:
                item.LastServicedDate = entry.EntryDate;
                break;
            case EquipmentServiceLogType.DefectReported:
                item.DefectNotes = notes;
                break;
            case EquipmentServiceLogType.DefectResolved:
                item.DefectNotes = null;
                break;
        }
        item.DateUpdated        = DateTime.UtcNow;
        item.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentServiceLog), entry.Id, entry, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        var performedByName = entry.PerformedByAppUserId is null ? null : await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == entry.PerformedByAppUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        var createdByName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        return Ok(new EquipmentServiceLogRecord(
            entry.Id, entry.EquipmentItemId, entry.EntryType, entry.EntryDate, entry.Notes,
            entry.PerformedByAppUserId, performedByName, entry.DateCreated, entry.CreatedByAppUserId, createdByName));
    }

    // ── Projection ───────────────────────────────────────────────────────────

    private static async Task<Dictionary<Guid, string?>> HolderNamesAsync(
        BenDataContext db, IReadOnlyCollection<EquipmentItem> items, CancellationToken ct)
    {
        var holderIds = items.Where(i => i.CurrentHolderAppUserId is not null)
            .Select(i => i.CurrentHolderAppUserId!.Value).Distinct().ToList();
        if (holderIds.Count == 0) return [];

        return await db.AppUsers.AsNoTracking()
            .Where(u => holderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    /// <summary>
    /// Projects group gear, blanking the serial for callers who may not manage equipment.
    /// </summary>
    /// <remarks>
    /// The serial is resolved server-side and simply absent from the payload — the same shape the
    /// avatar endpoint uses. A <c>CanSeeSerial</c> flag the client could ignore would not be a
    /// protection.
    /// </remarks>
    private static EquipmentItemRecord ToOrgRecord(
        EquipmentItem item, bool canManage, Dictionary<Guid, string?> holderNames, string? orgName = null,
        bool canSeeCounters = false)
    {
        var flags = EquipmentAccess.ComputeItemFlags(item, Guid.Empty, isSuperAdmin: false, canManageOrgEquipment: canManage);

        return new EquipmentItemRecord(
            item.Id,
            item.OwnerAppUserId,
            null,
            item.OwningOrganizationId,
            orgName,
            item.EquipmentModelId,
            // ModelName then BrandName — the record's order. These were transposed, so every org
            // surface showed the make where the model belongs and vice versa.
            item.EquipmentModel.Name,
            item.EquipmentModel.EquipmentBrand.Name,
            item.EquipmentModel.EquipmentCategory.Name,
            item.DisplayName,
            flags.CanSeeSerial ? item.SerialNumber : null,
            item.AcquisitionDate,
            item.Notes,
            item.IsRetired,
            item.IncludeInGlobalCatalog,
            item.LoanAudience,
            item.WebsiteUrl,
            canSeeCounters ? new EquipmentItemCountersRecord(item.ViewCount, item.LinkClickCount) : null,
            item.CurrentHolderAppUserId,
            item.CurrentHolderAppUserId is not null && holderNames.TryGetValue(item.CurrentHolderAppUserId.Value, out var name) ? name : null,
            item.LastServicedDate,
            item.DefectNotes,
            item.Photos.OrderBy(p => p.SortOrder)
                .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder, p.ExcludeFromCatalog))
                .ToList(),
            flags);
    }
}
