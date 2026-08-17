using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.SeedData;
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

    public MyEquipmentController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage, IAuditLogService auditLog)
    {
        _db          = db;
        _fileStorage = fileStorage;
        _auditLog    = auditLog;
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
    /// Deletes an item with no checkout history. Phase 4 adds the checkout-lifecycle table and
    /// changes this to a 409-with-retire-instead once a row has any loan history — nothing to
    /// guard yet in Phase 1.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.EquipmentItems.Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerAppUserId == userId, ct);
        if (entity is null) return NotFound();

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
            if (path is not null) await _fileStorage.DeleteAsync(path, ct);

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
        await _fileStorage.WriteFormFileAsync(storagePath, file, ct);

        var uploadFile = new UploadFile
        {
            Id                 = Guid.NewGuid(),
            UploadFileTypeId   = UploadFileTypeSeeder.EquipmentPhotoFileTypeId,
            AppUserId          = userId,
            FileName           = file.FileName,
            StoredFileName     = storedName,
            ContentType        = file.ContentType,
            FileSize           = file.Length,
            StoragePath        = storagePath,
            IsPublic           = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

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

        return Ok(new EquipmentItemPhotoRecord(photo.Id, photo.EquipmentItemId, photo.UploadFileId, photo.IsPrimary, photo.Caption, photo.SortOrder));
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
            item.CurrentHolderAppUserId,
            holderName,
            item.LastServicedDate,
            flags.CanSeeSerial ? item.DefectNotes : null,
            item.Photos.OrderBy(p => p.SortOrder)
                .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder))
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

    public EquipmentPhotoContentController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage)
    {
        _db          = db;
        _fileStorage = fileStorage;
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
        var userId = GetCurrentUserId();
        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

        await using var db = await _db.CreateDbContextAsync(ct);
        var photo = await db.EquipmentItemPhotos.AsNoTracking()
            .Include(p => p.EquipmentItem)
            .Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return NotFound();

        // Publicly listed by its owner — anyone may see it, signed in or not.
        var isPubliclyListed = photo.EquipmentItem.IncludeInGlobalCatalog && !photo.EquipmentItem.IsRetired;

        var flags = EquipmentAccess.ComputeItemFlags(photo.EquipmentItem, userId, isSuperAdmin);

        // Shared with a group the caller and the owner are both active members of. Checked live
        // rather than trusted from the share row, so a share left behind after either of them left
        // the group stops granting anything.
        var isSharedWithMyGroup = !isPubliclyListed && !flags.IsOwner && !isSuperAdmin
            && userId != Guid.Empty
            && !photo.EquipmentItem.IsRetired
            && await EquipmentAccess.IsSharedWithAGroupSharedWithAsync(
                   db, photo.EquipmentItemId, photo.EquipmentItem.OwnerAppUserId, userId, ct);

        if (!isPubliclyListed && !isSharedWithMyGroup && !flags.IsOwner && !isSuperAdmin) return NotFound();

        if (photo.UploadFile.StoragePath is null) return NotFound();
        var stream = await _fileStorage.OpenReadAsync(photo.UploadFile.StoragePath, ct);
        return File(stream, photo.UploadFile.ContentType);
    }
}
