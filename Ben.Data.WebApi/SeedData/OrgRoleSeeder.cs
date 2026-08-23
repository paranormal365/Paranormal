using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Backfills the seven default roles for organizations that predate item 156 Phase C, and runs
/// the one-time grandfathering Ben approved for Phase D's enforcement flip.
/// </summary>
/// <remarks>
/// <para><b>Backfill:</b> a group with ANY roles is left entirely alone (its role list is its
/// own); a group with none gets the seven defaults.</para>
///
/// <para><b>Grandfathering, once per group:</b> when Phase D flips enforcement, ordinary members
/// lose the historical is-member case visibility unless something bridges them. The bridge is an
/// <b>Investigator Role</b> (Cases + Investigations Read) created here and assigned to every
/// ACTIVE non-admin member the group has at that moment. The whole block is gated on the role's
/// absence, which is what makes it one-time: members who join after the role exists start at
/// the baseline and are handed the role by a person, not a seeder. Owners and administrators are
/// skipped — they bypass permission checks entirely (decision D2).</para>
/// </remarks>
internal static class OrgRoleSeeder
{
    internal const string InvestigatorRoleName = "Investigator Role";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // ── Backfill the seven defaults ───────────────────────────────────────
        var orgsWithRoles = await db.OrganizationRoles
            .Select(r => r.OrganizationId).Distinct().ToListAsync();
        var bare = await db.Organizations
            .Where(o => !orgsWithRoles.Contains(o.Id))
            .Select(o => new { o.Id, o.CreatedByAppUserId })
            .ToListAsync();
        foreach (var org in bare)
            OrgRoleDefaults.AddDefaultRoles(db, org.Id, org.CreatedByAppUserId);
        if (bare.Count > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[OrgRoleSeeder] Backfilled the seven default roles for {bare.Count} organization(s).");
        }

        // ── Grandfathering (one-time per group, gated on the role's absence) ──
        var orgsWithInvestigator = await db.OrganizationRoles
            .Where(r => r.Name == InvestigatorRoleName)
            .Select(r => r.OrganizationId).Distinct().ToListAsync();

        var allOrgs = await db.Organizations
            .Where(o => !orgsWithInvestigator.Contains(o.Id))
            .Select(o => new { o.Id, o.CreatedByAppUserId })
            .ToListAsync();

        var granted = 0;
        foreach (var org in allOrgs)
        {
            var now = DateTime.UtcNow;
            var role = new OrganizationRole
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id,
                Name = InvestigatorRoleName,
                Description = "Reads the group's cases and investigations. Assigned to everyone "
                    + "who was already a member when role-based case access arrived, so the "
                    + "change took nothing from anyone; hand it to new members as they earn it.",
                IsActive = true, SortOrder = 100,
                DateCreated = now, CreatedByAppUserId = org.CreatedByAppUserId,
            };
            db.OrganizationRoles.Add(role);
            db.OrganizationRolePermissions.AddRange(
                new OrganizationRolePermission
                {
                    Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                    TableName = OrganizationSecurityTable.Case,
                    Actions = OrganizationSecurityAction.Read,
                    DateCreated = now, CreatedByAppUserId = org.CreatedByAppUserId,
                },
                new OrganizationRolePermission
                {
                    Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                    TableName = OrganizationSecurityTable.Investigation,
                    Actions = OrganizationSecurityAction.Read,
                    DateCreated = now, CreatedByAppUserId = org.CreatedByAppUserId,
                });

            var grandfathered = await db.OrganizationUserMemberships
                .Where(m => m.OrganizationId == org.Id && m.IsActive
                         && m.Role != OrganizationMemberRole.Owner
                         && m.Role != OrganizationMemberRole.Administrator)
                .Select(m => m.Id)
                .ToListAsync();

            foreach (var membershipId in grandfathered)
            {
                db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
                {
                    Id = Guid.NewGuid(),
                    OrganizationRoleId = role.Id,
                    OrganizationUserMembershipId = membershipId,
                    DateCreated = now, CreatedByAppUserId = org.CreatedByAppUserId,
                });
                granted++;
            }
        }

        if (allOrgs.Count > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine(
                $"[OrgRoleSeeder] Grandfathered {granted} member(s) across {allOrgs.Count} organization(s) with the Investigator Role.");
        }
    }
}
