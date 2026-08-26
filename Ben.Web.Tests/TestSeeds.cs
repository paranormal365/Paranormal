using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Web.Tests;

/// <summary>
/// Mirrors item 156 Phase C's grandfather bridge inside unit-test seeds: every active
/// non-admin membership in the org gets a role reading Cases and Investigations.
/// </summary>
/// <remarks>
/// Phase D made case/investigation reads answer to HasAccessAsync instead of bare membership.
/// Production members kept their access through the Investigator Role the seeder assigned;
/// tests that seed "a member who can see the case" must model the same world, or they model a
/// member the flip deliberately excludes. Tests about STRANGERS keep seeding strangers — this
/// touches members only.
/// </remarks>
public static class TestSeeds
{
    /// <summary>Read on Cases and Investigations — what the bridge itself granted.</summary>
    public const OrganizationSecurityAction ReadOnly = OrganizationSecurityAction.Read;

    /// <summary>
    /// Everything a working investigator does to a case: read it, add to it, change it, remove
    /// from it.
    /// </summary>
    /// <remarks>
    /// Needed from IH-03 step 2 onward, when case notes, files, research, reports, mixes and
    /// schedule proposals stopped gating their writes on the READ grant. A suite whose subject is
    /// "does upload work", not "who may upload", seeds this and goes back to testing its subject.
    /// Suites about refusal keep seeding <see cref="ReadOnly"/>, or no grant at all.
    /// </remarks>
    public const OrganizationSecurityAction CaseWork =
        OrganizationSecurityAction.Read | OrganizationSecurityAction.Create
        | OrganizationSecurityAction.Update | OrganizationSecurityAction.Delete;

    /// <summary>
    /// Grants one user one table's actions directly, outside any role.
    /// </summary>
    /// <remarks>
    /// <see cref="BridgeAsync"/> covers Cases and Investigations, which is most of what suites
    /// need. This is for the areas it does not reach — the calendar, equipment, membership — where
    /// a suite needs its member able to do the one thing the suite is about.
    /// </remarks>
    public static async Task GrantAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid appUserId,
        OrganizationSecurityTable table, OrganizationSecurityAction actions)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = appUserId,
            TableName = table, Actions = actions,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = appUserId,
        });
        await db.SaveChangesAsync();
    }

    public static async Task BridgeAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId,
        OrganizationSecurityAction actions = ReadOnly)
    {
        await using var db = await factory.CreateDbContextAsync();
        var owner = await db.Organizations.Where(o => o.Id == orgId)
            .Select(o => o.CreatedByAppUserId).FirstAsync();

        var role = new OrganizationRole
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Investigator Role",
            IsActive = true, SortOrder = 100,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        };
        db.OrganizationRoles.Add(role);
        foreach (var table in new[] { OrganizationSecurityTable.Case, OrganizationSecurityTable.Investigation })
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission
            {
                Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                TableName = table, Actions = actions,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });

        var memberships = await db.OrganizationUserMemberships
            .Where(m => m.OrganizationId == orgId && m.IsActive
                     && m.Role != OrganizationMemberRole.Owner
                     && m.Role != OrganizationMemberRole.Administrator)
            .Select(m => m.Id).ToListAsync();
        foreach (var membershipId in memberships)
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
        await db.SaveChangesAsync();
    }
}
