using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Opening a period freezes the inputs, snapshots the LIVE terms, and sets first-paid once.
/// </summary>
/// <remarks>
/// The renewal promise lives here: phase C's notice says "at your renewal", and the mechanism
/// that keeps it is nothing more than the next snapshot being taken from the live tier — the old
/// terms simply are not in it. If that ever became a special case, the promise would need code;
/// as long as it is the absence of code, it cannot break.
/// </remarks>
public sealed class PeriodOpenerTests
{
    private static SubscriptionTier Tier(int? openCases, decimal monthly)
    {
        var tier = new SubscriptionTier { Id = Guid.NewGuid(), Name = "Small group", MinMembers = 1 };
        tier.Prices.Add(new SubscriptionTierPrice
            { Interval = BillingInterval.Monthly, Price = monthly, IsActive = true });
        tier.Limits.Add(new SubscriptionTierLimit
                { Limit = SubscriptionLimit.OpenCases, MaxValue = openCases });
        return tier;
    }

    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End   = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Opening_a_paid_period_freezes_count_and_price_and_snapshots_the_terms()
    {
        var sub = new OrganizationSubscription { Id = Guid.NewGuid() };

        var snapshot = PeriodOpener.Open(
            sub, Tier(openCases: 10, monthly: 15m), SubscriptionStatus.Active,
            BillingInterval.Monthly, Start, End, memberCount: 7, Guid.NewGuid());

        Assert.Equal(7, sub.MemberCountAtPeriodStart);
        Assert.Equal(15m, sub.PriceAtPeriodStart);
        Assert.NotNull(snapshot);
        Assert.Equal("Small group", snapshot.TierName);
        Assert.Equal(10, EffectiveTermsResolver.FromJson(snapshot.LimitsJson)[SubscriptionLimit.OpenCases]);
    }

    /// <summary>
    /// A renewal after a reduction snapshots the reduced terms — which is exactly how the
    /// reduction "lands at renewal" without any landing code existing.
    /// </summary>
    [Fact]
    public void Renewing_against_a_reduced_tier_snapshots_the_reduced_terms()
    {
        var sub = new OrganizationSubscription { Id = Guid.NewGuid() };

        PeriodOpener.Open(sub, Tier(10, 15m), SubscriptionStatus.Active,
            BillingInterval.Monthly, Start, End, 7, Guid.NewGuid());

        // The SuperAdmin lowered the cap mid-period; the group's current snapshot still says 10.
        // Then the period renews:
        var renewal = PeriodOpener.Open(sub, Tier(2, 15m), SubscriptionStatus.Active,
            BillingInterval.Monthly, End, End.AddMonths(1), 7, Guid.NewGuid());

        Assert.Equal(2, EffectiveTermsResolver.FromJson(renewal!.LimitsJson)[SubscriptionLimit.OpenCases]);
    }

    /// <summary>First-paid is a high-water mark: set on the first Active period, never moved.</summary>
    [Fact]
    public void First_paid_is_set_once_and_survives_lapse_and_reactivation()
    {
        var sub = new OrganizationSubscription { Id = Guid.NewGuid() };
        var by  = Guid.NewGuid();

        PeriodOpener.Open(sub, Tier(null, 15m), SubscriptionStatus.Active,
            BillingInterval.Monthly, Start, End, 5, by);
        var firstPaid = sub.FirstPaidPeriodStartUtc;
        Assert.Equal(Start, firstPaid);

        PeriodOpener.Open(sub, Tier(null, 15m), SubscriptionStatus.Lapsed,
            BillingInterval.Monthly, Start, End, 5, by);
        Assert.NotNull(sub.LapsedAtUtc);
        Assert.Equal(firstPaid, sub.FirstPaidPeriodStartUtc);

        PeriodOpener.Open(sub, Tier(null, 15m), SubscriptionStatus.Active,
            BillingInterval.Monthly, End, End.AddMonths(1), 5, by);
        Assert.Null(sub.LapsedAtUtc);
        Assert.Equal(firstPaid, sub.FirstPaidPeriodStartUtc);   // still the ORIGINAL first payment
    }

    [Fact]
    public void A_free_period_has_no_contract_to_snapshot()
    {
        var sub = new OrganizationSubscription { Id = Guid.NewGuid() };

        var snapshot = PeriodOpener.Open(sub, tier: null, SubscriptionStatus.Free,
            BillingInterval.Monthly, null, null, 3, Guid.NewGuid());

        Assert.Null(snapshot);
        Assert.Equal(0m, sub.PriceAtPeriodStart);
        Assert.Null(sub.FirstPaidPeriodStartUtc);
    }
}
