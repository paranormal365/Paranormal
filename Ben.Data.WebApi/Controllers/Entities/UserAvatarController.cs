using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

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
///   <item>The mirror of that: they are your client on a live case at an org you actively belong
///         to. Engaging an organization to come to your home is itself the sharing — see
///         <see cref="ClientIsEngagedWithViewersOrgAsync"/> for why this side needs no flags and
///         why it ends when the case does.</item>
///   <item>Otherwise their public photo, if they have set one.</item>
///   <item>Otherwise the sitewide default avatar, when a SuperAdmin has configured one.</item>
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

        // Falls back to the sitewide default before giving up. Placed last on purpose: a generic
        // house image must never take precedence over a photo the person actually chose. It
        // carries no personal information, so it is served to any viewer — the audience rules
        // above decide which of *their* photos you get, not whether you may see a stock image.
        var fileId = chosen?.UploadFileId
                  ?? await SiteSettingsService.GetGuidAsync(db, SiteSettingKeys.DefaultAvatarUploadFileId, ct);
        if (fileId is null) return NoContent();

        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        // A default pointing at a deleted or mistyped file id degrades to initials rather than
        // erroring — a bad setting shouldn't break every avatar on the site.
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
        // Each route is independent and any one of them is sufficient. Written as a flat list on
        // purpose: an earlier version threaded them together and its "no qualifying orgs" exits
        // returned before the later routes ran, so someone who was a member at one org and a
        // client at another was judged only by the route that happened to be checked first.
        if (viewerId == subjectId) return true;
        if (await ShareACaseAsClientsAsync(db, viewerId, subjectId, ct)) return true;
        if (await SharesAnActiveOrgAsync(db, viewerId, subjectId, ct)) return true;
        if (await SubjectConsentsToShowClientsAsync(db, viewerId, subjectId, ct)) return true;
        if (await ClientIsEngagedWithViewersOrgAsync(db, viewerId, subjectId, ct)) return true;
        return false;
    }

    /// <summary>
    /// Both people are clients on the same case — the originating client and a co-client, or two
    /// co-clients.
    /// </summary>
    /// <remarks>
    /// <para>No flags and no gating. People on the same case are participants in the same events:
    /// they already read each other's occurrences and messages, and were invited onto the case by
    /// one another. Treating them as strangers to each other would be a fiction the rest of the
    /// product doesn't maintain.</para>
    ///
    /// <para>Unlike the client↔org route, this is not limited to live cases. That bound exists
    /// because an <i>engagement</i> with an organization ends; two people who experienced the same
    /// events remain who they are after the file closes, and they typically share a household.
    /// If that ever needs revisiting, add the same status filter used by
    /// <see cref="ClientIsEngagedWithViewersOrgAsync"/>.</para>
    ///
    /// <para>Says nothing about the org or public boundaries — those keep their own rules.</para>
    /// </remarks>
    private static async Task<bool> ShareACaseAsClientsAsync(
        BenDataContext db, Guid viewerId, Guid subjectId, CancellationToken ct)
    {
        var viewerCaseIds = await ClientCaseIdsAsync(db, viewerId, ct);
        if (viewerCaseIds.Count == 0) return false;

        var subjectCaseIds = await ClientCaseIdsAsync(db, subjectId, ct);
        return subjectCaseIds.Any(viewerCaseIds.Contains);
    }

    /// <summary>Cases where this user is the originating client or a co-client.</summary>
    private static async Task<HashSet<Guid>> ClientCaseIdsAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
    {
        var own = await db.Cases.AsNoTracking()
            .Where(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var shared = await db.CaseClientAccesses.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .Select(a => a.CaseId)
            .ToListAsync(ct);

        return own.Concat(shared).ToHashSet();
    }

    /// <summary>
    /// Colleagues — an active membership in common. No consent flags: working together is the
    /// relationship, and org members already see each other's names and case notes.
    /// </summary>
    private static async Task<bool> SharesAnActiveOrgAsync(
        BenDataContext db, Guid viewerId, Guid subjectId, CancellationToken ct)
    {
        var subjectOrgIds = await ActiveOrgIdsAsync(db, subjectId, ct);
        if (subjectOrgIds.Count == 0) return false;

        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.AppUserId == viewerId
                        && m.IsActive
                        && subjectOrgIds.Contains(m.OrganizationId), ct);
    }

    /// <summary>
    /// The member→client direction: the viewer is a client of an org the subject works for, and
    /// both that org and the subject have said yes. Two keys, via <see cref="PrivatePhotoConsent"/>.
    /// </summary>
    private static async Task<bool> SubjectConsentsToShowClientsAsync(
        BenDataContext db, Guid viewerId, Guid subjectId, CancellationToken ct)
    {
        var subjectOrgIds = await ActiveOrgIdsAsync(db, subjectId, ct);
        if (subjectOrgIds.Count == 0) return false;

        var viewerCaseOrgIds = await db.Cases.AsNoTracking()
            .Where(c => (c.ClientRequest != null && c.ClientRequest.AppUserId == viewerId)
                     || db.CaseClientAccesses.Any(a => a.CaseId == c.Id && a.AppUserId == viewerId))
            .Select(c => c.OrganizationId)
            .Distinct()
            .ToListAsync(ct);

        var sharedOrgIds = subjectOrgIds.Intersect(viewerCaseOrgIds).ToList();
        if (sharedOrgIds.Count == 0) return false;

        var subject = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == subjectId, ct);
        if (subject is null) return false;

        // Any one qualifying org is enough, but each is judged on both keys together.
        var candidateOrgs = await db.Organizations.AsNoTracking()
            .Where(o => sharedOrgIds.Contains(o.Id))
            .ToListAsync(ct);

        return candidateOrgs.Any(org => PrivatePhotoConsent.MayShowToClient(subject, org));
    }

    private static Task<List<Guid>> ActiveOrgIdsAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
        => db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

    /// <summary>
    /// Whether the subject is a client on a live case at an organization the viewer actively
    /// belongs to — the investigator-looking-at-their-client direction.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately asymmetric with the member→client direction above, which needs both an
    /// org policy and a personal opt-in. Here the engagement <i>is</i> the sharing: a client has
    /// asked this organization into their home and has already given it their address and their
    /// account of what happened. Investigators knowing which face belongs to that file is a
    /// smaller disclosure than the ones the client has already chosen to make, and it is the
    /// arrangement the feature plan specifies. If that ever needs revisiting, the fix is a client
    /// opt-out flag consulted here — the resolution stays in this one method either way.</para>
    ///
    /// <para>Scoped to cases that are still running. Closed, transferred and published cases end
    /// the engagement, and access that outlives the relationship is exactly the kind that nobody
    /// remembers granting. Co-clients count the same as the originating client: they were invited
    /// onto the case as participants, not as bystanders.</para>
    /// </remarks>
    private static async Task<bool> ClientIsEngagedWithViewersOrgAsync(
        BenDataContext db, Guid viewerId, Guid subjectId, CancellationToken ct)
    {
        var viewerOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == viewerId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);
        if (viewerOrgIds.Count == 0) return false;

        return await db.Cases.AsNoTracking()
            .AnyAsync(c => viewerOrgIds.Contains(c.OrganizationId)
                        && LiveCaseStatuses.Contains(c.Status)
                        && ((c.ClientRequest != null && c.ClientRequest.AppUserId == subjectId)
                            || db.CaseClientAccesses.Any(a => a.CaseId == c.Id && a.AppUserId == subjectId)),
                       ct);
    }

    /// <summary>
    /// Case states in which the client is still engaged with the organization. Excludes Closed,
    /// Transferred, Public and Haunted — those are finished files, not live working relationships.
    /// </summary>
    private static readonly CaseStatus[] LiveCaseStatuses =
        [CaseStatus.Proposed, CaseStatus.Accepted, CaseStatus.Active, CaseStatus.Summarized];
}
