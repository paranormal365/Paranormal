using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Serves the profile photo of <em>another</em> user, choosing which one the caller is allowed to
/// see. The caller never names a photo — only a person — so there is no id to tamper with.
/// </summary>
/// <remarks>
/// <para>Resolution order, most-trusted relationship first:</para>
/// <list type="number">
///   <item>It's you — you always see your own private photo.</item>
///   <item>You share an active org membership with them: colleagues see each other properly.</item>
///   <item>You're a client of a case at an org they actively belong to, that org allows it, and
///         they opted in — the two-key rule, via <see cref="PrivatePhotoConsent"/>.</item>
///   <item>Otherwise their public photo, if they have set one.</item>
///   <item>Otherwise nothing (204), and the caller renders initials.</item>
/// </list>
///
/// <para>Deliberately returns 204 rather than 404 when there is no photo to show: "this person has
/// no picture you may see" is a normal answer, not an error, and the two cases are intentionally
/// indistinguishable so the endpoint can't be used to probe whether someone has a private photo.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UserAvatarController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IFileStorageService _fileStorage;

    public UserAvatarController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IFileStorageService fileStorage)
    {
        _dbContextFactory = dbContextFactory;
        _fileStorage = fileStorage;
    }

    [HttpGet("{userId:guid}/avatar")]
    public async Task<IActionResult> GetAvatar(Guid userId, CancellationToken ct)
    {
        var viewerId = GetCurrentUserId();
        if (viewerId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var maySeePrivate = await MaySeePrivatePhotoAsync(db, viewerId, userId, ct);

        // One query for both slots, then pick — the private photo is only preferred when allowed,
        // and falling back to public keeps a face on screen rather than dropping to initials just
        // because the viewer isn't close enough for the private one.
        var photos = await db.AppUserPhotos.AsNoTracking()
            .Where(p => p.AppUserId == userId && p.IsActive)
            .Select(p => new { p.IsPublic, p.UploadFileId })
            .ToListAsync(ct);

        var chosen = (maySeePrivate ? photos.FirstOrDefault(p => !p.IsPublic) : null)
                  ?? photos.FirstOrDefault(p => p.IsPublic);
        if (chosen is null) return NoContent();

        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == chosen.UploadFileId, ct);
        if (file is null) return NoContent();

        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            var stream = await _fileStorage.OpenReadAsync(file.StoragePath, ct);
            return File(stream, file.ContentType);
        }
        return file.FileData is not null
            ? File(file.FileData, file.ContentType)
            : NoContent();
    }

    /// <summary>
    /// Whether <paramref name="viewerId"/> has a relationship with <paramref name="subjectId"/>
    /// close enough to be shown the private photo.
    /// </summary>
    private static async Task<bool> MaySeePrivatePhotoAsync(
        BenDataContext db, Guid viewerId, Guid subjectId, CancellationToken ct)
    {
        if (viewerId == subjectId) return true;

        var subjectOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == subjectId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);
        if (subjectOrgIds.Count == 0) return false;

        // Colleagues: an active membership in common. No consent flags needed — working together
        // is the relationship, and org members already see each other's names and notes.
        var sharesOrg = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.AppUserId == viewerId
                        && m.IsActive
                        && subjectOrgIds.Contains(m.OrganizationId), ct);
        if (sharesOrg) return true;

        // Client route: only the orgs the viewer actually has a case with count, and only if both
        // that org and the subject have said yes.
        var viewerCaseOrgIds = await db.Cases.AsNoTracking()
            .Where(c => (c.ClientRequest != null && c.ClientRequest.AppUserId == viewerId)
                     || db.CaseClientAccesses.Any(a => a.CaseId == c.Id && a.AppUserId == viewerId))
            .Select(c => c.OrganizationId)
            .Distinct()
            .ToListAsync(ct);
        if (viewerCaseOrgIds.Count == 0) return false;

        var sharedOrgIds = subjectOrgIds.Intersect(viewerCaseOrgIds).ToList();
        if (sharedOrgIds.Count == 0) return false;

        var subject = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == subjectId, ct);
        if (subject is null) return false;

        // Any one qualifying org is enough, but each is judged on both keys together.
        var permissiveOrgs = await db.Organizations.AsNoTracking()
            .Where(o => sharedOrgIds.Contains(o.Id))
            .ToListAsync(ct);

        return permissiveOrgs.Any(org => PrivatePhotoConsent.MayShowToClient(subject, org));
    }
}
