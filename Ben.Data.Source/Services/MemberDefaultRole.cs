using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.Source.Services;

/// <summary>
/// Gives a brand-new membership the functional role its group starts people with
/// (site evaluation 2026-09-06, W-M1).
/// </summary>
/// <remarks>
/// <para><b>The gap this closes.</b> A membership carries a rank, and rank alone opens nothing
/// below Administrator: <c>OrganizationSecurityService.HasAccessAsync</c> waves Owner and
/// Administrator through and then requires a functional role or a direct grant. So a person added
/// as Member or Viewer had <c>Case.Read</c> false — while their desk listed the group's cases as
/// links and a visit roster named them Lead Investigator. Every one of those doors led to a page
/// the API refused.</para>
///
/// <para><b>One place, called by every door.</b> There are three that create a membership — the
/// security endpoint's upsert, an accepted membership application, and the seeders — and each had
/// its own copy of the "add the row" code and none of them thought about permissions. A rule
/// spread over three call sites is a rule that will be right in two of them.</para>
///
/// <para><b>Adds, does not save</b>, like every other Add* helper here: the caller owns the
/// transaction, because a membership is never written on its own.</para>
/// </remarks>
public static class MemberDefaultRole
{
    /// <summary>
    /// Stages the group's default functional role for a membership that has just been created.
    /// </summary>
    /// <remarks>
    /// <para>Does nothing, deliberately, when:</para>
    /// <list type="bullet">
    /// <item>the group has not chosen a default — which is every group that existed before the
    /// setting did, so no group's behaviour changes until somebody picks one;</item>
    /// <item>the rank is Owner or Administrator, who already bypass every check, and for whom a
    /// role row would be a line of noise on the Members grid;</item>
    /// <item>the chosen role has been deleted, deactivated, or belongs to another group — a
    /// stale setting must not write a grant pointing at somebody else's role;</item>
    /// <item>this membership already holds that role.</item>
    /// </list>
    ///
    /// <para><b>Creation only.</b> A rank change never adds or removes the default: an owner who
    /// picks a new default expects it to apply to the people who join next, not to reach back and
    /// re-permission a roster they have already tuned by hand.</para>
    /// </remarks>
    /// <returns>True when a role membership was staged.</returns>
    public static async Task<bool> ApplyAsync(
        BenDataContext db, Organization organization, OrganizationUserMembership membership,
        Guid actingUserId, CancellationToken token = default)
    {
        if (organization.DefaultMemberRoleId is not { } roleId) return false;
        if (membership.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Administrator)
            return false;

        var roleIsUsable = await db.OrganizationRoles
            .AsNoTracking()
            .AnyAsync(r => r.Id == roleId
                        && r.OrganizationId == organization.Id
                        && r.IsActive, token);
        if (!roleIsUsable) return false;

        var alreadyHeld = await db.OrganizationRoleMemberships
            .AsNoTracking()
            .AnyAsync(rm => rm.OrganizationRoleId == roleId
                         && rm.OrganizationUserMembershipId == membership.Id, token);
        if (alreadyHeld) return false;

        db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
        {
            Id                           = Guid.NewGuid(),
            OrganizationRoleId           = roleId,
            OrganizationUserMembershipId = membership.Id,
            DateCreated                  = DateTime.UtcNow,
            CreatedByAppUserId           = actingUserId,
        });

        return true;
    }

    /// <summary>
    /// The same, for a caller holding only the organization's id.
    /// </summary>
    /// <remarks>
    /// Loads the organization untracked — this is called on paths that have the id from a route
    /// and no reason to have fetched the row.
    /// </remarks>
    public static async Task<bool> ApplyAsync(
        BenDataContext db, Guid organizationId, OrganizationUserMembership membership,
        Guid actingUserId, CancellationToken token = default)
    {
        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, token);

        return organization is not null
            && await ApplyAsync(db, organization, membership, actingUserId, token);
    }
}
