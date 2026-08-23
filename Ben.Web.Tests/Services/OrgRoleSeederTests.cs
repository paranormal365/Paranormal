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
/// Item 156 Phase C's seeder: the seven-role backfill, and the ONE-TIME grandfathering that
/// bridges existing members across Phase D's enforcement flip.
/// </summary>
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
    public async Task Backfill_gives_a_bare_org_the_seven_roles_and_grandfathers_the_member_only()
    {
        var (services, factory) = Harness();
        var w = await SeedOrgAsync(factory);

        await OrgRoleSeeder.SeedAsync(services, Config());

        await using var db = await factory.CreateDbContextAsync();
        var roles = await db.OrganizationRoles.Where(r => r.OrganizationId == w.OrgId).ToListAsync();
        Assert.Equal(8, roles.Count);   // seven defaults + Investigator
        Assert.All(roles, r => Assert.EndsWith("Role", r.Name));

        var investigator = roles.Single(r => r.Name == "Investigator Role");
        var assigned = await db.OrganizationRoleMemberships
            .Where(m => m.OrganizationRoleId == investigator.Id)
            .Select(m => m.OrganizationUserMembershipId)
            .ToListAsync();

        // The plain member is bridged; the owner and administrator are not — they bypass
        // permission checks entirely and an assignment would only muddy the roster.
        Assert.Equal([w.MemberMembershipId], assigned);
    }

    [Fact]
    public async Task Grandfathering_is_one_time_a_member_joining_later_starts_at_baseline()
    {
        var (services, factory) = Harness();
        var w = await SeedOrgAsync(factory);
        await OrgRoleSeeder.SeedAsync(services, Config());

        // Somebody joins after the flip…
        Guid lateMembership = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = lateMembership, OrganizationId = w.OrgId, AppUserId = Guid.NewGuid(),
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        // …and the seeder runs again, as it does on every host start.
        await OrgRoleSeeder.SeedAsync(services, Config());

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(1, await verify.OrganizationRoles.CountAsync(r => r.Name == "Investigator Role"));
        var assigned = await verify.OrganizationRoleMemberships
            .Select(m => m.OrganizationUserMembershipId).ToListAsync();
        Assert.DoesNotContain(lateMembership, assigned);
        Assert.Contains(w.MemberMembershipId, assigned);
    }

    [Fact]
    public async Task An_org_with_its_own_roles_keeps_them_but_is_still_grandfathered()
    {
        // The two gates are independent on purpose: an edited role list is never touched, and
        // the enforcement bridge still arrives — a group that built roles early must not lose
        // its ordinary members' case visibility for its diligence.
        var (services, factory) = Harness();
        var w = await SeedOrgAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(new OrganizationRole
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, Name = "My Custom Role",
                IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        await OrgRoleSeeder.SeedAsync(services, Config());

        await using var verify = await factory.CreateDbContextAsync();
        var names = await verify.OrganizationRoles
            .Where(r => r.OrganizationId == w.OrgId).Select(r => r.Name).ToListAsync();
        Assert.Contains("My Custom Role", names);
        Assert.DoesNotContain("Case Manager Role", names);   // backfill skipped: it had roles
        Assert.Contains("Investigator Role", names);          // grandfathering still ran
    }

    [Fact]
    public async Task The_grandfather_grant_actually_opens_case_read_through_the_real_resolver()
    {
        // The bridge is only a bridge if HasAccessAsync honors it — assert the whole chain.
        var (services, factory) = Harness();
        var w = await SeedOrgAsync(factory);
        await OrgRoleSeeder.SeedAsync(services, Config());

        Guid memberUserId;
        await using (var db = await factory.CreateDbContextAsync())
            memberUserId = (await db.OrganizationUserMemberships.FirstAsync(m => m.Id == w.MemberMembershipId)).AppUserId;

        var security = new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory);
        Assert.True(await security.HasAccessAsync(memberUserId, w.OrgId,
            OrganizationSecurityTable.Case, OrganizationSecurityAction.Read));
        Assert.False(await security.HasAccessAsync(memberUserId, w.OrgId,
            OrganizationSecurityTable.Case, OrganizationSecurityAction.Delete));
    }
}
