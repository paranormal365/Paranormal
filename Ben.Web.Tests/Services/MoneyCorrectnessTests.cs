using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The money findings of the 2026-09-17 audit, each pinned by the arithmetic that was wrong.
/// </summary>
/// <remarks>
/// <para>Money defects do not announce themselves. Every one of these produced a number rather
/// than an exception — a cent too much, a discount that vanished, a surcharge wearing a coupon's
/// name — which is why they sat unnoticed and why they are pinned here rather than left to the
/// next reader to re-derive.</para>
///
/// <para>Each test was seen failing against the unfixed code before it was kept.</para>
/// </remarks>
public sealed class MoneyCorrectnessTests
{
    private static Coupon Percent(int off) =>
        new() { Name = "Percent", PercentOff = off, Duration = CouponDuration.Once, IsActive = true };

    private static Coupon Amount(decimal off) =>
        new() { Name = "Amount", AmountOff = off, Duration = CouponDuration.Once, IsActive = true };

    // ── C1: the proration rounds once, at the end ─────────────────────────────

    /// <summary>
    /// Three tours added together cost what three tours cost, not three separate roundings.
    /// </summary>
    /// <remarks>
    /// Ten days left of a thirty-day period is exactly a third, so three tours owe a third of
    /// three units: $29 × 3 ÷ 3 = $29.00. Priced one at a time each came to $9.67 — a third of $29
    /// is $9.6667 and the convention rounds away from zero — and three of those is $29.01. The
    /// extra cent is small; what matters is that it is always UP, so it never cancels out and it
    /// grows with the number of tours.
    /// </remarks>
    [Fact]
    public void Adding_three_tours_at_once_is_not_three_separate_roundings()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddDays(30);
        var now   = start.AddDays(20);          // ten days left: exactly a third

        var three = TourBilling.Remainder(29m, start, end, now, units: 3);

        Assert.Equal(29m, three);
        Assert.Equal(9.67m, TourBilling.Remainder(29m, start, end, now));   // the per-unit price
        Assert.Equal(29.01m, TourBilling.Remainder(29m, start, end, now) * 3);   // what it used to charge
    }

    /// <summary>One unit is unchanged, so nothing that was already right moved.</summary>
    [Fact]
    public void One_unit_still_prices_exactly_as_it_did()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddDays(30);

        Assert.Equal(29m, TourBilling.Remainder(29m, start, end, start));
        Assert.Equal(29m, TourBilling.Remainder(29m, start, end, start, units: 1));
        Assert.Equal(Math.Round(29m * 21m / 30m, 2), TourBilling.Remainder(29m, start, end, start.AddDays(9)));
    }

    /// <summary>Nothing is owed for no units, rather than a full unit by omission.</summary>
    [Fact]
    public void No_units_owes_nothing()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0m, TourBilling.Remainder(29m, start, start.AddDays(30), start, units: 0));
    }

    // ── C3/C4: one definition of "this is a percentage coupon" ────────────────

    /// <summary>
    /// A coupon carrying a zero percentage and a real fixed amount takes the fixed amount off.
    /// </summary>
    /// <remarks>
    /// Misconfiguration reads "is this a percentage coupon?" as <c>PercentOff is &gt; 0</c>, so a
    /// zero is not one and the coupon is well-formed on its AmountOff alone. PriceFor asked the
    /// different question "is PercentOff non-null", matched the zero, worked out 0% of the price
    /// and threw the $5 away. The code reported as applied and took nothing off.
    /// </remarks>
    [Fact]
    public void A_zero_percentage_does_not_void_the_fixed_amount_beside_it()
    {
        var coupon = Amount(5m);
        coupon.PercentOff = 0;

        Assert.Null(CouponMath.Misconfiguration(coupon));

        var price = CouponMath.PriceFor(15m, coupon);
        Assert.Equal(5m, price.Discount);
        Assert.Equal(10m, price.Payable);
    }

    /// <summary>A coupon that would ADD money is refused rather than charged.</summary>
    /// <remarks>
    /// Neither discount field was tested for being below zero, and a negative survived precisely
    /// because both checks read "is &gt; 0". PercentOff -10 beside a real AmountOff passed every
    /// check and then produced a negative discount: 110% of list, billed as a discount.
    /// </remarks>
    [Fact]
    public void A_negative_percentage_is_refused_and_can_never_bill_above_list()
    {
        var coupon = Amount(5m);
        coupon.PercentOff = -10;

        Assert.Equal(
            "That code adds to the price rather than taking anything off.",
            CouponMath.Misconfiguration(coupon));

        // And even if one reached PriceFor by another route, payable never exceeds the list price.
        var price = CouponMath.PriceFor(100m, coupon);
        Assert.True(price.Payable <= 100m, $"payable {price.Payable} is above the $100 list price");
        Assert.True(price.Discount >= 0m, $"discount {price.Discount} is a surcharge");
    }

    /// <summary>A negative fixed amount is refused beside a percentage, and alone.</summary>
    /// <remarks>
    /// Alone it is caught a step earlier and more precisely — a coupon whose only field is -$5
    /// genuinely "takes nothing off" — so only the message differs. Beside a real percentage it
    /// passed every check, because both the both-set and the neither-set tests read "is &gt; 0"
    /// and a negative satisfies neither.
    /// </remarks>
    [Fact]
    public void A_negative_fixed_amount_is_refused_too()
    {
        var beside = Percent(20);
        beside.AmountOff = -5m;
        Assert.Equal(
            "That code adds to the price rather than taking anything off.",
            CouponMath.Misconfiguration(beside));

        Assert.NotNull(CouponMath.Misconfiguration(Amount(-5m)));
    }

    /// <summary>The ordinary coupons still price exactly as they did.</summary>
    [Fact]
    public void Percentage_and_fixed_coupons_are_unchanged()
    {
        Assert.Equal(3m, CouponMath.PriceFor(15m, Percent(20)).Discount);
        Assert.Equal(5m, CouponMath.PriceFor(15m, Amount(5m)).Discount);
        Assert.Equal(15m, CouponMath.PriceFor(15m, Percent(100)).Discount);   // a free month
        Assert.Equal(0m, CouponMath.PriceFor(15m, Percent(100)).Payable);     // never a credit
        Assert.Equal(15m, CouponMath.PriceFor(15m, Amount(50m)).Discount);    // capped at the price
    }

    // ── F1: a lapsed plan stops supplying its tier ────────────────────────────

    private static BenDataContext Context()
        => new(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>
    /// Seeds a free band and a paid band, puts the group on the paid one at <paramref name="status"/>,
    /// and excludes private-residence casework from the free band only.
    /// </summary>
    private static async Task<Guid> SeedAsync(BenDataContext db, SubscriptionStatus status)
    {
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), userId = Guid.NewGuid();
        Guid freeId = Guid.NewGuid(), paidId = Guid.NewGuid();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "A group", UrlName = $"g{Guid.NewGuid():N}"[..10],
            Kind = OrganizationKind.InvestigationGroup, DateCreated = now, CreatedByAppUserId = userId,
        });

        foreach (var (id, name, price, sort) in new[]
                 { (freeId, "Free", 0m, 1), (paidId, "Standard", 29m, 2) })
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = id, Name = name, MinMembers = 1, MaxMembers = null, IsBandedByMembers = true,
                IsActive = true, SortOrder = sort, DateCreated = now, CreatedByAppUserId = userId,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = id, Interval = BillingInterval.Monthly,
                Price = price, IsActive = true, DateCreated = now, CreatedByAppUserId = userId,
            });
        }

        // Only the free band is denied private-residence work; the paid band includes it.
        db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
        {
            Id = Guid.NewGuid(), SubscriptionTierId = freeId,
            Capability = TierCapability.PrivateResidenceCases,
            DateCreated = now, CreatedByAppUserId = userId,
        });

        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Status = status,
            SubscriptionTierId = paidId, Interval = BillingInterval.Monthly,
            CurrentPeriodStart = now.AddDays(-60), CurrentPeriodEnd = now.AddDays(-30),
            PriceAtPeriodStart = 29m, MemberCountAtPeriodStart = 1,
            DateCreated = now.AddDays(-60), CreatedByAppUserId = userId,
        });

        await db.SaveChangesAsync();
        return orgId;
    }

    /// <summary>
    /// A group that stopped paying stops holding what the payment bought.
    /// </summary>
    /// <remarks>
    /// PaidPlan states the rule for the same question: "Active, not merely present. A Lapsed
    /// subscription is not a paid plan — otherwise letting one expire would be a way to keep
    /// everything it bought, forever." EffectiveTierAsync took the newest row whatever its status,
    /// so that is exactly what a lapse bought: one month on a band including private-residence
    /// work, and the capability was retained indefinitely.
    /// </remarks>
    [Theory]
    [InlineData(SubscriptionStatus.Lapsed)]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.PendingPayment)]
    public async Task A_subscription_that_is_over_no_longer_supplies_its_tier(SubscriptionStatus status)
    {
        await using var db = Context();
        var orgId = await SeedAsync(db, status);

        var (included, tierName) = await TierAreaResolution.HasCapabilityAsync(
            db, orgId, TierCapability.PrivateResidenceCases);

        Assert.Equal("Free", tierName);
        Assert.False(included);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Free)]
    public async Task A_standing_subscription_still_supplies_its_tier(SubscriptionStatus status)
    {
        await using var db = Context();
        var orgId = await SeedAsync(db, status);

        var (included, tierName) = await TierAreaResolution.HasCapabilityAsync(
            db, orgId, TierCapability.PrivateResidenceCases);

        Assert.Equal("Standard", tierName);
        Assert.True(included);
    }

    /// <summary>
    /// The listing answer agrees with the per-group answer. They are asked from different screens
    /// about the same group, and a group finder advertising a capability the gate then refuses is
    /// worse than either answer alone.
    /// </summary>
    [Theory]
    [InlineData(SubscriptionStatus.Lapsed, false)]
    [InlineData(SubscriptionStatus.Active, true)]
    public async Task The_listing_and_the_gate_give_the_same_answer(SubscriptionStatus status, bool expected)
    {
        await using var db = Context();
        var orgId = await SeedAsync(db, status);

        var holders = await TierAreaResolution.WithCapabilityAsync(
            db, [orgId], TierCapability.PrivateResidenceCases);
        var (included, _) = await TierAreaResolution.HasCapabilityAsync(
            db, orgId, TierCapability.PrivateResidenceCases);

        Assert.Equal(expected, holders.Contains(orgId));
        Assert.Equal(expected, included);
    }
}
