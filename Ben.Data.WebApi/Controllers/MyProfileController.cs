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

        return Ok(new MyProfileRecord
        {
            AppUserId    = user.Id,
            DisplayName  = user.DisplayName,
            Email        = user.Email,
            PublicPhoto  = photos.FirstOrDefault(p => p.IsPublic),
            PrivatePhoto = photos.FirstOrDefault(p => !p.IsPublic),
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

        var before = new AppUser { Id = user.Id, DisplayName = user.DisplayName };

        // Null means "not supplied" and leaves the name alone; whitespace means "clear it".
        // Collapsing those two would let a partial update blank a name it never mentioned.
        if (request.DisplayName is not null)
        {
            var trimmed = request.DisplayName.Trim();
            if (trimmed.Length > MaxDisplayNameLength)
                return BadRequest($"Display name cannot exceed {MaxDisplayNameLength} characters.");
            user.DisplayName = trimmed.Length == 0 ? null : trimmed;
        }

        user.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(AppUser), user.Id, before, user, userId, AppSources.WebApi, ct));

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

        // Two writes racing for the same slot both try to insert an active row, and the filtered
        // unique index rejects the loser. The index is doing its job — the data stays correct —
        // but a double-click is a normal thing for a user to do, and it surfaced as a bare 500.
        // Each retry re-reads the now-committed winner and deactivates it properly. Measured: a
        // 6-way race needed more than one retry, so the budget is small but not one.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SetPhotoOnceAsync(userId, request, ct);
            }
            catch (DbUpdateException) when (attempt < MaxSlotWriteAttempts)
            {
                // Fall through and try again with a fresh context. Bounded rather than infinite:
                // past this point a failure is a real fault and should surface, not spin.
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

        // One transaction: deactivating the old slot and activating the new one must not be
        // separable, or a failure between them leaves the user with no photo at all.
        if (db.Database.IsRelational())
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        _ = TryAuditAsync(_auditLog.LogCreateAsync(
            nameof(AppUserPhoto), photo.Id, photo, userId, AppSources.WebApi, ct));

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
            nameof(AppUserPhoto), photoId, photo, userId, AppSources.WebApi, ct));

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
    /// a real fault. Covers realistic contention (a double-click, a retried request) without
    /// turning a genuine constraint problem into an endless loop.
    /// </summary>
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
