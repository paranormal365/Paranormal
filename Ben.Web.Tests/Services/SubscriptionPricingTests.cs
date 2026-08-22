using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The price list must price everybody, and the discount arithmetic must not invent money.
/// </summary>
/// <remarks>
/// Both halves fail silently if they are wrong: an unpriced band bills a group nothing and nobody
/// reports it, and a rounding slip is a wrong number rather than an error. Item 85.
/// </remarks>
public sealed class SubscriptionPricingTests
{
    private static SubscriptionTier Band(string name, int min, int? max, decimal price) =>
        new() { Name = name, MinMembers = min, MaxMembers = max, MonthlyPrice = price, IsActive = true };

    private static List<SubscriptionTier> SoundList() =>
    [
        Band("Free",   1, 3,    0m),
        Band("Small",  4, 10,  15m),
        Band("Large", 11, null, 40m),
    ];

    // ── the price list ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,  "Free")]
    [InlineData(3,  "Free")]
    [InlineData(4,  "Small")]
    [InlineData(10, "Small")]
    [InlineData(11, "Large")]
    [InlineData(500,"Large")]
    public void Every_member_count_lands_in_the_band_that_covers_it(int members, string expected)
    {
        Assert.Equal(expected, SubscriptionTierResolver.Resolve(SoundList(), members).Name);
    }

    /// <summary>
    /// A group with nobody in it is still priced, at the bottom band.
    /// </summary>
    /// <remarks>
    /// Zero active members is a real state — everyone left, or nobody has accepted an invitation
    /// yet — and it must not fall through the bottom of a list that starts at 1.
    /// </remarks>
    [Fact]
    public void An_organization_with_no_members_is_priced_at_the_lowest_band()
    {
        Assert.Equal("Free", SubscriptionTierResolver.Resolve(SoundList(), 0).Name);
    }

    [Fact]
    public void A_sound_price_list_has_nothing_to_report()
    {
        Assert.Null(SubscriptionTierResolver.Validate(SoundList()));
    }

    /// <summary>
    /// A gap is the dangerous mistake, because the symptom is a group that stops being charged.
    /// </summary>
    [Fact]
    public void A_gap_between_bands_is_refused()
    {
        var withGap = new List<SubscriptionTier> { Band("Free", 1, 3, 0m), Band("Large", 11, null, 40m) };

        var problem = SubscriptionTierResolver.Validate(withGap);

        Assert.NotNull(problem);
        Assert.Contains("4", problem);
        Assert.Contains("10", problem);
    }

    [Fact]
    public void Overlapping_bands_are_refused()
    {
        var overlapping = new List<SubscriptionTier>
        { Band("Free", 1, 5, 0m), Band("Small", 4, 10, 15m), Band("Large", 11, null, 40m) };

        Assert.Contains("overlap", SubscriptionTierResolver.Validate(overlapping)!);
    }

    /// <summary>Without an unbounded top band a group can grow out of the price list.</summary>
    [Fact]
    public void A_price_list_that_a_group_can_outgrow_is_refused()
    {
        var capped = new List<SubscriptionTier> { Band("Free", 1, 3, 0m), Band("Small", 4, 10, 15m) };

        Assert.Contains("outgrow", SubscriptionTierResolver.Validate(capped)!);
    }

    [Fact]
    public void A_list_that_does_not_start_at_one_member_is_refused()
    {
        var starts_at_two = new List<SubscriptionTier> { Band("Small", 2, null, 15m) };

        Assert.Contains("must start at 1", SubscriptionTierResolver.Validate(starts_at_two)!);
    }

    /// <summary>
    /// Resolving against an unusable list throws rather than returning null.
    /// </summary>
    /// <remarks>
    /// A null would be read as "no band applies" and almost certainly treated as free, which is the
    /// expensive direction to be wrong in.
    /// </remarks>
    [Fact]
    public void Resolving_against_a_broken_price_list_throws_rather_than_returning_nothing()
    {
        var withGap = new List<SubscriptionTier> { Band("Free", 1, 3, 0m), Band("Large", 11, null, 40m) };

        Assert.Throws<InvalidOperationException>(() => SubscriptionTierResolver.Resolve(withGap, 5));
    }

    [Fact]
    public void A_retired_band_does_not_price_anybody()
    {
        var tiers = SoundList();
        tiers.Add(new SubscriptionTier
        { Name = "Old Small", MinMembers = 4, MaxMembers = 10, MonthlyPrice = 5m, IsActive = false });

        // The retired band overlaps Small but is ignored, so the list is still sound and Small wins.
        Assert.Null(SubscriptionTierResolver.Validate(tiers));
        Assert.Equal("Small", SubscriptionTierResolver.Resolve(tiers, 5).Name);
    }

    // ── coupons ───────────────────────────────────────────────────────────────

    private static Coupon Percent(int off) =>
        new() { Code = "PCT", PercentOff = off, Duration = CouponDuration.Once, IsActive = true };

    [Fact]
    public void A_percentage_comes_off_the_band_price()
    {
        var price = CouponMath.PriceFor(15m, Percent(20));

        Assert.Equal(15m, price.ListPrice);
        Assert.Equal(3m,  price.Discount);
        Assert.Equal(12m, price.Payable);
    }

    /// <summary>
    /// Half a cent rounds up, not to even.
    /// </summary>
    /// <remarks>
    /// <para>.NET rounds midpoints to even by default, which would shave a cent off about half of
    /// all discounts — never reported, and wrong every time.</para>
    ///
    /// <para><b>The example matters.</b> The first version of this test used 15% of $14.90 =
    /// $2.235, which rounds to $2.24 under <i>both</i> modes because the preceding digit is already
    /// odd — it passed against the bug it was written to catch. 5% of $24.50 is $1.225, where
    /// to-even gives $1.22 and away-from-zero gives $1.23. Verified by switching the rounding mode
    /// and watching this fail.</para>
    /// </remarks>
    [Fact]
    public void A_half_cent_discount_rounds_away_from_zero_not_to_even()
    {
        Assert.Equal(1.23m, CouponMath.PriceFor(24.50m, Percent(5)).Discount);
    }

    [Fact]
    public void A_discount_larger_than_the_price_is_a_free_period_not_a_credit()
    {
        var coupon = new Coupon { Code = "BIG", AmountOff = 100m, Duration = CouponDuration.Once, IsActive = true };

        var price = CouponMath.PriceFor(15m, coupon);

        Assert.Equal(15m, price.Discount);
        Assert.Equal(0m,  price.Payable);
    }

    [Fact]
    public void A_free_band_stays_free_and_no_discount_is_claimed()
    {
        var price = CouponMath.PriceFor(0m, Percent(50));

        Assert.Equal(0m, price.Discount);
        Assert.Equal(0m, price.Payable);
    }

    /// <summary>A coupon that sets both kinds of discount has no single meaning.</summary>
    [Fact]
    public void A_coupon_setting_both_a_percentage_and_an_amount_is_refused()
    {
        var both = new Coupon { Code = "BOTH", PercentOff = 10, AmountOff = 5m, Duration = CouponDuration.Once, IsActive = true };

        Assert.Contains("no single meaning", CouponMath.Misconfiguration(both)!);
    }

    /// <summary>
    /// A coupon that takes nothing off is refused rather than silently discounting zero.
    /// </summary>
    /// <remarks>
    /// This is what an edit that clears both fields produces, and the silent version of it is a
    /// code that appears to work and changes no price.
    /// </remarks>
    [Fact]
    public void A_coupon_that_takes_nothing_off_is_refused()
    {
        var empty = new Coupon { Code = "NOWT", Duration = CouponDuration.Once, IsActive = true };

        Assert.Contains("takes nothing off", CouponMath.Misconfiguration(empty)!);
        Assert.Equal(15m, CouponMath.PriceFor(15m, empty).Payable);
    }

    [Fact]
    public void A_repeating_coupon_with_no_periods_is_refused()
    {
        var bad = new Coupon { Code = "REP", PercentOff = 10, Duration = CouponDuration.Repeating, IsActive = true };

        Assert.Contains("no periods", CouponMath.Misconfiguration(bad)!);
    }

    // ── redemption rules ──────────────────────────────────────────────────────

    [Fact]
    public void A_retired_code_cannot_be_redeemed()
    {
        var c = Percent(10); c.IsActive = false;

        Assert.NotNull(CouponMath.WhyNotRedeemable(c, DateTime.UtcNow, alreadyRedeemedByThisOrg: false));
    }

    [Fact]
    public void An_expired_code_cannot_be_redeemed()
    {
        var c = Percent(10); c.RedeemByUtc = DateTime.UtcNow.AddDays(-1);

        Assert.Contains("expired", CouponMath.WhyNotRedeemable(c, DateTime.UtcNow, false)!);
    }

    [Fact]
    public void A_fully_claimed_code_cannot_be_redeemed()
    {
        var c = Percent(10); c.MaxRedemptions = 2; c.RedemptionCount = 2;

        Assert.Contains("fully claimed", CouponMath.WhyNotRedeemable(c, DateTime.UtcNow, false)!);
    }

    [Fact]
    public void The_same_organization_cannot_redeem_a_code_twice()
    {
        Assert.Contains("already used", CouponMath.WhyNotRedeemable(Percent(10), DateTime.UtcNow, true)!);
    }

    [Fact]
    public void A_usable_code_reports_no_reason_against_it()
    {
        Assert.Null(CouponMath.WhyNotRedeemable(Percent(10), DateTime.UtcNow, false));
    }

    [Theory]
    [InlineData(CouponDuration.Once, null, 1)]
    [InlineData(CouponDuration.Repeating, 3, 3)]
    public void A_redemption_is_granted_the_periods_its_coupon_promises(CouponDuration duration, int? configured, int expected)
    {
        var c = Percent(10); c.Duration = duration; c.DurationPeriods = configured;

        Assert.Equal(expected, CouponMath.PeriodsFor(c));
    }

    [Fact]
    public void A_forever_coupon_is_granted_no_expiry_and_keeps_applying()
    {
        var c = Percent(10); c.Duration = CouponDuration.Forever;

        Assert.Null(CouponMath.PeriodsFor(c));
        Assert.True(CouponMath.IsStillApplying(new CouponRedemption { PeriodsRemaining = null }));
    }

    [Fact]
    public void A_redemption_with_no_periods_left_stops_applying()
    {
        Assert.False(CouponMath.IsStillApplying(new CouponRedemption { PeriodsRemaining = 0 }));
    }
}
