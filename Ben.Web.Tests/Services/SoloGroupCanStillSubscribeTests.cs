using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A one-member group must be able to buy a plan, or it can never become a two-member group.
/// </summary>
/// <remarks>
/// <para><b>The trap this pins (2026-09-18).</b> Adding a Free band at 1–1 to close the fail-open
/// hole put a one-member group in a corner with no way out:</para>
///
/// <list type="number">
/// <item><c>BillableUnits.PriceAsync</c> resolves the band from the MEMBER COUNT, so one member
/// resolves to Free.</item>
/// <item>Free lists at nothing, and <c>OrganizationCheckoutController.Start</c> refuses a list
/// price of zero — "There is nothing to subscribe to at that size."</item>
/// <item><c>PaidPlan.WhyCannotAddMemberAsync</c> refuses the SECOND member without an active
/// subscription — "Working with other people is part of a paid plan."</item>
/// </list>
///
/// <para>So the group cannot subscribe, because it is too small; and cannot grow, because it has
/// not subscribed. <b>Every group starts with one member</b> — its creator — so this closes the
/// acquisition funnel completely rather than affecting an edge case.</para>
///
/// <para>Before the free band existed, Small Group covered 1–3 and a solo group simply bought it.
/// The band boundary is what changed, so the fix belongs where the band is chosen for a PURCHASE:
/// a group buying a plan buys the cheapest band that is actually sold, not the band its current
/// size sits in.</para>
/// </remarks>
public sealed class SoloGroupCanStillSubscribeTests
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

    /// <summary>The live ladder as it now stands on production.</summary>
    private static List<SubscriptionTier> LiveLadder() =>
    [
        Band("Free", 1, 1, 0, (0m, BillingInterval.Monthly)),
        Band("Small Group", 2, 3, 1, (20m, BillingInterval.Monthly), (200m, BillingInterval.Yearly)),
        Band("Standard Group", 4, 10, 2, (40m, BillingInterval.Monthly), (400m, BillingInterval.Yearly)),
        Band("Large Group", 11, 25, 3, (60m, BillingInterval.Monthly), (600m, BillingInterval.Yearly)),
        Band("Enterprise", 26, null, 4, (100m, BillingInterval.Monthly), (1000m, BillingInterval.Yearly)),
    ];

    /// <summary>
    /// The first jaw of the trap: a solo group prices at nothing, which checkout refuses.
    /// </summary>
    [Fact]
    public void A_solo_group_resolves_to_a_band_that_checkout_will_not_sell()
    {
        var tier = SubscriptionTierResolver.Resolve(LiveLadder(), memberCount: 1);
        var listPrice = SubscriptionPricing.PriceFor(tier, BillingInterval.Monthly);

        Assert.Equal("Free", tier.Name);
        Assert.Equal(0m, listPrice);

        // OrganizationCheckoutController.Start: `if (listPrice <= 0m) return BadRequest(...)`.
        Assert.True(listPrice <= 0m,
            "a list price of zero is what Start refuses, so this group cannot buy anything");
    }

    /// <summary>
    /// The second jaw: and it cannot add the member that would price it into a sellable band.
    /// </summary>
    [Fact]
    public async Task And_a_solo_group_with_no_plan_cannot_add_a_second_member()
    {
        await using var db = new BenDataContext(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "A brand new group", UrlName = $"g{Guid.NewGuid():N}"[..10],
            Kind = OrganizationKind.InvestigationGroup,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        var refusal = await PaidPlan.WhyCannotAddMemberAsync(db, orgId, default);

        Assert.NotNull(refusal);
        Assert.Contains("paid plan", refusal!);
    }

    /// <summary>
    /// The way out: a group BUYING a plan is priced at the cheapest band that is actually sold —
    /// the band it is moving into, not the one its current size sits in.
    /// </summary>
    [Fact]
    public async Task A_solo_group_buying_a_plan_is_priced_at_the_first_paid_band()
    {
        await using var db = await SeedAsync(members: 1);

        var priced = await BillableUnits.PriceAsync(
            db, LiveLadder(), OrgId, OrganizationKind.InvestigationGroup, BillingInterval.Monthly, default);

        Assert.NotNull(priced);
        Assert.Equal("Small Group", priced!.Tier.Name);
        Assert.Equal(20m, priced.ListPrice);

        // And it is above zero, which is the whole point: Start refuses a list price of zero.
        Assert.True(priced.ListPrice > 0m, "a solo group must be able to buy something");
    }

    /// <summary>A group already big enough is priced exactly as it always was.</summary>
    [Theory]
    [InlineData(2, "Small Group", 20)]
    [InlineData(3, "Small Group", 20)]
    [InlineData(4, "Standard Group", 40)]
    [InlineData(30, "Enterprise", 100)]
    public async Task A_group_that_already_fits_a_paid_band_is_unchanged(
        int members, string band, int price)
    {
        await using var db = await SeedAsync(members);

        var priced = await BillableUnits.PriceAsync(
            db, LiveLadder(), OrgId, OrganizationKind.InvestigationGroup, BillingInterval.Monthly, default);

        Assert.NotNull(priced);
        Assert.Equal(band, priced!.Tier.Name);
        Assert.Equal(price, priced.ListPrice);
    }

    /// <summary>
    /// The general resolver is untouched: a solo group still SITS on the free band, which is what
    /// governs its caps and capabilities. Only what it would BUY steps up.
    /// </summary>
    [Fact]
    public void What_a_solo_group_sits_on_is_still_the_free_band()
        => Assert.Equal("Free", SubscriptionTierResolver.Resolve(LiveLadder(), memberCount: 1).Name);

    private static readonly Guid OrgId = Guid.NewGuid();

    private static async Task<BenDataContext> SeedAsync(int members)
    {
        var db = new BenDataContext(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "A group", UrlName = $"g{Guid.NewGuid():N}"[..10],
            Kind = OrganizationKind.InvestigationGroup,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });
        for (var i = 0; i < members; i++)
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = Guid.NewGuid(),
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
            });
        await db.SaveChangesAsync();
        return db;
    }
}
