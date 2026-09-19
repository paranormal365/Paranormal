using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The subscription caps refuse the next one with a sentence, and fail OPEN on missing data.
/// </summary>
/// <remarks>
/// <para>Both directions matter and fail differently. A cap that does not bind quietly
/// under-charges; a cap that binds when billing data is absent locks paying groups out of
/// features and reads as the platform being broken. The guard's rule is that every ambiguous
/// state — no subscription row, no tiers at all, an unusable price list — answers "allowed".</para>
///
/// <para>Exercised through <see cref="SubscriptionLimitGuard"/> directly rather than through five
/// controllers: the controllers' share is one count-and-call apiece, and their existing suites
/// already pin those paths staying reachable.</para>
/// </remarks>
public sealed class SubscriptionLimitEnforcementTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid TierId)> SeedAsync(
        int? openCasesCap, bool withSubscription = true, bool withContract = false, string? contractLimitsJson = null)
    {
        var factory = Factory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var tierId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });

        var tier = new SubscriptionTier
        {
            Id = tierId, Name = "Small group", MinMembers = 1, MaxMembers = null, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        tier.Limits.Add(new SubscriptionTierLimit
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Limit = SubscriptionLimit.OpenCases, MaxValue = openCasesCap,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.SubscriptionTiers.Add(tier);

        Guid subId = Guid.NewGuid();
        var periodStart = DateTime.UtcNow.AddDays(-10);
        if (withSubscription)
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = subId, OrganizationId = orgId, Status = SubscriptionStatus.Active,
                SubscriptionTierId = tierId, CurrentPeriodStart = periodStart,
                CurrentPeriodEnd = periodStart.AddMonths(1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

        if (withContract)
            db.SubscriptionContractTerms.Add(new SubscriptionContractTerms
            {
                Id = Guid.NewGuid(), OrganizationSubscriptionId = subId, SubscriptionTierId = tierId,
                TierName = "Small group", Interval = BillingInterval.Monthly, Price = 15m,
                LimitsJson = contractLimitsJson ?? "{}",
                PeriodStartUtc = periodStart, PeriodEndUtc = periodStart.AddMonths(1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

        await db.SaveChangesAsync();
        return (factory, orgId, tierId);
    }

    [Fact]
    public async Task At_the_cap_the_next_one_is_refused_with_a_sentence_naming_the_plan()
    {
        var (f, orgId, _) = await SeedAsync(openCasesCap: 2);

        var refusal = await new SubscriptionLimitGuard(f)
            .WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, currentCount: 2, default);

        Assert.NotNull(refusal);
        Assert.Contains("Small group", refusal);
        Assert.Contains("2 open case", refusal);
    }

    [Fact]
    public async Task Under_the_cap_the_next_one_is_allowed()
    {
        var (f, orgId, _) = await SeedAsync(openCasesCap: 2);

        Assert.Null(await new SubscriptionLimitGuard(f)
            .WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, currentCount: 1, default));
    }

    /// <summary>Zero is feature-off, and its sentence says "does not include" rather than counting.</summary>
    [Fact]
    public async Task A_zero_cap_refuses_as_not_included()
    {
        var (f, orgId, _) = await SeedAsync(openCasesCap: 0);

        var refusal = await new SubscriptionLimitGuard(f)
            .WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, currentCount: 0, default);

        Assert.Contains("does not include", refusal!);
    }

    /// <summary>
    /// The contract can hold a cap the live tier has lowered — enforcement honours the pricing
    /// card's promise, not the current row.
    /// </summary>
    [Fact]
    public async Task A_contract_with_a_higher_cap_beats_the_lowered_live_tier()
    {
        var (f, orgId, _) = await SeedAsync(
            openCasesCap: 2, withContract: true, contractLimitsJson: """{"OpenCases":10}""");

        var guard = new SubscriptionLimitGuard(f);

        Assert.Null(await guard.WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, 5, default));
        Assert.NotNull(await guard.WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, 10, default));
    }

    // ── the fail-open half ────────────────────────────────────────────────────

    [Fact]
    public async Task No_cap_row_means_no_cap()
    {
        var f = Factory();
        var orgId = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = Guid.NewGuid(), Name = "Free", MinMembers = 1, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        Assert.Null(await new SubscriptionLimitGuard(f)
            .WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, 1_000, default));
    }

    /// <summary>
    /// An empty billing database caps nothing. The failure mode this pins: a deployment where
    /// billing has not been set up must not refuse anybody anything.
    /// </summary>
    [Fact]
    public async Task No_tiers_at_all_means_everything_is_allowed()
    {
        Assert.Null(await new SubscriptionLimitGuard(Factory())
            .WhyNotOneMoreAsync(Guid.NewGuid(), SubscriptionLimit.OpenCases, 1_000, default));
    }

    [Fact]
    public async Task A_group_with_no_subscription_row_falls_back_to_the_band_its_member_count_buys()
    {
        var (f, orgId, tierId) = await SeedAsync(openCasesCap: 2, withSubscription: false);

        var refusal = await new SubscriptionLimitGuard(f)
            .WhyNotOneMoreAsync(orgId, SubscriptionLimit.OpenCases, 2, default);

        // The lone tier is unbounded and starts at 1, so the resolver picks it and its cap binds.
        Assert.NotNull(refusal);
        Assert.Contains("Small group", refusal);
    }
}
