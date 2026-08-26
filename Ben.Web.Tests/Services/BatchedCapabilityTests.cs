using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Resolving one capability for a whole listing (item 194) must agree with resolving it one
/// group at a time — and must fail open in every ambiguous case, exactly as the single-group
/// method does.
/// </summary>
/// <remarks>
/// The group finder now says on every card whether a group can take private-residence work, so
/// somebody with a haunted home learns it before they pick rather than after. Asking per card
/// would be the N+1 that turns a browse page into forty round trips, so the answer is batched —
/// and a batched rewrite of a security-adjacent rule is exactly where the two implementations
/// drift. These tests pin them together.
/// </remarks>
public class BatchedCapabilityTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(IDbContextFactory<BenDataContext> F, Guid Free, Guid Paid)> SeedAsync(
        bool seedTiers = true, bool excludeFromFree = true)
    {
        var f = CreateFactory();
        var owner = Guid.NewGuid();
        var freeTierId = Guid.NewGuid();
        var paidTierId = Guid.NewGuid();
        var freeOrg = Guid.NewGuid();
        var paidOrg = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        foreach (var (id, name) in new[] { (freeOrg, "Free"), (paidOrg, "Paid") })
            db.Organizations.Add(new Organization
            {
                Id = id, Name = name, UrlName = $"{name}-{id:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });

        if (seedTiers)
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = freeTierId, Name = "Free", MinMembers = 0, MaxMembers = 5,
                SortOrder = 1, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = paidTierId, Name = "Small group", MinMembers = 6, MaxMembers = null,
                SortOrder = 2, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });

            if (excludeFromFree)
                db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
                {
                    Id = Guid.NewGuid(), SubscriptionTierId = freeTierId,
                    Capability = TierCapability.PrivateResidenceCases,
                    DateCreated = DateTime.UtcNow,
                });

            // Each org pinned to a tier by subscription, so member counts do not decide it.
            foreach (var (orgId, tierId) in new[] { (freeOrg, freeTierId), (paidOrg, paidTierId) })
                db.OrganizationSubscriptions.Add(new OrganizationSubscription
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId, SubscriptionTierId = tierId,
                    Status = SubscriptionStatus.Active,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
                });
        }

        await db.SaveChangesAsync();
        return (f, freeOrg, paidOrg);
    }

    [Fact]
    public async Task The_batch_separates_the_excluded_tier_from_the_included_one()
    {
        var (f, free, paid) = await SeedAsync();
        await using var db = await f.CreateDbContextAsync();

        var holders = await TierAreaResolution.WithCapabilityAsync(
            db, [free, paid], TierCapability.PrivateResidenceCases);

        Assert.Contains(paid, holders);
        Assert.DoesNotContain(free, holders);
    }

    /// <summary>The batch and the single-group method must never disagree.</summary>
    [Fact]
    public async Task The_batch_agrees_with_asking_one_at_a_time()
    {
        var (f, free, paid) = await SeedAsync();
        await using var db = await f.CreateDbContextAsync();

        var holders = await TierAreaResolution.WithCapabilityAsync(
            db, [free, paid], TierCapability.PrivateResidenceCases);

        foreach (var orgId in new[] { free, paid })
        {
            var (single, _) = await TierAreaResolution.HasCapabilityAsync(
                db, orgId, TierCapability.PrivateResidenceCases);
            Assert.Equal(single, holders.Contains(orgId));
        }
    }

    /// <summary>
    /// No tiers configured at all: everybody holds everything.
    /// </summary>
    /// <remarks>
    /// The fail-open rule, and the state every database is in before Ben configures pricing. A
    /// batch that answered "nobody" here would brand every group on the finder as public-only.
    /// </remarks>
    [Fact]
    public async Task With_no_tiers_configured_every_group_holds_the_capability()
    {
        var (f, free, paid) = await SeedAsync(seedTiers: false);
        await using var db = await f.CreateDbContextAsync();

        var holders = await TierAreaResolution.WithCapabilityAsync(
            db, [free, paid], TierCapability.PrivateResidenceCases);

        Assert.Contains(free, holders);
        Assert.Contains(paid, holders);
    }

    /// <summary>Tiers exist but exclude nothing — still everybody.</summary>
    [Fact]
    public async Task A_tier_with_no_exclusion_row_holds_the_capability()
    {
        var (f, free, paid) = await SeedAsync(excludeFromFree: false);
        await using var db = await f.CreateDbContextAsync();

        var holders = await TierAreaResolution.WithCapabilityAsync(
            db, [free, paid], TierCapability.PrivateResidenceCases);

        Assert.Contains(free, holders);
        Assert.Contains(paid, holders);
    }

    /// <summary>An empty request is an empty answer, not a query.</summary>
    [Fact]
    public async Task An_empty_list_resolves_to_nothing()
    {
        var (f, _, _) = await SeedAsync();
        await using var db = await f.CreateDbContextAsync();

        Assert.Empty(await TierAreaResolution.WithCapabilityAsync(
            db, [], TierCapability.PrivateResidenceCases));
    }
}
