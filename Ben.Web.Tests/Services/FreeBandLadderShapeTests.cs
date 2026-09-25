using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The shape of the live price ladder, pinned: a free band that gates the free lane without
/// selling anybody a subscription that costs nothing.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists (2026-09-17 audit, round four).</b> The live ladder was four paid
/// bands and nothing priced at zero. <c>TierAreaResolution.FreeTierAsync</c> resolves a group with
/// no subscription by looking for an active, member-banded tier whose prices are all zero; finding
/// none it returned null, and every capability check then <b>failed open</b>. So on production
/// every group held <c>PrivateResidenceCases</c>, <c>CaseTransfers</c>,
/// <c>MediaMetadataStripping</c> and <c>HostEvents</c> while paying nothing — item 184's entire
/// paid lane, unenforced. Ben's instruction on 2026-09-18: add a free band, and exclude private
/// residences from it.</para>
///
/// <para><b>A band priced at zero is safe to have, because checkout already refuses to sell it.</b>
/// <c>OrganizationCheckoutController.Start</c> tests the LIST price and answers "There is nothing
/// to subscribe to at that size. A group is free by not having a plan at all — being on one is
/// what a paid plan means." That guard is what makes a zero-priced band a <i>reference</i> tier
/// rather than a product, and it is why the band can keep a normal active price and render on the
/// pricing page like any other.</para>
///
/// <para><b>The member range is not arbitrary.</b> One person free and the second asking for a
/// plan is the rule <c>PaidPlan.WhyCannotAddMemberAsync</c> already enforces: "One person is free;
/// working with other people is the paid part... the FIRST person never meets this and the second
/// is what asks for a plan." The ladder now says what the gate has always done. It is also the
/// only range that fits: the free band must start at 1, and anything wider than 1–1 would push
/// Small Group into Standard Group's range and force every band to be renumbered.</para>
/// </remarks>
public sealed class FreeBandLadderShapeTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static SubscriptionTier Band(
        string name, int min, int? max, int sort, params (decimal Price, BillingInterval Interval)[] prices)
    {
        var tier = new SubscriptionTier
        {
            Id = Guid.NewGuid(), Name = name, MinMembers = min, MaxMembers = max,
            SortOrder = sort, IsActive = true, IsBandedByMembers = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        };
        foreach (var (price, interval) in prices)
            tier.Prices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = tier.Id,
                Interval = interval, Price = price, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
            });
        return tier;
    }

    /// <summary>The ladder as it is being put on the live site.</summary>
    private static List<SubscriptionTier> LiveLadder() =>
    [
        Band("Free", 1, 1, 0, (0m, BillingInterval.Monthly)),
        Band("Small Group", 2, 3, 1, (20m, BillingInterval.Monthly), (200m, BillingInterval.Yearly)),
        Band("Standard Group", 4, 10, 2, (40m, BillingInterval.Monthly), (400m, BillingInterval.Yearly)),
        Band("Large Group", 11, 25, 3, (60m, BillingInterval.Monthly), (600m, BillingInterval.Yearly)),
        Band("Enterprise", 26, null, 4, (100m, BillingInterval.Monthly), (1000m, BillingInterval.Yearly)),
    ];

    /// <summary>The ladder is sound, so nothing throws and checkout keeps working.</summary>
    /// <remarks>
    /// <c>Resolve</c> THROWS on an unsound list, and it sits on the path of checkout, the
    /// permission-area gate and the renewal job together. Getting the contiguity wrong is not a bad
    /// quote, it is an outage — so this is asserted before the rows are written, not after.
    /// </remarks>
    [Fact]
    public void The_ladder_validates()
        => Assert.Null(SubscriptionTierResolver.Validate(LiveLadder()));

    /// <summary>
    /// The free band is what a group with no plan resolves to, so gating happens at all. This is
    /// the predicate <c>FreeTierAsync</c> runs, and the whole fix turns on it matching something.
    /// </summary>
    [Fact]
    public void The_free_band_matches_the_predicate_FreeTierAsync_uses()
    {
        var free = LiveLadder().FirstOrDefault(
            t => t.IsActive && t.IsBandedByMembers && t.Prices.Count != 0 && t.Prices.All(p => p.Price == 0m));

        Assert.NotNull(free);
        Assert.Equal("Free", free!.Name);
    }

    /// <summary>
    /// The paid bands are NOT the free band, or a paying group would resolve to the free tier's
    /// capabilities and the fix would gate the wrong people.
    /// </summary>
    [Fact]
    public void No_paid_band_matches_the_free_predicate()
    {
        var matches = LiveLadder()
            .Where(t => t.Prices.Count != 0 && t.Prices.All(p => p.Price == 0m))
            .Select(t => t.Name)
            .ToList();

        Assert.Equal(["Free"], matches);
    }

    /// <summary>
    /// Checkout refuses the free band, so it is a reference tier and not a product. Asserted on the
    /// LIST price because that is what the guard tests.
    /// </summary>
    [Fact]
    public void The_free_band_is_priced_at_nothing_which_is_what_checkout_refuses()
    {
        var free = LiveLadder().Single(t => t.Name == "Free");

        Assert.Equal(0m, SubscriptionPricing.PriceFor(free, BillingInterval.Monthly));
        Assert.True(SubscriptionPricing.PriceFor(free, BillingInterval.Monthly) <= 0m,
            "OrganizationCheckoutController.Start refuses a band whose list price is <= 0.");
    }

    /// <summary>
    /// The pricing page recognises it as free rather than rendering a broken card. Its own rule is
    /// "a band whose every cadence costs nothing", and it needs at least one price row to say so —
    /// which is why the band keeps an ACTIVE price rather than a hidden one.
    /// </summary>
    [Fact]
    public void The_pricing_page_can_tell_it_is_free()
    {
        var free = LiveLadder().Single(t => t.Name == "Free");

        // PricingPage.IsFree: tier.Prices.Count > 0 && tier.Prices.All(p => p.Price == 0)
        Assert.True(free.Prices.Count > 0 && free.Prices.All(p => p.Price == 0m));

        // And it is offered at the cadence the page defaults to, so no "not offered" branch shows.
        Assert.Contains(BillingInterval.Monthly, SubscriptionPricing.AvailableIntervals(free));
    }

    /// <summary>
    /// One person lands on Free and two on the first paid band — the ladder saying what
    /// <c>PaidPlan.WhyCannotAddMemberAsync</c> already enforces.
    /// </summary>
    [Theory]
    [InlineData(0, "Free")]
    [InlineData(1, "Free")]
    [InlineData(2, "Small Group")]
    [InlineData(3, "Small Group")]
    [InlineData(4, "Standard Group")]
    [InlineData(26, "Enterprise")]
    [InlineData(500, "Enterprise")]
    public void A_group_of_this_size_lands_on_this_band(int members, string expected)
        => Assert.Equal(expected, SubscriptionTierResolver.Resolve(LiveLadder(), members).Name);

    /// <summary>
    /// Leaving the paid ladder starting at one member overlaps the free band, and <c>Resolve</c>
    /// throws on an overlapping list. This is the edit that would take the site down, so it is
    /// named rather than left to be discovered.
    /// </summary>
    [Fact]
    public void Leaving_the_paid_ladder_starting_at_one_member_breaks_the_list()
    {
        var ladder = LiveLadder();
        ladder.Single(t => t.Name == "Small Group").MinMembers = 1;

        var problem = SubscriptionTierResolver.Validate(ladder);

        Assert.NotNull(problem);
        Assert.Contains("overlap", problem!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And removing the free band is what the live site was doing: the ladder still validates, so
    /// nothing complains, and every capability check silently fails open.
    /// </summary>
    [Fact]
    public void A_ladder_with_no_free_band_still_validates_which_is_why_this_went_unnoticed()
    {
        var ladder = LiveLadder();
        ladder.RemoveAll(t => t.Name == "Free");
        ladder.Single(t => t.Name == "Small Group").MinMembers = 1;

        // Perfectly sound as a price list...
        Assert.Null(SubscriptionTierResolver.Validate(ladder));

        // ...and nothing in it answers "what does a group with no plan get?".
        Assert.DoesNotContain(ladder, t => t.Prices.Count != 0 && t.Prices.All(p => p.Price == 0m));
    }
}
