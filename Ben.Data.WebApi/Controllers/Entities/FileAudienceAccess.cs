using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Which of the four comment/share audiences a user matches for a specific UploadFile, and
/// whether they can see the file at all. Shared between <see cref="UploadFileCommentController"/>
/// (posting/read-gating) and <see cref="CaseFileController"/>'s <c>Link</c> action (source-file
/// visibility check before copy-on-attach) — the only two places in the app that need this
/// combined logic, so it lives as a plain static helper rather than a new injectable service,
/// matching this codebase's existing convention of per-need static/inline access checks (e.g.
/// <c>CaseFileController.IsOrgMember</c>) rather than a shared permission-service layer.
/// </summary>
public static class FileAudienceAccess
{
    /// <summary>
    /// Which audiences <paramref name="userId"/> currently belongs to for <paramref name="uploadFileId"/>.
    /// Each bool requires an active Phase-1 share/link actually targeting that audience on this
    /// file — being an investigation-team member in general, or a client of some other case, does
    /// not count. This is identity ("who are you to this file"), independent of whether the file
    /// owner has the corresponding <c>Allow*Comments</c> toggle on — callers AND the two together
    /// to decide whether posting is allowed; the membership bools alone are what gets snapshotted
    /// onto a posted <c>UploadFileComment</c> for display.
    /// </summary>
    public static async Task<FileAudienceMembership> GetMembershipAsync(
        BenDataContext db, Guid uploadFileId, Guid userId, CancellationToken ct)
    {
        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (file is null)
            return new FileAudienceMembership(false, false, false, false, false);

        var isOwner = file.AppUserId == userId;

        // Investigation team: an active share targets an investigation the user actually attends.
        var investigationIds = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .Select(a => a.InvestigationId)
            .ToListAsync(ct);
        var isInvestigationTeamMember = investigationIds.Count > 0 && await db.UploadFileShares.AsNoTracking()
            .AnyAsync(s => s.UploadFileId == uploadFileId && s.IsActive
                        && s.TargetType == ShareTargetType.InvestigationTeam
                        && investigationIds.Contains(s.TargetInvestigationId!.Value), ct);

        // Client: the file is linked (CaseFile or CaseTimelineEntryFile) to a case where this user
        // is the originating client — the only place in the codebase "is the client" is determined.
        var isClient = await db.CaseFiles.AsNoTracking()
            .AnyAsync(cf => cf.UploadFileId == uploadFileId
                         && cf.Case.ClientRequest != null && cf.Case.ClientRequest.AppUserId == userId, ct)
            || await db.CaseTimelineEntryFiles.AsNoTracking()
            .AnyAsync(ef => ef.UploadFileId == uploadFileId
                         && ef.CaseTimelineEntry.Case.ClientRequest != null
                         && ef.CaseTimelineEntry.Case.ClientRequest.AppUserId == userId, ct);

        // Organization: an active share/link targets an org the user is an active member of,
        // respecting UploadFileOrganizationShare's visibility tier (mirrors MediaLibraryController).
        var orgMemberships = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync(ct);
        var orgIds = orgMemberships.Select(m => m.OrganizationId).ToHashSet();
        var adminOrgIds = orgMemberships
            .Where(m => m.Role <= OrganizationMemberRole.Administrator)
            .Select(m => m.OrganizationId).ToHashSet();

        var isOrganizationMember = false;
        if (orgIds.Count > 0)
        {
            isOrganizationMember = await db.UploadFileShares.AsNoTracking()
                .AnyAsync(s => s.UploadFileId == uploadFileId && s.IsActive
                            && s.TargetType == ShareTargetType.Organization
                            && orgIds.Contains(s.TargetOrganizationId!.Value), ct);

            if (!isOrganizationMember)
            {
                var tieredShares = await db.UploadFileOrganizationShares.AsNoTracking()
                    .Where(s => s.UploadFileId == uploadFileId && s.IsActive && orgIds.Contains(s.OrganizationId))
                    .Select(s => new { s.OrganizationId, s.Visibility })
                    .ToListAsync(ct);
                isOrganizationMember = tieredShares.Any(s =>
                    s.Visibility == FileShareVisibility.Public
                    || s.Visibility == FileShareVisibility.OrgMembers
                    || (s.Visibility == FileShareVisibility.OrgAdminsOnly && adminOrgIds.Contains(s.OrganizationId)));
            }
        }

        // Public: the file is visible to any authenticated user via IsPublic or a Public-target share.
        var isPublicCommenter = file.IsPublic || await db.UploadFileShares.AsNoTracking()
            .AnyAsync(s => s.UploadFileId == uploadFileId && s.IsActive && s.TargetType == ShareTargetType.Public, ct);

        return new FileAudienceMembership(isOwner, isInvestigationTeamMember, isClient, isOrganizationMember, isPublicCommenter);
    }

    /// <summary>
    /// True if <paramref name="userId"/> can see <paramref name="uploadFileId"/> at all — the same
    /// visibility union <see cref="MediaLibraryController.GetFiles"/> computes across the whole
    /// library, scoped down to one file. Broader than <see cref="GetMembershipAsync"/>: also covers
    /// direct person-to-person shares and "any case in an org I belong to," which aren't part of
    /// the four comment audiences but do grant plain visibility.
    /// </summary>
    public static async Task<bool> CanViewFileAsync(
        BenDataContext db, Guid uploadFileId, Guid userId, CancellationToken ct)
    {
        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (file is null) return false;

        if (file.AppUserId == userId) return true;
        if (file.IsPublic) return true;

        if (await db.UploadFileShares.AsNoTracking().AnyAsync(s =>
                s.UploadFileId == uploadFileId && s.IsActive &&
                (s.TargetType == ShareTargetType.Public
                 || (s.TargetType == ShareTargetType.Person && s.TargetAppUserId == userId)), ct))
            return true;

        var membership = await GetMembershipAsync(db, uploadFileId, userId, ct);
        if (membership.IsInvestigationTeamMember || membership.IsClient || membership.IsOrganizationMember)
            return true;

        // Broader case-linked visibility: any org this user belongs to that owns a case this file
        // is linked to (CaseFile / CaseTimelineEntryFile / a published VideoProject).
        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId).ToListAsync(ct);
        if (orgIds.Count == 0) return false;

        var caseIds = await db.Cases.AsNoTracking()
            .Where(c => orgIds.Contains(c.OrganizationId))
            .Select(c => c.Id).ToListAsync(ct);
        if (caseIds.Count == 0) return false;

        return await db.CaseFiles.AsNoTracking()
                   .AnyAsync(cf => cf.UploadFileId == uploadFileId && caseIds.Contains(cf.CaseId), ct)
               || await db.CaseTimelineEntryFiles.AsNoTracking()
                   .AnyAsync(ef => ef.UploadFileId == uploadFileId && caseIds.Contains(ef.CaseTimelineEntry.CaseId), ct)
               || await db.VideoProjects.AsNoTracking()
                   .AnyAsync(p => p.PublishedUploadFileId == uploadFileId && p.CaseId.HasValue && caseIds.Contains(p.CaseId.Value), ct);
    }

    /// <summary>
    /// True if <paramref name="userId"/> is an active admin-tier (Owner or Administrator) member
    /// of <paramref name="organizationId"/>. Shared helper for the several Phase-B controllers
    /// that gate an org-scoped management action on "admin of this org" — mirrors the same
    /// <c>Role &lt;= OrganizationMemberRole.Administrator</c> tiering <see cref="GetMembershipAsync"/>
    /// already uses for the org-comment audience (line ~65 of this file).
    /// </summary>
    public static async Task<bool> IsOrgAdminAsync(
        BenDataContext db, Guid organizationId, Guid userId, CancellationToken ct)
    {
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive
                        && m.Role <= OrganizationMemberRole.Administrator, ct);
    }

    /// <summary>
    /// True if <paramref name="userId"/> is an active member (any role) of
    /// <paramref name="organizationId"/>. The DB-query half of the "is org member" check five
    /// controllers previously each hand-rolled their own private copy of
    /// (<c>CaseNoteController</c>, <c>OrgCalendarController</c> ×2, <c>InvestigationController</c>,
    /// <c>CaseTransferController</c>) — consolidated here. Callers still do their own
    /// <c>User.IsInRole(RoleNames.SuperAdmin)</c> bypass check first, since that reads the
    /// controller's own <c>ClaimsPrincipal</c> rather than the database.
    /// </summary>
    public static async Task<bool> IsOrgMemberAsync(
        BenDataContext db, Guid organizationId, Guid userId, CancellationToken ct)
    {
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive, ct);
    }
}

/// <summary>Snapshot of which audiences a user currently belongs to for one file.</summary>
public record FileAudienceMembership(
    bool IsOwner,
    bool IsInvestigationTeamMember,
    bool IsClient,
    bool IsOrganizationMember,
    bool IsPublicCommenter)
{
    /// <summary>True if any audience bool is set — used as the base "can post at all" precondition
    /// before ANDing against the file's per-audience Allow* toggles.</summary>
    public bool MatchesAnyAudience =>
        IsOwner || IsInvestigationTeamMember || IsClient || IsOrganizationMember || IsPublicCommenter;
}
