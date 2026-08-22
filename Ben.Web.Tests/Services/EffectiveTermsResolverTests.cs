using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The better-of rule: a paid group keeps what it bought, and only ever gains from a live edit.
/// </summary>
/// <remarks>
/// This is Ben's contract semantics in one resolver — "the tier they signed up for is a contract
/// for the term". The ordering of "better" is easy to invert for one shape of cap without
/// noticing, so these were verified to fail against an inverted resolver before being trusted.
/// </remarks>
public sealed class EffectiveTermsResolverTests
{
    private static SubscriptionTierLimit Cap(SubscriptionLimit limit, int? max) =>
        new() { Limit = limit, MaxValue = max };

    private static SubscriptionContractTerms ContractWith(params (SubscriptionLimit, int?)[] caps) =>
        new()
        {
            TierName   = "As sold",
            Price      = 15m,
            Interval   = BillingInterval.Monthly,
            LimitsJson = EffectiveTermsResolver.ToJson(
                caps.Select(c => Cap(c.Item1, c.Item2))),
        };

    private static EffectiveLimit One(IReadOnlyList<EffectiveLimit> result, SubscriptionLimit limit) =>
        Assert.Single(result, l => l.Limit == limit);

    // ── the four directions a cap can move ────────────────────────────────────

    [Fact]
    public void A_raised_cap_reaches_a_contracted_group_immediately()
    {
        var result = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.EquipmentItems, 25)),
            [Cap(SubscriptionLimit.EquipmentItems, 50)]);

        var cap = One(result, SubscriptionLimit.EquipmentItems);
        Assert.Equal(50, cap.MaxValue);
        Assert.False(cap.FromContract);
    }

    [Fact]
    public void A_lowered_cap_waits_and_the_contract_holds_the_line()
    {
        var result = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.EquipmentItems, 25)),
            [Cap(SubscriptionLimit.EquipmentItems, 10)]);

        var cap = One(result, SubscriptionLimit.EquipmentItems);
        Assert.Equal(25, cap.MaxValue);
        Assert.True(cap.FromContract);
    }

    /// <summary>A cap the tier gains mid-term does not bind a group that paid before it existed.</summary>
    /// <remarks>
    /// No cap at signing means uncapped was part of the deal. The new cap is a reduction and
    /// reductions wait for renewal — the next snapshot picks it up.
    /// </remarks>
    [Fact]
    public void A_cap_added_after_signing_does_not_bind_until_renewal()
    {
        var result = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.EquipmentItems, 25)),
            [Cap(SubscriptionLimit.EquipmentItems, 25), Cap(SubscriptionLimit.OpenCases, 5)]);

        Assert.DoesNotContain(result, l => l.Limit == SubscriptionLimit.OpenCases);
    }

    /// <summary>A cap the tier drops stops binding at once — removal is an improvement.</summary>
    [Fact]
    public void A_cap_dropped_from_the_tier_stops_binding_immediately()
    {
        var result = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.OpenCases, 5)),
            [Cap(SubscriptionLimit.EquipmentItems, 25)]);

        Assert.DoesNotContain(result, l => l.Limit == SubscriptionLimit.OpenCases);
    }

    // ── unlimited, zero, and the free band ────────────────────────────────────

    /// <summary>Null is unlimited, so a cap moving to null is an improvement from any number.</summary>
    [Fact]
    public void A_cap_becoming_unlimited_reaches_the_group_and_a_cap_appearing_under_unlimited_waits()
    {
        var toUnlimited = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.StorageMegabytes, 500)),
            [Cap(SubscriptionLimit.StorageMegabytes, null)]);
        Assert.Null(One(toUnlimited, SubscriptionLimit.StorageMegabytes).MaxValue);

        var fromUnlimited = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.StorageMegabytes, null)),
            [Cap(SubscriptionLimit.StorageMegabytes, 500)]);
        var held = One(fromUnlimited, SubscriptionLimit.StorageMegabytes);
        Assert.Null(held.MaxValue);
        Assert.True(held.FromContract);
    }

    /// <summary>Zero is feature-off; a bought zero rising to a number is an improvement.</summary>
    [Fact]
    public void A_feature_turned_on_for_the_band_reaches_a_group_that_bought_it_off()
    {
        var result = EffectiveTermsResolver.Resolve(
            ContractWith((SubscriptionLimit.PublishedPages, 0)),
            [Cap(SubscriptionLimit.PublishedPages, 3)]);

        Assert.Equal(3, One(result, SubscriptionLimit.PublishedPages).MaxValue);
    }

    /// <summary>
    /// A free-band group has no contract, and the live tier applies exactly as written.
    /// </summary>
    /// <remarks>
    /// Nothing was paid, so nothing is locked — the alternative is never being able to tighten
    /// the free band at all.
    /// </remarks>
    [Fact]
    public void A_group_with_no_contract_gets_the_live_tier_as_is()
    {
        var result = EffectiveTermsResolver.Resolve(
            contract: null,
            [Cap(SubscriptionLimit.OpenCases, 2), Cap(SubscriptionLimit.EquipmentItems, 5)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, One(result, SubscriptionLimit.OpenCases).MaxValue);
        Assert.All(result, l => Assert.False(l.FromContract));
    }

    // ── price ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_price_rise_does_not_touch_the_period_already_paid_for()
    {
        var tier = new SubscriptionTier { Name = "Band" };
        tier.Prices.Add(new SubscriptionTierPrice
            { Interval = BillingInterval.Monthly, Price = 20m, IsActive = true });

        var (price, fromContract) = EffectiveTermsResolver.EffectivePrice(
            ContractWith(), tier);

        Assert.Equal(15m, price);
        Assert.True(fromContract);
    }

    [Fact]
    public void A_price_cut_reaches_the_group_immediately()
    {
        var tier = new SubscriptionTier { Name = "Band" };
        tier.Prices.Add(new SubscriptionTierPrice
            { Interval = BillingInterval.Monthly, Price = 10m, IsActive = true });

        var (price, fromContract) = EffectiveTermsResolver.EffectivePrice(ContractWith(), tier);

        Assert.Equal(10m, price);
        Assert.False(fromContract);
    }

    // ── the JSON round trip ───────────────────────────────────────────────────

    /// <summary>Keys are names, so the stored contract survives an enum renumbering.</summary>
    [Fact]
    public void A_snapshot_round_trips_and_stores_names_not_numbers()
    {
        var json = EffectiveTermsResolver.ToJson(
            [Cap(SubscriptionLimit.OpenCases, 10), Cap(SubscriptionLimit.StorageMegabytes, null)]);

        Assert.Contains("OpenCases", json);
        Assert.DoesNotContain("\"1\"", json);

        var back = EffectiveTermsResolver.FromJson(json);
        Assert.Equal(10, back[SubscriptionLimit.OpenCases]);
        Assert.Null(back[SubscriptionLimit.StorageMegabytes]);
    }

    /// <summary>A key from a retired limit type is skipped, not fatal.</summary>
    [Fact]
    public void An_unknown_key_in_an_old_contract_is_skipped_rather_than_throwing()
    {
        var back = EffectiveTermsResolver.FromJson(
            """{"OpenCases":3,"SomeRetiredThing":9}""");

        Assert.Single(back);
        Assert.Equal(3, back[SubscriptionLimit.OpenCases]);
    }

    [Fact]
    public void A_snapshot_copies_the_tier_name_so_a_rename_cannot_rewrite_the_receipt()
    {
        var tier = new SubscriptionTier { Id = Guid.NewGuid(), Name = "Small group" };
        var sub  = new OrganizationSubscription { Id = Guid.NewGuid() };

        var snapshot = EffectiveTermsResolver.Snapshot(
            sub, tier, BillingInterval.Yearly, 150m,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc), Guid.NewGuid());

        tier.Name = "Renamed later";

        Assert.Equal("Small group", snapshot.TierName);
        Assert.Equal(150m, snapshot.Price);
    }
}
