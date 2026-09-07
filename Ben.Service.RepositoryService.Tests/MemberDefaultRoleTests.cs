using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;
using MemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// What a person can actually do the moment they become a member (site evaluation 2026-09-06, W-M1).
/// </summary>
/// <remarks>
/// <para>A membership rank opens nothing below Administrator. <see cref="OrganizationSecurityService.HasAccessAsync"/>
/// waves Owner and Administrator through and then asks for a functional role or a direct grant, so
/// somebody added as a Member or a Viewer had <c>Case.Read</c> false — while their desk listed the
/// group's cases as links and a visit roster named them Lead Investigator. Every one of those doors
/// led to a page the API refused.</para>
///
/// <para>These tests are written against the permission answer, not against the row: the row is an
/// implementation detail, and "can this person read a case" is the thing that was wrong.</para>
/// </remarks>
public class MemberDefaultRoleTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>
    /// An organization with an Owner, its default roles, and its starting role chosen — exactly
    /// what <see cref="NewOrganizationDefaults"/> produces for a group created today.
    /// </summary>
    private static async Task<(Guid OrgId, Guid OwnerId, Guid JoinerId)> SeedAsync(
        IDbContextFactory<BenDataContext> factory, bool chooseAStartingRole = true)
    {
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[] { (ownerId, "Owner"), (joinerId, "Joiner") })
            db.AppUsers.Add(new AppUser
            {
                Id = id, UserName = id.ToString(), Email = $"{id}@test.com", DisplayName = name,
            });

        var org = new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = $"org-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        };
        db.Organizations.Add(org);
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = MemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });

        if (chooseAStartingRole)
        {
            // Saves twice on purpose — see AddAllAsync: a group and the role it points at cannot
            // be inserted in one batch, because each references the other.
            await NewOrganizationDefaults.AddAllAsync(db, org, ownerId);
        }
        else
        {
            NewOrganizationDefaults.AddAll(db, orgId, ownerId);
            await db.SaveChangesAsync();
        }
        return (orgId, ownerId, joinerId);
    }

    // ── The setting itself ───────────────────────────────────────────────────

    [Fact]
    public async Task A_new_group_starts_people_on_the_investigator_role()
    {
        var factory = CreateFactory();
        var (orgId, _, _) = await SeedAsync(factory);

        await using var db = await factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == orgId);

        Assert.NotNull(org.DefaultMemberRoleId);
        var chosen = await db.OrganizationRoles.SingleAsync(r => r.Id == org.DefaultMemberRoleId);
        Assert.Equal(OrgRoleDefaults.DefaultMemberRoleName, chosen.Name);
    }

    // ── Door one: the security service's upsert ──────────────────────────────

    [Fact]
    public async Task A_member_added_through_the_security_door_can_read_the_cases_their_desk_lists()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);
        var security = new OrganizationSecurityService(factory);

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);

        Assert.True(await security.HasAccessAsync(joinerId, orgId, DataTable.Case, DataAction.Read),
            "A new member could not read the group's cases — the W-M1 blank page is back.");
    }

    [Fact]
    public async Task A_viewer_added_through_the_security_door_can_read_them_too()
    {
        // W-VW1 was the same failure wearing a different rank.
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);
        var security = new OrganizationSecurityService(factory);

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Viewer, true, ownerId);

        Assert.True(await security.HasAccessAsync(joinerId, orgId, DataTable.Case, DataAction.Read));
    }

    [Fact]
    public async Task A_group_that_chose_no_starting_role_is_left_exactly_as_it_was()
    {
        // Every group that existed before the setting did has none, and none of them should
        // silently gain permissions because this shipped.
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory, chooseAStartingRole: false);
        var security = new OrganizationSecurityService(factory);

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);

        Assert.False(await security.HasAccessAsync(joinerId, orgId, DataTable.Case, DataAction.Read));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.OrganizationRoleMemberships.ToListAsync());
    }

    [Fact]
    public async Task An_administrator_is_not_given_a_role_row_they_do_not_need()
    {
        // Rank already opens everything for them; a row here would be noise on the Members grid.
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);
        var security = new OrganizationSecurityService(factory);

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Administrator, true, ownerId);

        Assert.True(await security.HasAccessAsync(joinerId, orgId, DataTable.Case, DataAction.Read));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.OrganizationRoleMemberships.ToListAsync());
    }

    [Fact]
    public async Task Changing_somebodys_rank_does_not_hand_them_the_starting_role_again()
    {
        // Creation only. An owner who picks a default expects it to apply to the people who join
        // next — not to reach back into a roster they have already tuned by hand.
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);
        var security = new OrganizationSecurityService(factory);

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var granted = await db.OrganizationRoleMemberships.SingleAsync();
            db.OrganizationRoleMemberships.Remove(granted);
            await db.SaveChangesAsync();
        }

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Viewer, true, ownerId);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.OrganizationRoleMemberships.ToListAsync());
    }

    // ── The guards on the setting itself ─────────────────────────────────────

    [Fact]
    public async Task A_starting_role_that_belongs_to_another_group_is_ignored()
    {
        // A stale or crafted setting must never write a grant pointing at somebody else's role.
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);
        var (otherOrgId, _, _) = await SeedAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var theirRoleId = await db.Organizations
                .Where(o => o.Id == otherOrgId).Select(o => o.DefaultMemberRoleId).SingleAsync();
            var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
            org.DefaultMemberRoleId = theirRoleId;
            await db.SaveChangesAsync();
        }

        var security = new OrganizationSecurityService(factory);
        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.OrganizationRoleMemberships
            .Where(rm => rm.OrganizationUserMembership.OrganizationId == orgId).ToListAsync());
    }

    [Fact]
    public async Task A_starting_role_that_has_been_switched_off_is_ignored()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
            var role = await db.OrganizationRoles.SingleAsync(r => r.Id == org.DefaultMemberRoleId);
            role.IsActive = false;
            await db.SaveChangesAsync();
        }

        var security = new OrganizationSecurityService(factory);
        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.OrganizationRoleMemberships.ToListAsync());
    }

    [Fact]
    public async Task Somebody_re_added_to_a_group_does_not_collect_the_role_twice()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, joinerId) = await SeedAsync(factory);
        var security = new OrganizationSecurityService(factory);

        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);
        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, false, ownerId);
        await security.UpsertMembershipAsync(orgId, joinerId, MemberRole.Member, true, ownerId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.OrganizationRoleMemberships.ToListAsync());
    }
}
