using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.SeedData;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The signed-in user's own equipment list (backlog item #55, personal gear). Every route is
/// scoped to the caller — ownership checks match id AND owner together and return 404 rather than
/// 403 on a mismatch, so confirming an id exists to someone who doesn't own it is never possible.
/// </summary>
[ApiController]
[Route("api/me/equipment")]
[Authorize]
public sealed class MyEquipmentController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;
    private readonly IMediaIngestService _mediaIngest;

    public MyEquipmentController(
        IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage,
        IAuditLogService auditLog, IMediaIngestService mediaIngest)
    {
        _db          = db;
        _fileStorage = fileStorage;
        _auditLog    = auditLog;
        _mediaIngest = mediaIngest;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EquipmentItemRecord>>> GetAll(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var items = await db.EquipmentItems.AsNoTracking()
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .Where(i => i.OwnerAppUserId == userId)
            .OrderBy(i => i.IsRetired).ThenBy(i => i.DisplayName)
            .ToListAsync(ct);

        var ownerName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        return Ok(items.Select(i => ToRecord(i, EquipmentAccess.ComputeItemFlags(i, userId, IsSuperAdmin()), ownerName, null, null)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EquipmentItemRecord>> GetOne(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking()
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (item is null) return NotFound();

        var ownerName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        return Ok(ToRecord(item, EquipmentAccess.ComputeItemFlags(item, userId, IsSuperAdmin()), ownerName, null, null));
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentItemRecord>> Create(
        [FromBody] UpsertEquipmentItemRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) return BadRequest("Display name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentModels.AnyAsync(m => m.Id == request.EquipmentModelId, ct))
            return BadRequest("Equipment model not found.");

        var entity = new EquipmentItem
        {
            Id                 = Guid.NewGuid(),
            OwnerAppUserId     = userId,
            EquipmentModelId   = request.EquipmentModelId,
            DisplayName        = displayName,
            SerialNumber       = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim(),
            AcquisitionDate    = request.AcquisitionDate,
            Notes              = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsRetired          = false,
            IncludeInGlobalCatalog = request.IncludeInGlobalCatalog,
            LoanAudience           = request.LoanAudience,
            WebsiteUrl             = NormalizeWebsiteUrl(request.WebsiteUrl),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.EquipmentItems.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentItem), entity.Id, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        await db.Entry(entity).Reference(i => i.EquipmentModel).LoadAsync(ct);
        await db.Entry(entity.EquipmentModel).Reference(m => m.EquipmentBrand).LoadAsync(ct);
        await db.Entry(entity.EquipmentModel).Reference(m => m.EquipmentCategory).LoadAsync(ct);

        var ownerName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(ToRecord(entity, EquipmentAccess.ComputeItemFlags(entity, userId, IsSuperAdmin()), ownerName, null, null));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EquipmentItemRecord>> Update(
        Guid id, [FromBody] UpsertEquipmentItemRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) return BadRequest("Display name is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var before = await EquipmentAccess.FindOwnedAsync(db, id, userId, ct);
        if (before is null) return NotFound();
        var beforeSnapshot = new
        {
            before.DisplayName, before.SerialNumber, before.AcquisitionDate, before.Notes,
            before.EquipmentModelId, before.IncludeInGlobalCatalog, before.LoanAudience,
        };

        if (request.EquipmentModelId != before.EquipmentModelId
            && !await db.EquipmentModels.AnyAsync(m => m.Id == request.EquipmentModelId, ct))
            return BadRequest("Equipment model not found.");

        var entity = await db.EquipmentItems
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .FirstAsync(i => i.Id == id, ct);
        entity.EquipmentModelId = request.EquipmentModelId;
        entity.DisplayName      = displayName;
        entity.SerialNumber     = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim();
        entity.AcquisitionDate  = request.AcquisitionDate;
        entity.Notes            = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        entity.IncludeInGlobalCatalog = request.IncludeInGlobalCatalog;
        entity.LoanAudience           = request.LoanAudience;
        entity.WebsiteUrl             = NormalizeWebsiteUrl(request.WebsiteUrl);
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(EquipmentItem), entity.Id, beforeSnapshot, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        await db.Entry(entity.EquipmentModel).Reference(m => m.EquipmentBrand).LoadAsync(ct);
        await db.Entry(entity.EquipmentModel).Reference(m => m.EquipmentCategory).LoadAsync(ct);

        var ownerName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(ToRecord(entity, EquipmentAccess.ComputeItemFlags(entity, userId, IsSuperAdmin()), ownerName, null, null));
    }

    /// <summary>
    /// Takes a piece out of service without destroying what happened to it.
    /// </summary>
    /// <remarks>
    /// The counterpart to the delete guard above, and until now the missing half of it: four places
    /// in the product told people to retire an item instead of deleting it, and there was no way to
    /// do so — an item with history was simply stuck. Retired gear drops out of the public catalog,
    /// out of borrowing, and out of group listings, while its loans and service log stay readable.
    /// </remarks>
    [HttpPost("{id:guid}/retire")]
    public Task<IActionResult> Retire(Guid id, CancellationToken ct) => SetRetiredAsync(id, true, ct);

    /// <summary>Puts a retired piece back into service.</summary>
    [HttpPost("{id:guid}/unretire")]
    public Task<IActionResult> Unretire(Guid id, CancellationToken ct) => SetRetiredAsync(id, false, ct);

    private async Task<IActionResult> SetRetiredAsync(Guid id, bool retired, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentItems
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (entity is null) return NotFound();

        if (entity.IsRetired == retired)
            return Conflict(retired ? "That item is already retired." : "That item is not retired.");

        // Retiring gear somebody currently has would strand the loan; it has to come back first.
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
    /// Deletes a piece of gear that has no history worth keeping, or refuses.
    /// </summary>
    /// <remarks>
    /// Once an item has been lent or serviced, deleting it would take the account of what happened
    /// to it along with it — including loans other people were party to. The answer then is to
    /// retire it, which is what the group-owned side has always done and what this side told
    /// nobody while quietly destroying the history.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentItems.Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (entity is null) return NotFound();

        if (await db.EquipmentCheckouts.AnyAsync(c => c.EquipmentItemId == id, ct)
            || await db.EquipmentServiceLogs.AnyAsync(l => l.EquipmentItemId == id, ct))
            return Conflict("This item has loan or service history. Retire it instead of deleting it.");

        var photoPaths = new List<string?>();
        if (entity.Photos.Count > 0)
        {
            var uploadFileIds = entity.Photos.Select(p => p.UploadFileId).ToList();
            var uploadFiles = await db.UploadFiles.Where(f => uploadFileIds.Contains(f.Id)).ToListAsync(ct);
            photoPaths.AddRange(uploadFiles.Select(f => f.StoragePath));
            db.EquipmentItemPhotos.RemoveRange(entity.Photos);
            db.UploadFiles.RemoveRange(uploadFiles);
        }
        db.EquipmentItems.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(EquipmentItem), entity.Id, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        foreach (var path in photoPaths)
            if (path is not null) await _mediaIngest.DeleteAllAsync(path, ct);

        return NoContent();
    }

    // ── Photos ────────────────────────────────────────────────────────────────

    /// <summary>Attaches a photo to one of my items. Saved under my user file path.</summary>
    [HttpPost("{id:guid}/photos")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<EquipmentItemPhotoRecord>> AttachPhoto(
        Guid id, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (item is null) return NotFound();

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.UserFilePath(userId, storedName);
        var uploadFileId = Guid.NewGuid();

        // Metadata off to its own table, original kept, sanitized copy served — see
        // IMediaIngestService. A non-decodable file is the caller's mistake, not a server fault.
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        var uploadFile = new UploadFile
        {
            Id                 = uploadFileId,
            UploadFileTypeId   = UploadFileTypeSeeder.EquipmentPhotoFileTypeId,
            AppUserId          = userId,
            FileName           = file.FileName,
            StoredFileName     = storedName,
            // What a viewer downloads is the sanitized copy, so its type and size are what belong
            // on the row — the original's are recorded in the metadata table beside its EXIF.
            ContentType        = ingested.ServedContentType,
            FileSize           = ingested.ServedFileSize,
            StoragePath        = storagePath,
            IsPublic           = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        db.UploadFileMetadata.Add(ingested.Metadata);

        var isFirstPhoto = item.Photos.Count == 0;
        var photo = new EquipmentItemPhoto
        {
            Id                 = Guid.NewGuid(),
            EquipmentItemId    = id,
            UploadFileId       = uploadFile.Id,
            IsPrimary          = isFirstPhoto,
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
    public async Task<IActionResult> DetachPhoto(Guid id, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentItems.AnyAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct))
            return NotFound();

        var photo = await db.EquipmentItemPhotos.Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.EquipmentItemId == id, ct);
        if (photo is null) return NotFound();

        var wasPrimary  = photo.IsPrimary;
        var storagePath = photo.UploadFile.StoragePath;
        db.EquipmentItemPhotos.Remove(photo);
        db.UploadFiles.Remove(photo.UploadFile);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(EquipmentItemPhoto), photo.Id, photo, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        // Losing the primary photo silently would leave the item with a gallery but no cover shot.
        if (wasPrimary)
        {
            var next = await db.EquipmentItemPhotos
                .Where(p => p.EquipmentItemId == id)
                .OrderBy(p => p.SortOrder)
                .FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsPrimary = true;
                await db.SaveChangesAsync(ct);
            }
        }

        if (storagePath is not null)
            await _fileStorage.DeleteAsync(storagePath, ct);

        return NoContent();
    }

    [HttpPut("{id:guid}/photos/{photoId:guid}/primary")]
    public async Task<IActionResult> SetPrimaryPhoto(Guid id, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.EquipmentItems.AnyAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct))
            return NotFound();

        var photos = await db.EquipmentItemPhotos.Where(p => p.EquipmentItemId == id).ToListAsync(ct);
        var target = photos.FirstOrDefault(p => p.Id == photoId);
        if (target is null) return NotFound();

        foreach (var p in photos) p.IsPrimary = p.Id == photoId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Sharing with groups ───────────────────────────────────────────────────

    /// <summary>
    /// The caller's groups, each flagged with whether this item is shared with it.
    /// </summary>
    /// <remarks>
    /// Returns the options rather than just the current shares: the editor needs both, and asking
    /// "which groups am I in" and "which of them can see this" separately is the same question
    /// twice. Groups the owner has since left simply drop off the list — and stop granting
    /// visibility, because every read checks live membership.
    /// </remarks>
    [HttpGet("{id:guid}/shares")]
    public async Task<ActionResult<IEnumerable<EquipmentShareOptionRecord>>> GetShares(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (item is null) return NotFound();

        var sharedOrgIds = await db.EquipmentItemShares.AsNoTracking()
            .Where(s => s.EquipmentItemId == id)
            .Select(s => s.OrganizationId)
            .ToListAsync(ct);

        var options = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Join(db.Organizations.AsNoTracking(), m => m.OrganizationId, o => o.Id, (m, o) => new { o.Id, o.Name })
            .OrderBy(o => o.Name)
            .ToListAsync(ct);

        return Ok(options.Select(o => new EquipmentShareOptionRecord(o.Id, o.Name, sharedOrgIds.Contains(o.Id))));
    }

    /// <summary>
    /// Replaces which groups can see this item. Any group not in the list is unshared.
    /// </summary>
    /// <remarks>
    /// Only groups the caller is an active member of are accepted — you cannot push your gear into
    /// a group you do not belong to. Rejects org-owned items outright: that gear already belongs to
    /// a group, so a share row would mean nothing.
    /// </remarks>
    [HttpPut("{id:guid}/shares")]
    public async Task<ActionResult<IEnumerable<EquipmentShareOptionRecord>>> SetShares(
        Guid id, [FromBody] SetEquipmentSharesRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (item is null) return NotFound();
        if (item.OwningOrganizationId is not null)
            return BadRequest("Group-owned equipment already belongs to a group and cannot be shared this way.");

        var requested = (request.OrganizationIds ?? []).Distinct().ToList();

        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        var notMine = requested.Where(o => !myOrgIds.Contains(o)).ToList();
        if (notMine.Count > 0)
            return BadRequest("You can only share equipment with groups you are an active member of.");

        var existing = await db.EquipmentItemShares.Where(s => s.EquipmentItemId == id).ToListAsync(ct);

        var toRemove = existing.Where(s => !requested.Contains(s.OrganizationId)).ToList();
        if (toRemove.Count > 0) db.EquipmentItemShares.RemoveRange(toRemove);

        var existingOrgIds = existing.Select(s => s.OrganizationId).ToHashSet();
        foreach (var orgId in requested.Where(o => !existingOrgIds.Contains(o)))
        {
            db.EquipmentItemShares.Add(new EquipmentItemShare
            {
                Id                 = Guid.NewGuid(),
                EquipmentItemId    = id,
                OrganizationId     = orgId,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(EquipmentItem), id,
            new { SharedWith = existing.Select(s => s.OrganizationId).ToList() },
            new { SharedWith = requested },
            userId, Ben.Data.Common.Constants.AppSources.WebApi));

        return await GetShares(id, ct);
    }

    /// <summary>
    /// Shares or unshares every one of the caller's non-retired personal items with one group.
    /// </summary>
    /// <remarks>
    /// A convenience over the per-item endpoint, not a second model: it writes the same per-item
    /// rows, so the owner can immediately exclude one piece without undoing the rest.
    /// </remarks>
    [HttpPost("shares/bulk")]
    public async Task<ActionResult<BulkEquipmentShareResult>> BulkShare(
        [FromBody] BulkEquipmentShareRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var isMember = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.AppUserId == userId && m.OrganizationId == request.OrganizationId && m.IsActive, ct);
        if (!isMember)
            return BadRequest("You can only share equipment with groups you are an active member of.");

        var myItemIds = await db.EquipmentItems.AsNoTracking()
            .Where(i => i.OwnerAppUserId == userId && !i.IsRetired)
            .Select(i => i.Id)
            .ToListAsync(ct);

        var existing = await db.EquipmentItemShares
            .Where(s => s.OrganizationId == request.OrganizationId && myItemIds.Contains(s.EquipmentItemId))
            .ToListAsync(ct);

        var affected = 0;
        if (request.Share)
        {
            var alreadyShared = existing.Select(s => s.EquipmentItemId).ToHashSet();
            foreach (var itemId in myItemIds.Where(i => !alreadyShared.Contains(i)))
            {
                db.EquipmentItemShares.Add(new EquipmentItemShare
                {
                    Id                 = Guid.NewGuid(),
                    EquipmentItemId    = itemId,
                    OrganizationId     = request.OrganizationId,
                    DateCreated        = DateTime.UtcNow,
                    CreatedByAppUserId = userId,
                });
                affected++;
            }
        }
        else
        {
            db.EquipmentItemShares.RemoveRange(existing);
            affected = existing.Count;
        }

        if (affected > 0) await db.SaveChangesAsync(ct);

        return Ok(new BulkEquipmentShareResult(affected, myItemIds.Count));
    }

    private bool IsSuperAdmin() => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

    /// <summary>
    /// Accepts only an absolute http/https address, so a stored link cannot become a
    /// <c>javascript:</c> payload the moment somebody renders it as an anchor.
    /// </summary>
    internal static string? NormalizeWebsiteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }

    private static EquipmentItemRecord ToRecord(
        EquipmentItem item, EquipmentItemFlags flags, string? ownerName, string? orgName, string? holderName)
        => new(
            item.Id,
            item.OwnerAppUserId,
            ownerName,
            item.OwningOrganizationId,
            orgName,
            item.EquipmentModelId,
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
            // Counters are for org Administrators and SuperAdmin — an owner does not see their own.
            null,
            item.CurrentHolderAppUserId,
            holderName,
            item.LastServicedDate,
            flags.CanSeeSerial ? item.DefectNotes : null,
            item.Photos.OrderBy(p => p.SortOrder)
                .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder, p.ExcludeFromCatalog))
                .ToList(),
            flags);
}

/// <summary>
/// Authenticated byte access to an equipment item photo — the `&lt;img&gt;`-sends-no-bearer-token
/// pattern every other photo surface in this app uses (see AvatarCache/UserMediaPreview). Callers
/// fetch bytes through this and render a data: URI, never a plain &lt;img src&gt;.
/// </summary>
[ApiController]
[Route("api/equipment/photos")]
public sealed class EquipmentPhotoContentController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;

    public EquipmentPhotoContentController(
        IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage, IMediaIngestService mediaIngest)
    {
        _db          = db;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
    }

    /// <summary>
    /// Photo bytes for one equipment photo.
    /// </summary>
    /// <remarks>
    /// Not blanket <c>[Authorize]</c>, because an item its owner listed publicly has to show its
    /// photos to visitors who have no token — the same reason the endpoint exists at all is that an
    /// <c>&lt;img src&gt;</c> sends no bearer token. Anonymous callers therefore reach exactly the
    /// photos of publicly-listed, non-retired items and nothing else; everything narrower still
    /// requires being the owner (or SuperAdmin). Answers 404 rather than 403 throughout, so the
    /// endpoint cannot be used to probe which photo ids exist.
    /// </remarks>
    [HttpGet("{photoId:guid}/content")]
    [AllowAnonymous]
    public async Task<IActionResult> GetContent(Guid photoId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var photo = await LoadVisiblePhotoAsync(db, photoId, ct);
        if (photo is null) return NotFound();
        if (photo.UploadFile.StoragePath is null) return NotFound();

        // The sanitized copy is what leaves the server; the original stays for evidence only.
        var servingPath = _mediaIngest.ServingPathFor(photo.UploadFile.StoragePath);
        var stream = await _fileStorage.OpenReadAsync(servingPath, ct);
        return File(stream, photo.UploadFile.ContentType);
    }

    /// <summary>
    /// A small copy of the photo, for grids that would otherwise download full-resolution files to
    /// draw them a hundred pixels wide.
    /// </summary>
    /// <remarks>
    /// Same visibility rule as the full bytes — deliberately the same method, so the two routes
    /// cannot drift apart and quietly leave the thumbnail more permissive than the thing it is a
    /// thumbnail of. Generates and stores on first request when the sibling file is missing, which
    /// covers anything uploaded before the pipeline existed without a backfill job.
    /// </remarks>
    [HttpGet("{photoId:guid}/thumbnail")]
    [AllowAnonymous]
    public async Task<IActionResult> GetThumbnail(Guid photoId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var photo = await LoadVisiblePhotoAsync(db, photoId, ct);
        if (photo is null) return NotFound();
        if (photo.UploadFile.StoragePath is null) return NotFound();

        var stream = await _mediaIngest.OpenThumbnailAsync(photo.UploadFile.StoragePath, ct);
        return stream is null
            ? await GetContent(photoId, ct)   // nothing to shrink (a non-image) — serve the real thing
            : File(stream, "image/jpeg");
    }

    /// <summary>
    /// What was stripped out of the picture: EXIF, GPS, camera, capture time.
    /// </summary>
    /// <remarks>
    /// Ben's rule (2026-08-17): this is for org Administrators and SuperAdmin only — deliberately
    /// NOT the item's owner, who can see the photo itself but not the coordinates that came with
    /// it. Never carried on the bytes or thumbnail routes; a caller has to ask for it by name, and
    /// gets a 404 rather than a 403 if they may not.
    /// </remarks>
    [HttpGet("{photoId:guid}/metadata")]
    public async Task<ActionResult<UploadFileMetadataRecord>> GetPhotoMetadata(Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

        await using var db = await _db.CreateDbContextAsync(ct);
        var photo = await db.EquipmentItemPhotos.AsNoTracking()
            .Include(p => p.EquipmentItem)
            .FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return NotFound();

        var mayRead = isSuperAdmin;
        if (!mayRead && photo.EquipmentItem.OwningOrganizationId is Guid orgId)
        {
            // Org Administrators and Owners of the group that owns the gear. The membership ROLE,
            // not the Equipment permission: the audience Ben named is administrators, not whoever
            // an org happened to hand an equipment role to.
            mayRead = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
                            && (m.Role == OrganizationMemberRole.Owner
                                || m.Role == OrganizationMemberRole.Administrator), ct);
        }
        if (!mayRead) return NotFound();

        var meta = await db.UploadFileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UploadFileId == photo.UploadFileId, ct);
        if (meta is null) return NotFound();

        return Ok(new UploadFileMetadataRecord(
            meta.MediaKind, meta.WidthPixels, meta.HeightPixels, meta.CapturedAtUtc,
            meta.GpsLatitude, meta.GpsLongitude, meta.GpsAltitudeMeters,
            meta.CameraManufacturer, meta.CameraModel, meta.DurationSeconds, meta.ExtractedAtUtc));
    }

    /// <summary>
    /// Loads a photo the caller is allowed to see, or null.
    /// </summary>
    /// <remarks>
    /// The single place the visibility rule lives, because it has five branches and both serve
    /// routes depend on it being exactly the same rule. Answers null rather than a reason: every
    /// caller turns that into a 404, so the endpoints cannot be used to probe which photo ids
    /// exist.
    /// </remarks>
    private async Task<EquipmentItemPhoto?> LoadVisiblePhotoAsync(
        BenDataContext db, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

        var photo = await db.EquipmentItemPhotos.AsNoTracking()
            .Include(p => p.EquipmentItem)
            .Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return null;

        var item = photo.EquipmentItem;

        // Retired gear is out of circulation entirely — no public route reaches it.
        if (item.IsRetired)
        {
            var flagsForRetired = EquipmentAccess.ComputeItemFlags(item, userId, isSuperAdmin);
            return flagsForRetired.IsOwner || isSuperAdmin ? photo : null;
        }

        // Publicly listed by its owner — anyone may see it, signed in or not.
        if (item.IncludeInGlobalCatalog) return photo;

        // Pooled onto the make/model page, which is public. The owner opted this photo in by not
        // excluding it, so the bytes have to be reachable or the page shows broken images.
        if (!photo.ExcludeFromCatalog) return photo;

        var flags = EquipmentAccess.ComputeItemFlags(item, userId, isSuperAdmin);
        if (flags.IsOwner || isSuperAdmin) return photo;
        if (userId == Guid.Empty || item.IsRetired) return null;

        // Group-owned gear: any active member of the owning group. Without this branch an org
        // item's photos were reachable by nobody but SuperAdmin, because org items have no
        // OwnerAppUserId for IsOwner to match.
        if (item.OwningOrganizationId is Guid owningOrgId)
        {
            var isMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == owningOrgId && m.AppUserId == userId && m.IsActive, ct);
            return isMember ? photo : null;
        }

        // Personal gear shared into a group the caller and the owner are both still in. Checked
        // live rather than trusted from the share row, so a share left behind after either of them
        // left the group stops granting anything.
        var isShared = await EquipmentAccess.IsSharedWithAGroupSharedWithAsync(
            db, photo.EquipmentItemId, item.OwnerAppUserId, userId, ct);
        return isShared ? photo : null;
    }
}
