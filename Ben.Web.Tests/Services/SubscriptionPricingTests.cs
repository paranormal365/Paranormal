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
    private static SubscriptionTier Band(string name, int min, int? max, params (BillingInterval, decimal)[] prices)
    {
        var tier = new SubscriptionTier { Name = name, MinMembers = min, MaxMembers = max, IsActive = true };

        foreach (var (interval, price) in prices)
            tier.Prices.Add(new SubscriptionTierPrice { Interval = interval, Price = price, IsActive = true });

        return tier;
    }

    private static List<SubscriptionTier> SoundList() =>
    [
        Band("Free",   1, 3,    (BillingInterval.Monthly,  0m)),
        Band("Small",  4, 10,   (BillingInterval.Monthly, 15m), (BillingInterval.Yearly, 150m)),
        Band("Large", 11, null, (BillingInterval.Monthly, 40m), (BillingInterval.Yearly, 400m)),
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

    [Fact]
    public void A_price_list_that_does_not_start_at_one_member_is_refused()
    {
        var tiers = SoundList();
        tiers[0].MinMembers = 2;

        Assert.Contains("must start at 1", SubscriptionTierResolver.Validate(tiers)!);
    }

    /// <summary>
    /// A gap between bands is the failure this validation exists for.
    /// </summary>
    /// <remarks>
    /// Deleting the middle band does not make a five-member group "unpriced" — it makes it match
    /// nothing and be billed nothing, and nobody reports a group that stops being charged.
    /// </remarks>
    [Fact]
    public void A_gap_between_bands_is_refused_and_names_the_members_nothing_prices()
    {
        var tiers = SoundList();
        tiers.RemoveAt(1);

        var problem = SubscriptionTierResolver.Validate(tiers);

        Assert.Contains("Nothing prices 4", problem!);
        Assert.Contains("–10", problem);
    }

    [Fact]
    public void Overlapping_bands_are_refused()
    {
        var tiers = SoundList();
        tiers[2].MinMembers = 9;

        Assert.Contains("overlap", SubscriptionTierResolver.Validate(tiers)!);
    }

    [Fact]
    public void A_price_list_a_group_can_outgrow_is_refused()
    {
        var tiers = SoundList();
        tiers[2].MaxMembers = 100;

        Assert.Contains("must be unbounded", SubscriptionTierResolver.Validate(tiers)!);
    }

    [Fact]
    public void An_unbounded_band_that_is_not_the_top_one_is_refused()
    {
        var tiers = SoundList();
        tiers[1].MaxMembers = null;

        Assert.Contains("swallows", SubscriptionTierResolver.Validate(tiers)!);
    }

    [Fact]
    public void An_empty_price_list_is_refused_rather_than_pricing_everybody_at_nothing()
    {
        Assert.Contains("no active price bands", SubscriptionTierResolver.Validate([])!);
    }

    /// <summary>Resolving against an unsound list throws rather than returning null.</summary>
    /// <remarks>
    /// A caller handed null would almost certainly treat it as free, which is the expensive
    /// direction to be wrong in.
    /// </remarks>
    [Fact]
    public void Resolving_against_an_unsound_price_list_throws()
    {
        var tiers = SoundList();
        tiers.RemoveAt(1);

        Assert.Throws<InvalidOperationException>(() => SubscriptionTierResolver.Resolve(tiers, 5));
    }

    [Fact]
    public void A_retired_band_is_ignored_by_both_validation_and_resolution()
    {
        var tiers = SoundList();
        var old   = Band("Old Small", 4, 10, (BillingInterval.Monthly, 5m));
        old.IsActive = false;
        tiers.Add(old);

        // The retired band overlaps Small but is ignored, so the list is still sound and Small wins.
        Assert.Null(SubscriptionTierResolver.Validate(tiers));
        Assert.Equal("Small", SubscriptionTierResolver.Resolve(tiers, 5).Name);
    }

    // ── billing cadences ──────────────────────────────────────────────────────

    [Fact]
    public void A_band_reports_the_price_for_each_cadence_it_is_sold_at()
    {
        var small = SoundList()[1];

        Assert.Equal(15m,  SubscriptionPricing.PriceFor(small, BillingInterval.Monthly));
        Assert.Equal(150m, SubscriptionPricing.PriceFor(small, BillingInterval.Yearly));
    }

    /// <summary>
    /// A cadence with no row is not sold, and that is an answer rather than an error.
    /// </summary>
    /// <remarks>
    /// The free band is monthly-only on purpose — a yearly price of zero is a question asked for
    /// no reason. Checkout offers what comes back from <c>AvailableIntervals</c>.
    /// </remarks>
    [Fact]
    public void A_cadence_a_band_is_not_sold_at_reports_no_price_rather_than_zero()
    {
        var free = SoundList()[0];

        Assert.Null(SubscriptionPricing.PriceFor(free, BillingInterval.Yearly));
        Assert.Equal([BillingInterval.Monthly], SubscriptionPricing.AvailableIntervals(free));
    }

    [Fact]
    public void A_retired_price_row_stops_the_band_being_sold_at_that_cadence()
    {
        var small = SoundList()[1];
        small.Prices.First(p => p.Interval == BillingInterval.Yearly).IsActive = false;

        Assert.Null(SubscriptionPricing.PriceFor(small, BillingInterval.Yearly));
        Assert.DoesNotContain(BillingInterval.Yearly, SubscriptionPricing.AvailableIntervals(small));
    }

    [Theory]
    [InlineData(BillingInterval.Monthly, 1)]
    [InlineData(BillingInterval.Quarterly, 3)]
    [InlineData(BillingInterval.HalfYearly, 6)]
    [InlineData(BillingInterval.Yearly, 12)]
    public void An_intervals_value_is_the_number_of_months_it_covers(BillingInterval interval, int months)
    {
        Assert.Equal(months, SubscriptionPricing.MonthsIn(interval));
    }

    /// <summary>
    /// A period ends a number of months later, not a number of days later.
    /// </summary>
    /// <remarks>
    /// 365 days from 1 March 2027 is 29 February 2028, so a yearly subscription would drift a day
    /// earlier every leap year and eventually bill twice in one month. <c>AddMonths</c> holds the
    /// day of the month.
    /// </remarks>
    [Fact]
    public void A_yearly_period_ends_on_the_same_day_next_year_across_a_leap_year()
    {
        var start = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SubscriptionPricing.PeriodEnd(start, BillingInterval.Yearly));
    }

    /// <summary>A period starting on the 31st ends on the last day of a shorter month.</summary>
    [Fact]
    public void A_period_starting_on_the_thirty_first_clamps_rather_than_spilling_into_the_next_month()
    {
        var start = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
            SubscriptionPricing.PeriodEnd(start, BillingInterval.Monthly));
    }

    /// <summary>
    /// The yearly saving is read back out of the two prices rather than stored beside them.
    /// </summary>
    /// <remarks>
    /// $150 against twelve months at $15 is $30 off $180, which is 16.66% — shown as 16, not 17.
    /// Rounding a saving up overstates it, and on a price that is a claim somebody can check.
    /// </remarks>
    [Fact]
    public void The_yearly_saving_is_derived_from_the_prices_and_never_rounded_up()
    {
        var small = SoundList()[1];

        Assert.Equal(16, SubscriptionPricing.SavingPercentAgainstMonthly(small, BillingInterval.Yearly));
    }

    [Fact]
    public void Monthly_has_no_saving_against_itself()
    {
        Assert.Null(SubscriptionPricing.SavingPercentAgainstMonthly(SoundList()[1], BillingInterval.Monthly));
    }

    /// <summary>A cadence that costs more than paying monthly advertises no saving.</summary>
    [Fact]
    public void A_cadence_that_is_not_cheaper_reports_no_saving_rather_than_a_negative_one()
    {
        var small = SoundList()[1];
        small.Prices.First(p => p.Interval == BillingInterval.Yearly).Price = 200m;

        Assert.Null(SubscriptionPricing.SavingPercentAgainstMonthly(small, BillingInterval.Yearly));
    }

    [Fact]
    public void A_band_with_no_monthly_price_has_nothing_to_compare_a_saving_against()
    {
        var yearlyOnly = Band("Yearly only", 1, null, (BillingInterval.Yearly, 100m));

        Assert.Null(SubscriptionPricing.SavingPercentAgainstMonthly(yearlyOnly, BillingInterval.Yearly));
    }

    /// <summary>
    /// The editor's "N% off" button and the saving shown on the pricing page agree.
    /// </summary>
    /// <remarks>
    /// This is the round trip that lets the discount go unstored: a SuperAdmin types 20, the price
    /// row gets $144, and the page reads 20 back out. If these two ever disagree the percentage
    /// would have to be stored, and then there would be two of it.
    /// </remarks>
    [Theory]
    [InlineData(10)]
    [InlineData(17)]
    [InlineData(20)]
    [InlineData(25)]
    public void A_price_written_from_a_percentage_reads_the_same_percentage_back(int percentOff)
    {
        var tier = Band("Band", 1, null, (BillingInterval.Monthly, 15m));
        tier.Prices.Add(new SubscriptionTierPrice
        {
            Interval = BillingInterval.Yearly,
            Price    = SubscriptionPricing.PriceForSaving(15m, BillingInterval.Yearly, percentOff),
            IsActive = true,
        });

        Assert.Equal(percentOff, SubscriptionPricing.SavingPercentAgainstMonthly(tier, BillingInterval.Yearly));
    }

    [Fact]
    public void An_annualised_cost_makes_cadences_comparable()
    {
        Assert.Equal(180m, SubscriptionPricing.AnnualisedCost(15m, BillingInterval.Monthly));
        Assert.Equal(150m, SubscriptionPricing.AnnualisedCost(150m, BillingInterval.Yearly));
        Assert.Equal(160m, SubscriptionPricing.AnnualisedCost(40m, BillingInterval.Quarterly));
    }

    // ── coupons ───────────────────────────────────────────────────────────────

    private static Coupon Percent(int off) =>
        new() { Name = "Percent", PercentOff = off, Duration = CouponDuration.Once, IsActive = true };

    private static CouponCode CodeFor(Coupon coupon, int? maxRedemptions = null) =>
        new() { Coupon = coupon, Code = "PCT", MaxRedemptions = maxRedemptions, IsActive = true };

    private static readonly Guid Redeemer = Guid.NewGuid();

    private static CouponRedemptionContext Now(
        bool alreadyRedeemed = false,
        BillingInterval interval = BillingInterval.Monthly,
        bool isRenewal = false,
        Guid? asUser = null,
        DateTime? at = null) =>
        new(at ?? DateTime.UtcNow, asUser ?? Redeemer, interval, isRenewal, alreadyRedeemed);

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

    /// <summary>
    /// A percentage scales with the period; a fixed amount does not.
    /// </summary>
    /// <remarks>
    /// This is the whole reason <see cref="Coupon.AppliesToInterval"/> exists. "20% off" against a
    /// $150 yearly period is $30, twelve times what the author of a monthly campaign expected, and
    /// nothing about the coupon says which they meant.
    /// </remarks>
    [Fact]
    public void A_percentage_scales_with_the_period_and_a_fixed_amount_does_not()
    {
        var fiver = new Coupon { Name = "Fiver", AmountOff = 5m, Duration = CouponDuration.Once, IsActive = true };

        Assert.Equal(3m,  CouponMath.PriceFor(15m,  Percent(20)).Discount);
        Assert.Equal(30m, CouponMath.PriceFor(150m, Percent(20)).Discount);

        Assert.Equal(5m, CouponMath.PriceFor(15m,  fiver).Discount);
        Assert.Equal(5m, CouponMath.PriceFor(150m, fiver).Discount);
    }

    [Fact]
    public void A_discount_larger_than_the_price_is_a_free_period_not_a_credit()
    {
        var coupon = new Coupon { Name = "Big", AmountOff = 100m, Duration = CouponDuration.Once, IsActive = true };

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
        var both = new Coupon { Name = "Both", PercentOff = 10, AmountOff = 5m, Duration = CouponDuration.Once, IsActive = true };

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
        var empty = new Coupon { Name = "Nowt", Duration = CouponDuration.Once, IsActive = true };

        Assert.Contains("takes nothing off", CouponMath.Misconfiguration(empty)!);
        Assert.Equal(15m, CouponMath.PriceFor(15m, empty).Payable);
    }

    [Fact]
    public void A_repeating_coupon_with_no_periods_is_refused()
    {
        var bad = new Coupon { Name = "Rep", PercentOff = 10, Duration = CouponDuration.Repeating, IsActive = true };

        Assert.Contains("no periods", CouponMath.Misconfiguration(bad)!);
    }

    [Fact]
    public void A_coupon_whose_window_closes_before_it_opens_is_refused()
    {
        var c = Percent(10);
        c.ValidFromUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        c.RedeemByUtc  = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Contains("before it starts", CouponMath.Misconfiguration(c)!);
    }

    // ── redemption rules ──────────────────────────────────────────────────────

    [Fact]
    public void A_retired_campaign_cannot_be_redeemed()
    {
        var c = Percent(10); c.IsActive = false;

        Assert.NotNull(CouponMath.WhyNotRedeemable(c, CodeFor(c), Now()));
    }

    /// <summary>One code can be withdrawn without retiring the campaign it belongs to.</summary>
    [Fact]
    public void A_withdrawn_code_cannot_be_redeemed_even_though_its_campaign_is_live()
    {
        var c = Percent(10);
        var code = CodeFor(c); code.IsActive = false;

        Assert.NotNull(CouponMath.WhyNotRedeemable(c, code, Now()));
    }

    [Fact]
    public void An_expired_code_cannot_be_redeemed()
    {
        var c = Percent(10); c.RedeemByUtc = DateTime.UtcNow.AddDays(-1);

        Assert.Contains("expired", CouponMath.WhyNotRedeemable(c, CodeFor(c), Now())!);
    }

    /// <summary>
    /// A campaign written in advance is not redeemable before it opens.
    /// </summary>
    /// <remarks>
    /// The alternative is somebody remembering to flip IsActive at the right hour on the day of a
    /// conference, which is exactly the sort of thing that gets remembered late.
    /// </remarks>
    [Fact]
    public void A_campaign_that_has_not_opened_yet_cannot_be_redeemed()
    {
        var c = Percent(10); c.ValidFromUtc = DateTime.UtcNow.AddDays(3);

        Assert.Contains("cannot be used yet", CouponMath.WhyNotRedeemable(c, CodeFor(c), Now())!);
    }

    [Fact]
    public void A_campaign_inside_its_window_can_be_redeemed()
    {
        var c = Percent(10);
        c.ValidFromUtc = DateTime.UtcNow.AddDays(-1);
        c.RedeemByUtc  = DateTime.UtcNow.AddDays(1);

        Assert.Null(CouponMath.WhyNotRedeemable(c, CodeFor(c), Now()));
    }

    [Fact]
    public void A_fully_claimed_campaign_cannot_be_redeemed()
    {
        var c = Percent(10); c.MaxRedemptions = 2; c.RedemptionCount = 2;

        Assert.Contains("fully claimed", CouponMath.WhyNotRedeemable(c, CodeFor(c), Now())!);
    }

    /// <summary>
    /// A single-use code is spent when it has been used once, whatever the campaign's budget says.
    /// </summary>
    /// <remarks>
    /// This is the per-code half of "single-use by generation": one person burning their code must
    /// not touch anybody else's, and must not need the campaign to be exhausted to stop working.
    /// </remarks>
    [Fact]
    public void A_spent_single_use_code_stops_working_while_the_rest_of_its_batch_carries_on()
    {
        var c = Percent(10);
        c.MaxRedemptions = 500;   // the campaign has plenty left
        c.RedemptionCount = 3;

        var spent = CodeFor(c, maxRedemptions: 1); spent.RedemptionCount = 1;
        var fresh = CodeFor(c, maxRedemptions: 1);

        Assert.Contains("already been used", CouponMath.WhyNotRedeemable(c, spent, Now())!);
        Assert.Null(CouponMath.WhyNotRedeemable(c, fresh, Now()));
    }

    /// <summary>
    /// The campaign's budget stops a batch even when individual codes are unclaimed.
    /// </summary>
    /// <remarks>
    /// Which is how a print run of five hundred cards gets a budget of fifty without reprinting.
    /// </remarks>
    [Fact]
    public void A_campaign_budget_stops_unclaimed_codes_in_a_batch()
    {
        var c = Percent(10); c.MaxRedemptions = 50; c.RedemptionCount = 50;

        Assert.Contains("fully claimed", CouponMath.WhyNotRedeemable(c, CodeFor(c, 1), Now())!);
    }

    [Fact]
    public void The_same_organization_cannot_redeem_a_campaign_twice()
    {
        var c = Percent(10);

        Assert.Contains("already used",
            CouponMath.WhyNotRedeemable(c, CodeFor(c), Now(alreadyRedeemed: true))!);
    }

    /// <summary>A code addressed to one account is refused to every other account.</summary>
    [Fact]
    public void A_code_issued_to_one_person_is_refused_to_anybody_else()
    {
        var c = Percent(10);
        var code = CodeFor(c); code.RestrictedToAppUserId = Guid.NewGuid();

        Assert.Contains("different account", CouponMath.WhyNotRedeemable(c, code, Now())!);
    }

    [Fact]
    public void A_code_issued_to_one_person_works_for_that_person()
    {
        var c = Percent(10);
        var code = CodeFor(c); code.RestrictedToAppUserId = Redeemer;

        Assert.Null(CouponMath.WhyNotRedeemable(c, code, Now()));
    }

    /// <summary>The free-text note never restricts anybody; only the account id does.</summary>
    /// <remarks>
    /// Worth pinning: the note is written from a conference list and often names somebody whose
    /// account here does not exist. Enforcing it would refuse the very codes it records.
    /// </remarks>
    [Fact]
    public void The_issued_to_note_does_not_restrict_who_may_redeem()
    {
        var c = Percent(10);
        var code = CodeFor(c); code.IssuedTo = "someone.else@example.com";

        Assert.Null(CouponMath.WhyNotRedeemable(c, code, Now()));
    }

    /// <summary>A cadence-restricted coupon is refused against any other cadence.</summary>
    [Fact]
    public void A_yearly_only_coupon_is_refused_against_a_monthly_subscription()
    {
        var c = Percent(20); c.AppliesToInterval = BillingInterval.Yearly;

        var why = CouponMath.WhyNotRedeemable(c, CodeFor(c), Now(interval: BillingInterval.Monthly));

        Assert.Contains("yearly billing", why!);
        Assert.Null(CouponMath.WhyNotRedeemable(c, CodeFor(c), Now(interval: BillingInterval.Yearly)));
    }

    /// <summary>
    /// A renewal coupon reaches existing groups and not first-time ones, and the other way round.
    /// </summary>
    /// <remarks>
    /// Getting this backwards gives the retention discount to everybody except the people being
    /// retained, and the acquisition discount to everybody except new groups — a mistake that
    /// costs money in both directions and shows up as nothing at all in a log.
    /// </remarks>
    [Theory]
    [InlineData(CouponApplicability.RenewalsOnly,         true,  true)]
    [InlineData(CouponApplicability.RenewalsOnly,         false, false)]
    [InlineData(CouponApplicability.NewSubscriptionsOnly, true,  false)]
    [InlineData(CouponApplicability.NewSubscriptionsOnly, false, true)]
    [InlineData(CouponApplicability.Any,                  true,  true)]
    [InlineData(CouponApplicability.Any,                  false, true)]
    public void A_coupon_reaches_the_occasion_it_was_written_for(
        CouponApplicability appliesTo, bool isRenewal, bool expectedRedeemable)
    {
        var c = Percent(10); c.AppliesTo = appliesTo;

        var why = CouponMath.WhyNotRedeemable(c, CodeFor(c), Now(isRenewal: isRenewal));

        Assert.Equal(expectedRedeemable, why is null);
    }

    /// <summary>
    /// "Renewal" means the group has paid before, not that it is paying now.
    /// </summary>
    /// <remarks>
    /// A group that lapsed last month is exactly who a win-back coupon is for. Reading this as
    /// "currently active" would shut out the only people it was written to reach.
    /// </remarks>
    [Fact]
    public void A_lapsed_group_still_counts_as_a_renewal()
    {
        var lapsed = new OrganizationSubscription
        {
            Status                  = SubscriptionStatus.Lapsed,
            LapsedAtUtc             = DateTime.UtcNow.AddDays(-30),
            FirstPaidPeriodStartUtc = DateTime.UtcNow.AddYears(-2),
        };

        var neverPaid = new OrganizationSubscription { Status = SubscriptionStatus.Free };

        Assert.True(CouponMath.IsRenewal(lapsed));
        Assert.False(CouponMath.IsRenewal(neverPaid));
    }

    [Fact]
    public void A_usable_code_reports_no_reason_against_it()
    {
        var c = Percent(10);

        Assert.Null(CouponMath.WhyNotRedeemable(c, CodeFor(c), Now()));
    }

    // ── batches ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_campaign_with_no_codes_is_refused()
    {
        var c = Percent(10); c.Kind = CouponKind.Generated;

        Assert.Contains("no codes", CouponMath.BatchMisconfiguration(c, [])!);
    }

    /// <summary>A shared campaign has exactly one code, because it is the code.</summary>
    [Fact]
    public void A_shared_campaign_with_several_codes_is_refused()
    {
        var c = Percent(10); c.Kind = CouponKind.Shared;

        var problem = CouponMath.BatchMisconfiguration(c, [CodeFor(c), CodeFor(c)]);

        Assert.Contains("has 2", problem!);
    }

    [Fact]
    public void A_generated_batch_of_many_codes_is_fine()
    {
        var c = Percent(10); c.Kind = CouponKind.Generated;

        Assert.Null(CouponMath.BatchMisconfiguration(c, [CodeFor(c, 1), CodeFor(c, 1), CodeFor(c, 1)]));
    }

    // ── generated codes ───────────────────────────────────────────────────────

    /// <summary>
    /// Generated codes contain no character that gets misread off a printed card.
    /// </summary>
    /// <remarks>
    /// O/0, I/1/l, S/5, B/8 and Z/2 are the confusions that actually happen, and each one turns a
    /// redemption into a support message. Checked against a batch rather than one code because a
    /// single draw is unlikely to contain any given character.
    /// </remarks>
    [Fact]
    public void Generated_codes_avoid_every_character_that_gets_misread()
    {
        var banned = "OIl015S8BZ2";

        foreach (var code in CouponCodeGenerator.Batch(200))
            Assert.DoesNotContain(code, ch => banned.Contains(ch));
    }

    [Fact]
    public void A_generated_batch_contains_no_duplicates()
    {
        var codes = CouponCodeGenerator.Batch(500);

        Assert.Equal(500, codes.Count);
        Assert.Equal(500, codes.Distinct().Count());
    }

    [Fact]
    public void A_prefix_is_carried_onto_every_code_in_the_batch()
    {
        foreach (var code in CouponCodeGenerator.Batch(20, "paracon"))
            Assert.StartsWith("PARACON-", code);
    }

    /// <summary>
    /// What is typed and what is stored go through the same normalisation.
    /// </summary>
    /// <remarks>
    /// A code stored upper-cased and looked up as typed works for everybody who uses capitals and
    /// fails for everybody who does not — a bug that reads as "the code is wrong".
    /// </remarks>
    [Theory]
    [InlineData("  launch25 ", "LAUNCH25")]
    [InlineData("Launch25",    "LAUNCH25")]
    [InlineData(null,          "")]
    public void A_typed_code_normalises_to_its_stored_form(string? typed, string expected)
    {
        Assert.Equal(expected, CouponCodeGenerator.Normalise(typed));
    }

    [Fact]
    public void A_generated_code_is_already_in_its_stored_form()
    {
        var code = CouponCodeGenerator.One("launch");

        Assert.Equal(code, CouponCodeGenerator.Normalise(code));
    }

    [Fact]
    public void A_code_shorter_than_four_random_characters_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CouponCodeGenerator.One(randomLength: 3));
    }

    // ── durations ─────────────────────────────────────────────────────────────

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
