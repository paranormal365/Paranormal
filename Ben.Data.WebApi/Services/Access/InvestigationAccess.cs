using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>
/// Who may edit an investigation, and what a viewer is allowed to do with each row in a list.
/// </summary>
/// <remarks>
/// <para>Editing used to mean "any active member of the organization". That is too loose once
/// investigations become a shared, mapped record rather than a detail of one case: a group of forty
/// should not have forty people able to move Tuesday's visit. Five ways to earn it, and no others:
/// the person who scheduled it, the manager of its case, the lead of that particular visit, an
/// owner or administrator of the group, and anyone the group has granted <c>Update</c> on the
/// <see cref="OrganizationSecurityTable.Investigation"/> table.</para>
///
/// <para>A plain static helper rather than an injectable service, matching the convention
/// <see cref="FileAudienceAccess"/> and <see cref="CaseOrgAccess"/> already set here.</para>
///
/// <para><b>Reads are not gated by this.</b> Members can see all of their group's investigations;
/// it is only changing one that is narrowed. And a person's own RSVP stays self-gated — answering
/// an invitation is not managing the visit.</para>
/// </remarks>
public static class InvestigationAccess
{
    /// <summary>
    /// Whether <paramref name="userId"/> may change this investigation.
    /// </summary>
    /// <param name="db">Context for the lookups.</param>
    /// <param name="investigationId">The investigation being changed.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="isSuperAdmin">Passed in rather than queried — the caller has the claim already.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<bool> CanManageAsync(
        BenDataContext db, Guid investigationId, Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        if (isSuperAdmin) return true;
        if (userId == Guid.Empty) return false;

        var investigation = await db.Investigations.AsNoTracking()
            .Where(i => i.Id == investigationId)
            .Select(i => new { i.Id, i.OrganizationId, i.CaseId, i.CreatedByAppUserId })
            .FirstOrDefaultAsync(ct);
        if (investigation is null) return false;

        // Whoever scheduled it. The commonest case by far, and checked first so the ordinary path
        // costs one query.
        if (investigation.CreatedByAppUserId == userId) return true;

        // The manager of the case it belongs to — they are accountable for the case's schedule.
        if (investigation.CaseId is { } caseId
            && await db.Cases.AsNoTracking()
                .AnyAsync(c => c.Id == caseId && c.CaseManagerAppUserId == userId, ct))
            return true;

        // The lead of this particular visit. Delegated authority that expires with it — see
        // InvestigationAttendee.IsLead for why this is not the same as standing rank.
        if (await db.InvestigationAttendees.AsNoTracking()
                .AnyAsync(a => a.InvestigationId == investigationId && a.AppUserId == userId && a.IsLead, ct))
            return true;

        return await HasOrgAuthorityAsync(db, investigation.OrganizationId, userId, ct);
    }

    /// <summary>
    /// The per-row permissions for a whole list, computed in a fixed number of queries.
    /// </summary>
    /// <remarks>
    /// Batched deliberately. The obvious implementation calls <see cref="CanManageAsync"/> per row
    /// and turns a forty-investigation page into a couple of hundred queries; this loads the
    /// caller's memberships, grants and attendances once and answers from memory. The flags are
    /// computed on the server because the UI must render a verdict, never derive one — a client
    /// that decides for itself who may edit is a client that can be told otherwise.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<Guid, InvestigationPermissionFlags>> ComputeFlagsAsync(
        BenDataContext db, Guid organizationId, IReadOnlyCollection<Guid> investigationIds,
        Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        if (investigationIds.Count == 0)
            return new Dictionary<Guid, InvestigationPermissionFlags>();

        var rows = await db.Investigations.AsNoTracking()
            .Where(i => investigationIds.Contains(i.Id))
            .Select(i => new { i.Id, i.CaseId, i.CreatedByAppUserId })
            .ToListAsync(ct);

        // Org-wide authority answers every row at once, so establish it before touching per-row data.
        var hasOrgAuthority = isSuperAdmin
            || await HasOrgAuthorityAsync(db, organizationId, userId, ct);

        var myAttendances = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId && investigationIds.Contains(a.InvestigationId))
            .Select(a => new { a.InvestigationId, a.IsLead })
            .ToListAsync(ct);

        var leadOf = myAttendances.Where(a => a.IsLead).Select(a => a.InvestigationId).ToHashSet();
        var attending = myAttendances.Select(a => a.InvestigationId).ToHashSet();

        var caseIds = rows.Where(r => r.CaseId is not null).Select(r => r.CaseId!.Value).Distinct().ToList();
        var managedCaseIds = caseIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.Cases.AsNoTracking()
                .Where(c => caseIds.Contains(c.Id) && c.CaseManagerAppUserId == userId)
                .Select(c => c.Id)
                .ToListAsync(ct)).ToHashSet();

        return rows.ToDictionary(
            r => r.Id,
            r => new InvestigationPermissionFlags(
                CanEditRecord:
                    hasOrgAuthority
                    || r.CreatedByAppUserId == userId
                    || leadOf.Contains(r.Id)
                    || (r.CaseId is { } cid && managedCaseIds.Contains(cid)),
                // Recording your own findings is a participant's right, not a manager's. Someone
                // who was there has something to say about it whether or not they run anything.
                CanCompleteMyFindings: attending.Contains(r.Id)));
    }

    /// <summary>
    /// Owner/Administrator of the group, or an explicit grant of <c>Update</c> on the
    /// <see cref="OrganizationSecurityTable.Investigation"/> table — by direct grant or by role.
    /// </summary>
    private static async Task<bool> HasOrgAuthorityAsync(
        BenDataContext db, Guid organizationId, Guid userId, CancellationToken ct)
    {
        var membership = await db.OrganizationUserMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId
                                   && m.AppUserId == userId && m.IsActive, ct);
        if (membership is null) return false;

        if (membership.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Administrator)
            return true;

        var hasDirectGrant = await db.OrganizationAccessGrants.AsNoTracking()
            .AnyAsync(g => g.OrganizationId == organizationId
                        && g.AppUserId == userId
                        && g.TableName == OrganizationSecurityTable.Investigation
                        && (g.Actions & OrganizationSecurityAction.Update) != OrganizationSecurityAction.None, ct);
        if (hasDirectGrant) return true;

        // Named roles, OR'd across every active role the member holds — the same shape
        // OrganizationSecurityService uses, so a grant behaves identically wherever it is read.
        return await (
            from roleMembership in db.OrganizationRoleMemberships.AsNoTracking()
            join role in db.OrganizationRoles.AsNoTracking()
                on roleMembership.OrganizationRoleId equals role.Id
            join permission in db.OrganizationRolePermissions.AsNoTracking()
                on role.Id equals permission.OrganizationRoleId
            where roleMembership.OrganizationUserMembershipId == membership.Id
                && role.IsActive
                && permission.TableName == OrganizationSecurityTable.Investigation
                && (permission.Actions & OrganizationSecurityAction.Update) != OrganizationSecurityAction.None
            select roleMembership.Id).AnyAsync(ct);
    }
}

/// <summary>
/// What one viewer may do with one investigation. Computed server-side and sent as a verdict.
/// </summary>
/// <param name="CanEditRecord">May change the schedule, details, attendees and status.</param>
/// <param name="CanCompleteMyFindings">
/// Was there, so may record their own notes and readings — independent of <paramref name="CanEditRecord"/>.
/// </param>
public readonly record struct InvestigationPermissionFlags(
    bool CanEditRecord,
    bool CanCompleteMyFindings);
