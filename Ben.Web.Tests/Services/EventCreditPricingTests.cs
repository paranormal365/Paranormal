using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The arithmetic of event-credit prices (Ben, 10/21/2026): the full price, the paid-subscriber
/// percentage, bundles, and rounding to the cent.
/// </summary>
/// <remarks>
/// Pure: every number here is checked without a database, because a wrong cent is a wrong receipt.
/// </remarks>
public sealed class EventCreditPricingTests
{
    [Fact]
    public void One_credit_at_the_full_price_is_the_price()
    {
        var q = EventCreditPricing.Quote(99m, 1, null, isSubscriber: false, subscriberPercent: 20m);

        Assert.Equal(1, q.Quantity);
        Assert.Equal(99m, q.FullPrice);
        Assert.Equal(99m, q.Payable);
        Assert.False(q.SubscriberPrice);
    }

    [Fact]
    public void A_paid_subscriber_pays_twenty_percent_so_99_becomes_19_80()
    {
        // Ben's own example: "if it is $99 to purchase an event, a paid subscriber could get it for $20."
        var q = EventCreditPricing.Quote(99m, 1, null, isSubscriber: true, subscriberPercent: 20m);

        Assert.Equal(19.80m, q.Payable);
        Assert.Equal(99m, q.FullPrice);
        Assert.True(q.SubscriberPrice);
    }

    [Fact]
    public void Several_single_credits_are_priced_together_and_the_percentage_is_taken_once()
    {
        // 3 × $33.33 = $99.99; 20% of that is $19.998 → $20.00. Taking 20% of one credit first
        // ($6.666 → $6.67) and multiplying would charge $20.01.
        var q = EventCreditPricing.Quote(33.33m, 3, null, isSubscriber: true, subscriberPercent: 20m);

        Assert.Equal(99.99m, q.FullPrice);
        Assert.Equal(20.00m, q.Payable);
    }

    [Fact]
    public void Half_a_cent_rounds_away_from_zero_like_every_other_price_on_the_site()
    {
        // 5% of $0.50 is $0.025: away from zero gives $0.03, where banker's rounding would give
        // $0.02. 5% of $0.30 is $0.015, which both give as $0.02.
        Assert.Equal(0.03m, EventCreditPricing.AtPercent(0.50m, 5m));
        Assert.Equal(0.02m, EventCreditPricing.AtPercent(0.30m, 5m));
    }

    [Fact]
    public void A_bundle_uses_its_own_price_and_its_own_quantity()
    {
        var bundle = new EventCreditBundle(3, 249m);

        var full = EventCreditPricing.Quote(99m, quantity: 7, bundle, isSubscriber: false, subscriberPercent: 20m);
        Assert.Equal(3, full.Quantity);
        Assert.Equal(249m, full.Payable);

        // The subscriber percentage applies to bundles too.
        var subscriber = EventCreditPricing.Quote(99m, 1, bundle, isSubscriber: true, subscriberPercent: 20m);
        Assert.Equal(49.80m, subscriber.Payable);
    }

    [Fact]
    public void A_total_is_shared_across_its_credits_to_the_cent_and_adds_back_up()
    {
        var shares = EventCreditPricing.Share(250m, 3);

        Assert.Equal([83.34m, 83.33m, 83.33m], shares);
        Assert.Equal(250m, shares.Sum());
        Assert.Equal(19.80m, EventCreditPricing.Share(19.80m, 1).Single());
    }

    [Fact]
    public void Bundles_are_read_one_per_line_smallest_first()
    {
        var bundles = EventCreditPricing.ParseBundles("5 = 399\n3=249.50\n  10: $700  ", out var problem);

        Assert.Null(problem);
        Assert.Equal([3, 5, 10], bundles.Select(b => b.Quantity));
        Assert.Equal([249.50m, 399m, 700m], bundles.Select(b => b.Price));
    }

    [Theory]
    [InlineData("three = 249")]
    [InlineData("1 = 99")]
    [InlineData("21 = 1500")]
    [InlineData("3 = 0")]
    [InlineData("3 = 249\n3 = 240")]
    [InlineData("2=1\n3=1\n4=1\n5=1\n6=1\n7=1\n8=1")]
    public void A_bundle_list_the_site_cannot_honor_is_refused_whole(string raw)
    {
        var bundles = EventCreditPricing.ParseBundles(raw, out var problem);

        Assert.Empty(bundles);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData("12.5%", 12.5)]
    [InlineData("100", 100)]
    public void A_usable_percentage_is_read(string raw, double expected)
        => Assert.Equal((decimal)expected, EventCreditPricing.ParsePercent(raw));

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("twenty")]
    public void A_percentage_outside_1_to_100_is_not_one(string raw)
        => Assert.Null(EventCreditPricing.ParsePercent(raw));

    [Theory]
    [InlineData(SiteSettingKeys.EventCreditSubscriberPercent, "150")]
    [InlineData(SiteSettingKeys.EventCreditBundles, "3 for lots")]
    [InlineData(SiteSettingKeys.GhostWalkMonthlyPriceUsd, "-5")]
    [InlineData(SiteSettingKeys.GhostWalkMonthlyPriceUsd, "$29")]
    [InlineData(SiteSettingKeys.EventCreditPriceUsd, "free")]
    public void The_settings_page_refuses_a_price_it_could_not_honor_with_a_sentence(string key, string value)
    {
        var why = AdminSiteSettingController.WhyNotValid(key, value);

        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Theory]
    [InlineData(SiteSettingKeys.EventCreditSubscriberPercent, "20")]
    [InlineData(SiteSettingKeys.EventCreditBundles, "3 = 249")]
    [InlineData(SiteSettingKeys.GhostWalkMonthlyPriceUsd, "29.99")]
    [InlineData(SiteSettingKeys.EventCreditPriceUsd, "")]
    public void Sound_values_and_empty_ones_may_be_saved(string key, string value)
        => Assert.Null(AdminSiteSettingController.WhyNotValid(key, value));

    [Fact]
    public void The_new_settings_are_listed_in_a_section_with_words_for_the_page()
    {
        foreach (var key in new[]
                 {
                     SiteSettingKeys.EventCreditSubscriberPercent, SiteSettingKeys.EventCreditBundles,
                     SiteSettingKeys.GhostWalkEnabled, SiteSettingKeys.GhostWalkMonthlyPriceUsd,
                 })
        {
            Assert.Contains(SiteSettingKeys.Seed, s => s.Key == key && s.Label.Length > 0 && s.Description.Length > 0);
            Assert.Contains(SiteSettingKeys.Groups, g => g.Keys.Contains(key));
        }
        Assert.Contains(SiteSettingKeys.GhostWalkEnabled, SiteSettingKeys.BooleanKeys);
    }
}
