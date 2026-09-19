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

            // Prices, because "free" is identified by COSTING NOTHING — a tier list with no
            // prices describes no pricing model at all, and the fixture without them was not the
            // world these rules run in.
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = freeTierId,
                Interval = BillingInterval.Monthly, Price = 0m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = paidTierId,
                Interval = BillingInterval.Monthly, Price = 15m,
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
    /// <summary>
    /// A big group that pays nothing is FREE, not banded into a paid tier by its headcount.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-08-27: "A free version doesn't care about the number of people. It only
    /// cares about privacy." Before this, a group with no subscription was assigned a band by
    /// member count, so growing past the free band silently granted the paid capability to
    /// somebody paying nothing — two of five seeded groups were in exactly that state.</para>
    ///
    /// <para>The test seeds a group LARGER than the free band deliberately: under the old rule it
    /// would resolve to the paid tier and hold the capability, which is the regression this
    /// guards.</para>
    /// </remarks>
    [Fact]
    public async Task A_large_group_paying_nothing_is_free_not_promoted_by_headcount()
    {
        var (f, free, _) = await SeedAsync();

        await using (var seed = await f.CreateDbContextAsync())
        {
            // Drop the subscription that pinned it, and grow it past the free band.
            seed.OrganizationSubscriptions.RemoveRange(
                seed.OrganizationSubscriptions.Where(s => s.OrganizationId == free));
            for (var i = 0; i < 9; i++)
            {
                var uid = Guid.NewGuid();
                seed.Users.Add(new AppUser { Id = uid, UserName = $"{uid:N}@t.com", DateCreated = DateTime.UtcNow });
                seed.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = free, AppUserId = uid,
                    Role = OrganizationMemberRole.Member, IsActive = true,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = uid,
                });
            }
            await seed.SaveChangesAsync();
        }

        await using var db = await f.CreateDbContextAsync();
        var holders = await TierAreaResolution.WithCapabilityAsync(
            db, [free], TierCapability.PrivateResidenceCases);

        Assert.DoesNotContain(free, holders);

        // And the single-group answer agrees, which is the pair that used to drift.
        var (single, _) = await TierAreaResolution.HasCapabilityAsync(
            db, free, TierCapability.PrivateResidenceCases);
        Assert.False(single);
    }

    /// <summary>Paying still buys it, whatever the size.</summary>
    [Fact]
    public async Task A_paid_subscription_still_holds_the_capability()
    {
        var (f, _, paid) = await SeedAsync();
        await using var db = await f.CreateDbContextAsync();

        Assert.Contains(paid, await TierAreaResolution.WithCapabilityAsync(
            db, [paid], TierCapability.PrivateResidenceCases));
    }

    [Fact]
    public async Task An_empty_list_resolves_to_nothing()
    {
        var (f, _, _) = await SeedAsync();
        await using var db = await f.CreateDbContextAsync();

        Assert.Empty(await TierAreaResolution.WithCapabilityAsync(
            db, [], TierCapability.PrivateResidenceCases));
    }
}
