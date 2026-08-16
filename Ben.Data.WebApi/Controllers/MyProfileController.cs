using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The caller's own account — the first self-service profile surface in the product.
/// </summary>
/// <remarks>
/// <para>Until now a signed-in user could change nothing about themselves after signup: every
/// AppUser field lived behind SuperAdmin-only <c>/admin/users/{id}</c> screens. Everything here is
/// scoped to <c>GetCurrentUserId()</c> and takes no user id from the caller, so there is no
/// "edit someone else" shape to get wrong.</para>
///
/// <para>Photos are <see cref="UploadFile"/> rows joined through <see cref="AppUserPhoto"/>,
/// following the <c>OrganizationLogo</c> pattern. Two slots per user — public and private — with
/// at most one active row each; a filtered unique index enforces that in the database rather than
/// trusting this controller to be the only writer.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MyProfileController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public MyProfileController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    /// <summary>The caller's profile, including whichever photos are currently active.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<MyProfileRecord>> GetProfile(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        var photos = await ActivePhotosAsync(db, userId, ct);

        // Whether the opt-in can actually take effect anywhere. Computed rather than stored so it
        // stays honest as org policy changes underneath the user.
        // Joined explicitly — OrganizationUserMembership carries no Organization navigation.
        var anyOrgAllows = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Join(db.Organizations.AsNoTracking(),
                  m => m.OrganizationId, o => o.Id, (_, o) => o.AllowMemberPrivatePhotosToClients)
            .AnyAsync(allows => allows, ct);

        return Ok(new MyProfileRecord
        {
            AppUserId    = user.Id,
            DisplayName  = user.DisplayName,
            Email        = user.Email,
            PublicPhoto  = photos.FirstOrDefault(p => p.IsPublic),
            PrivatePhoto = photos.FirstOrDefault(p => !p.IsPublic),
            SharePrivatePhotoWithClients    = user.SharePrivatePhotoWithClients,
            AnyOrgAllowsPrivatePhotoSharing = anyOrgAllows,
        });
    }

    /// <summary>Updates the fields a user is allowed to change about themselves.</summary>
    [HttpPut("profile")]
    public async Task<ActionResult<MyProfileRecord>> UpdateProfile(
        [FromBody] UpdateMyProfileRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        var before = new AppUser
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            SharePrivatePhotoWithClients = user.SharePrivatePhotoWithClients,
        };

        // Null means "not supplied" and leaves the name alone; whitespace means "clear it".
        // Collapsing those two would let a partial update blank a name it never mentioned.
        if (request.DisplayName is not null)
        {
            var trimmed = request.DisplayName.Trim();
            if (trimmed.Length > MaxDisplayNameLength)
                return BadRequest($"Display name cannot exceed {MaxDisplayNameLength} characters.");
            user.DisplayName = trimmed.Length == 0 ? null : trimmed;
        }

        // Same null-means-untouched rule as DisplayName: consent is only changed when the caller
        // actually says so, never as a side effect of editing something else on the page.
        if (request.SharePrivatePhotoWithClients is { } share)
            user.SharePrivatePhotoWithClients = share;

        user.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(AppUser), user.Id, before, user, userId, AppSources.WebApi));

        return await GetProfile(ct);
    }

    /// <summary>Every photo the caller has ever set, newest first — prior ones can be re-activated.</summary>
    [HttpGet("photos")]
    public async Task<ActionResult<IEnumerable<AppUserPhotoRecord>>> GetPhotos(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var photos = await db.AppUserPhotos.AsNoTracking()
            .Include(p => p.UploadFile)
            .Where(p => p.AppUserId == userId)
            .OrderByDescending(p => p.DateCreated)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        return Ok(_mapper.Map<IEnumerable<AppUserPhotoRecord>>(photos));
    }

    /// <summary>
    /// Makes an already-uploaded file the caller's photo for one slot, deactivating whatever
    /// held that slot before.
    /// </summary>
    [HttpPost("photos")]
    public async Task<ActionResult<AppUserPhotoRecord>> SetPhoto(
        [FromBody] SetMyPhotoRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        // Optimistic concurrency: let writers race, and let the filtered unique index arbitrate.
        // The loser sees a DbUpdateException and runs the whole operation again — new context,
        // new transaction — so it re-reads the winner's committed row and deactivates it properly.
        // This works because the transaction spans the read as well as the write: an attempt that
        // read outside the transaction would keep retrying against the same stale snapshot.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SetPhotoOnceAsync(userId, request, ct);
            }
            catch (DbUpdateException) when (attempt < MaxSlotWriteAttempts)
            {
                // Bounded, not infinite: past this the collision isn't contention, it's a fault,
                // and it should surface rather than spin.
            }
        }
    }

    private async Task<ActionResult<AppUserPhotoRecord>> SetPhotoOnceAsync(
        Guid userId, SetMyPhotoRequest request, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Must be a file the caller owns. Without this check any authenticated user could point
        // their avatar at someone else's private upload and have the site serve it for them.
        var file = await db.UploadFiles
            .FirstOrDefaultAsync(f => f.Id == request.UploadFileId, ct);
        if (file is null) return NotFound("Upload file not found.");
        if (file.AppUserId != userId) return Forbid();

        // One transaction, opened before the first read rather than just around the save: the
        // read of "what currently holds this slot" is itself part of the operation, and a writer
        // that reads outside the transaction is reading state another writer is about to change.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        // The public slot is served to anyone, so the underlying file has to be public too —
        // otherwise the avatar endpoint would hand out a file the storage layer treats as private.
        // Kept in sync here rather than asking the caller to set it correctly.
        if (file.IsPublic != request.IsPublic)
        {
            file.IsPublic = request.IsPublic;
            file.DateUpdated = DateTime.UtcNow;
            file.UpdatedByAppUserId = userId;
        }

        var previous = await db.AppUserPhotos
            .Where(p => p.AppUserId == userId && p.IsPublic == request.IsPublic && p.IsActive)
            .ToListAsync(ct);
        foreach (var p in previous)
        {
            p.IsActive = false;
            p.DateUpdated = DateTime.UtcNow;
            p.UpdatedByAppUserId = userId;
        }

        var photo = new AppUserPhoto
        {
            Id                 = Guid.NewGuid(),
            AppUserId          = userId,
            UploadFileId       = request.UploadFileId,
            AltText            = string.IsNullOrWhiteSpace(request.AltText) ? null : request.AltText.Trim(),
            IsPublic           = request.IsPublic,
            IsActive           = true,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.AppUserPhotos.Add(photo);

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(
            nameof(AppUserPhoto), photo.Id, photo, userId, AppSources.WebApi));

        return Ok(_mapper.Map<AppUserPhotoRecord>(photo));
    }

    /// <summary>Clears the caller's photo for one slot without deleting the underlying file.</summary>
    [HttpDelete("photos/{photoId:guid}")]
    public async Task<IActionResult> DeletePhoto(Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Matched on both id and owner: someone else's photo id must read as "not found" rather
        // than "forbidden", which would confirm the row exists.
        var photo = await db.AppUserPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.AppUserId == userId, ct);
        if (photo is null) return NotFound();

        db.AppUserPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogDeleteAsync(
            nameof(AppUserPhoto), photoId, photo, userId, AppSources.WebApi));

        return NoContent();
    }

    /// <summary>
    /// The file type new profile-photo uploads should be created under. Exposed so the client
    /// doesn't have to hardcode the id or look it up by name.
    /// </summary>
    [HttpGet("photos/file-type")]
    public ActionResult<Guid> GetPhotoFileTypeId()
        => Ok(UploadFileTypeSeeder.ProfilePhotoFileTypeId);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const int MaxDisplayNameLength = 100;

    /// <summary>
    /// How many times a slot write may be attempted before a unique-index collision is treated as
    /// a fault rather than contention.
    /// </summary>
    /// <remarks>
    /// Measured, not guessed: with the transaction spanning the read, an 8-way race on one slot
    /// settles well inside this budget. Realistic contention here is a double-click or a retried
    /// request — two or three writers, not eight.
    /// </remarks>
    private const int MaxSlotWriteAttempts = 4;

    private async Task<List<AppUserPhotoRecord>> ActivePhotosAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
    {
        var photos = await db.AppUserPhotos.AsNoTracking()
            .Include(p => p.UploadFile)
            .Where(p => p.AppUserId == userId && p.IsActive)
            .ToListAsync(ct);

        return _mapper.Map<List<AppUserPhotoRecord>>(photos);
    }
}
