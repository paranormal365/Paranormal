using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which permission areas a group's plan includes (item 156 Phase A). The fail-open cases are
/// the point: only a tier whose checklist SAYS so may exclude anything.
/// </summary>
public sealed class IncludedAreasResolverTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static async Task<Guid> SeedOrgAsync(IDbContextFactory<BenDataContext> factory, int members = 3)
    {
        var orgId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = $"g-{orgId:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
        for (var i = 0; i < members; i++)
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = Guid.NewGuid(),
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
        await db.SaveChangesAsync();
        return orgId;
    }

    private static async Task<Guid> SeedTierAsync(
        IDbContextFactory<BenDataContext> factory, int min, int? max,
        params OrganizationPermissionArea[] areas)
    {
        var tierId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = $"T{min}", MinMembers = min, MaxMembers = max,
            SortOrder = min, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        foreach (var area in areas)
            db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
            {
                SubscriptionTierId = tierId, Area = area,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
        await db.SaveChangesAsync();
        return tierId;
    }

    private static async Task SubscribeAsync(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid tierId)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, SubscriptionTierId = tierId,
            Status = SubscriptionStatus.Active,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_subscribed_group_gets_exactly_its_tiers_checklist()
    {
        var factory = CreateFactory();
        var orgId = await SeedOrgAsync(factory);
        var tierId = await SeedTierAsync(factory, 1, null,
            OrganizationPermissionArea.Cases, OrganizationPermissionArea.Equipment);
        await SubscribeAsync(factory, orgId, tierId);

        var areas = await new IncludedAreasResolver(factory).ForOrganizationAsync(orgId);

        Assert.Equal(
            new[] { OrganizationPermissionArea.Cases, OrganizationPermissionArea.Equipment }.ToHashSet(),
            areas);
    }

    [Fact]
    public async Task With_no_subscription_the_member_resolved_tier_decides()
    {
        var factory = CreateFactory();
        var orgId = await SeedOrgAsync(factory, members: 3);
        await SeedTierAsync(factory, 1, 5, OrganizationPermissionArea.Cases);           // small: 1-5
        await SeedTierAsync(factory, 6, null, OrganizationPermissionArea.Equipment);    // large: 6+

        var areas = await new IncludedAreasResolver(factory).ForOrganizationAsync(orgId);

        Assert.Equal(new[] { OrganizationPermissionArea.Cases }.ToHashSet(), areas);
    }

    [Fact]
    public async Task With_no_tiers_configured_everything_is_included()
    {
        var factory = CreateFactory();
        var orgId = await SeedOrgAsync(factory);

        var areas = await new IncludedAreasResolver(factory).ForOrganizationAsync(orgId);

        Assert.Equal(Enum.GetValues<OrganizationPermissionArea>().ToHashSet(), areas);
    }

    [Fact]
    public async Task A_tier_with_no_checklist_rows_reads_as_all_inclusive()
    {
        // Zero rows means "never configured", not "includes nothing" — a deliberate exclusion
        // is a checklist that says so.
        var factory = CreateFactory();
        var orgId = await SeedOrgAsync(factory);
        var tierId = await SeedTierAsync(factory, 1, null /* no areas */);
        await SubscribeAsync(factory, orgId, tierId);

        var areas = await new IncludedAreasResolver(factory).ForOrganizationAsync(orgId);

        Assert.Equal(Enum.GetValues<OrganizationPermissionArea>().ToHashSet(), areas);
    }

    [Fact]
    public async Task IsIncluded_maps_tables_through_their_area_and_never_gates_user_scoped_ones()
    {
        var factory = CreateFactory();
        var orgId = await SeedOrgAsync(factory);
        var tierId = await SeedTierAsync(factory, 1, null, OrganizationPermissionArea.Cases);
        await SubscribeAsync(factory, orgId, tierId);
        var resolver = new IncludedAreasResolver(factory);

        Assert.True(await resolver.IsIncludedAsync(orgId, OrganizationSecurityTable.Case));
        Assert.False(await resolver.IsIncludedAsync(orgId, OrganizationSecurityTable.Equipment));
        // User-scoped values belong to no org and are never tier-gated.
        Assert.True(await resolver.IsIncludedAsync(orgId, OrganizationSecurityTable.UserEmail));
    }
}
