using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The default-role backfill — and the grandfathering that is deliberately no longer there.
/// </summary>
/// <remarks>
/// <para>These tests used to assert the opposite. The seeder created an Investigator Role and
/// handed it to every active non-admin member, so item 156 Phase D's enforcement flip took nothing
/// from anyone.</para>
///
/// <para><b>Ben ended that on 2026-08-26:</b> "Currently I am the only actual person using the
/// site. Keep me as the super admin then change the security settings instead of grandfathering
/// anyone." Roles are authoritative now — a member holds exactly what somebody gave them, and a
/// read grant can restrict rather than only add, which was the whole point of IH-03.</para>
///
/// <para>Rewritten rather than deleted, because the new rule needs guarding just as much as the
/// old one did: a seeder that starts quietly granting case access again would undo the decision
/// without anybody noticing.</para>
/// </remarks>
public sealed class OrgRoleSeederTests
{
    private static (IServiceProvider Services, IDbContextFactory<BenDataContext> Factory) Harness()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new PooledDbContextFactory<BenDataContext>(options);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BenDataContext>>(factory);
        return (services.BuildServiceProvider(), factory);
    }

    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    private sealed record World(Guid OrgId, Guid OwnerMembershipId, Guid MemberMembershipId, Guid AdminMembershipId);

    private static async Task<World> SeedOrgAsync(IDbContextFactory<BenDataContext> factory)
    {
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ownerM = Guid.NewGuid();
        var memberM = Guid.NewGuid();
        var adminM = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = $"g-{orgId:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = ownerM, OrganizationId = orgId, AppUserId = ownerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId },
            new OrganizationUserMembership { Id = adminM, OrganizationId = orgId, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId },
            new OrganizationUserMembership { Id = memberM, OrganizationId = orgId, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
        await db.SaveChangesAsync();
        return new World(orgId, ownerM, memberM, adminM);
    }

    [Fact]
    public async Task Backfill_gives_a_bare_org_the_default_roles()
    {
        var (services, factory) = Harness();
        var world = await SeedOrgAsync(factory);

        await OrgRoleSeeder.SeedAsync(services, Config());

        await using var db = await factory.CreateDbContextAsync();
        var roles = await db.OrganizationRoles.Where(r => r.OrganizationId == world.OrgId).ToListAsync();
        // Counted from the source of truth, not a literal: the list grew from seven to
        // eight when the Investigator Role joined it (IH-03 step 5 aftermath), and a hardcoded
        // seven made ADDING a default look like a regression.
        Assert.Equal(Ben.Data.Source.Services.OrgRoleDefaults.Defaults.Count, roles.Count);
    }

    /// <summary>
    /// Roles are CREATED. Nobody is put in them.
    /// </summary>
    /// <remarks>
    /// The heart of Ben's decision. Before, every active non-admin member came out of this holding
    /// an Investigator Role granting Cases and Investigations read — which meant a read grant could
    /// only ever ADD, and taking a role away from somebody changed nothing they could see.
    /// </remarks>
    [Fact]
    public async Task Backfill_puts_nobody_in_any_role()
    {
        var (services, factory) = Harness();
        var world = await SeedOrgAsync(factory);

        await OrgRoleSeeder.SeedAsync(services, Config());

        await using var db = await factory.CreateDbContextAsync();

        // The heart of the rule is the FIRST assertion: however many roles exist, nobody holds
        // one. The old second assertion — that no Investigator Role exists at all — described the
        // world where creating that role and grandfathering people into it were the same act.
        // They are separate now: the role IS a default (a fresh group needs something to hand its
        // ordinary members), and what must never come back is the automatic membership.
        Assert.Empty(db.OrganizationRoleMemberships);
        Assert.True(await db.OrganizationRoles
            .AnyAsync(r => r.OrganizationId == world.OrgId && r.Name == "Investigator Role"));
    }

    /// <summary>
    /// And a plain member really is refused, asked through the resolver the server uses.
    /// </summary>
    /// <remarks>
    /// The end-to-end version of the test above: not "no rows exist" but "the answer is no". This
    /// is the behaviour Ben asked for — a member sees the group's cases when somebody grants it,
    /// and not before.
    /// </remarks>
    [Fact]
    public async Task A_plain_member_cannot_read_cases_until_somebody_grants_it()
    {
        var (services, factory) = Harness();
        var world = await SeedOrgAsync(factory);
        await OrgRoleSeeder.SeedAsync(services, Config());

        Guid memberUserId;
        await using (var db = await factory.CreateDbContextAsync())
            memberUserId = (await db.OrganizationUserMemberships.FindAsync(world.MemberMembershipId))!.AppUserId;

        var security = new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory);

        Assert.False(await security.HasAccessAsync(
            memberUserId, world.OrgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Read));

        // Belonging is still true — the distinction the old helper names blurred.
        Assert.True(await security.BelongsToAsync(memberUserId, world.OrgId));
    }

    /// <summary>An organization that already has roles keeps exactly those.</summary>
    [Fact]
    public async Task An_org_with_its_own_roles_keeps_them_untouched()
    {
        var (services, factory) = Harness();
        var world = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(new OrganizationRole
            {
                Id = Guid.NewGuid(), OrganizationId = world.OrgId, Name = "Only Ours",
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        await OrgRoleSeeder.SeedAsync(services, Config());

        await using var check = await factory.CreateDbContextAsync();
        var roles = await check.OrganizationRoles.Where(r => r.OrganizationId == world.OrgId).ToListAsync();
        Assert.Single(roles);
        Assert.Equal("Only Ours", roles[0].Name);
    }
}
