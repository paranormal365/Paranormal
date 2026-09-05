using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A group is never free (Ben, 2026-09-05).
/// </summary>
/// <remarks>
/// <para>"I don't think a 'free group' should ever be a subscribable thing. An individual can be
/// free... a group cannot."</para>
///
/// <para>What made the rule necessary: a one-member group priced into a band that cost nothing, so
/// pressing Subscribe took the path built for a 100%-off coupon and wrote a real Active
/// subscription with no card. Every paywall asks the same question — is there an active
/// subscription — so that single click lifted the member limit, the storage cap, private field
/// sessions and private event evidence. Demonstrated by growing such a group to six members
/// without a charge.</para>
///
/// <para>The distinction these pin: a band priced at zero cannot be sold, while a real price
/// discounted to zero by a coupon still can. The second is item 195's trial and must keep
/// working.</para>
/// </remarks>
public sealed class FreeGroupIsNotSubscribableTests
{
    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeCheckoutSpec> Sessions = [];
        public bool IsConfigured => true;

        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
        {
            Sessions.Add(spec);
            return Task.FromResult(new StripeCheckoutHandle("https://checkout.stripe.test/1", "cus_fake"));
        }

        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
            => throw new NotSupportedException();

        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId);

    /// <summary>A group of one, on a ladder whose only band is priced at <paramref name="monthly"/>.</summary>
    private static async Task<World> SeedAsync(decimal monthly)
    {
        var f = CreateFactory();
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), tierId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = ownerId, UserName = "o@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "G", UrlName = $"g-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Free", MinMembers = 1, MaxMembers = null,
            SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = monthly, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId);
    }

    private static OrganizationCheckoutController Build(World w, FakeGateway gateway)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://ishaunted.test" })
            .Build();
        var ctrl = new OrganizationCheckoutController(
            w.F, new Mock<IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
            gateway,
            new StripeFulfillmentService(w.F, NullLogger<StripeFulfillmentService>.Instance),
            config);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, w.OwnerId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    // ── the point of sale ────────────────────────────────────────────────────

    [Fact]
    public async Task A_band_priced_at_nothing_cannot_be_bought()
    {
        var w = await SeedAsync(monthly: 0m);
        var gateway = new FakeGateway();

        var result = (await Build(w, gateway).Start(w.OrgId,
            new StartCheckoutRequest(BillingInterval.Monthly, null), default)).Result;

        var refusal = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("nothing to subscribe to", refusal.Value?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(gateway.Sessions);
    }

    /// <summary>
    /// The whole exploit in one assertion: the refusal has to leave the group with no subscription
    /// at all. A row written and then rolled back in spirit is still a row every paywall reads.
    /// </summary>
    [Fact]
    public async Task Refusing_it_leaves_the_group_with_no_subscription_at_all()
    {
        var w = await SeedAsync(monthly: 0m);

        await Build(w, new FakeGateway()).Start(w.OrgId,
            new StartCheckoutRequest(BillingInterval.Monthly, null), default);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.False(await db.OrganizationSubscriptions.AnyAsync(s => s.OrganizationId == w.OrgId));
    }

    [Fact]
    public async Task A_band_with_a_real_price_is_still_sold()
    {
        var w = await SeedAsync(monthly: 15m);
        var gateway = new FakeGateway();

        var result = (await Build(w, gateway).Start(w.OrgId,
            new StartCheckoutRequest(BillingInterval.Monthly, null), default)).Result;

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(gateway.Sessions);
    }

    // ── the price list says so out loud ──────────────────────────────────────

    [Fact]
    public void The_editor_is_told_when_a_band_is_priced_at_nothing()
    {
        var tiers = LadderWithMonthlyPrice(0m);

        var advisory = SubscriptionTierResolver.WhyGroupsCanStillBeFree(tiers);

        Assert.NotNull(advisory);
        Assert.Contains("priced at nothing", advisory);
    }

    [Fact]
    public void A_ladder_that_charges_for_every_band_draws_no_complaint()
        => Assert.Null(SubscriptionTierResolver.WhyGroupsCanStillBeFree(LadderWithMonthlyPrice(15m)));

    /// <summary>
    /// The advisory must not become a blocker. <c>Resolve</c> throws on an unsound list, so a
    /// database whose ladder already carries a free band would stop pricing anything at all —
    /// checkout, the permission-area gate and the renewal job together. A pricing mistake is not
    /// worth an outage.
    /// </summary>
    [Fact]
    public void A_free_band_still_prices_and_still_resolves()
    {
        var tiers = LadderWithMonthlyPrice(0m);

        Assert.Null(SubscriptionTierResolver.Validate(tiers));
        Assert.Equal("Free", SubscriptionTierResolver.Resolve(tiers, 1).Name);
    }

    private static List<SubscriptionTier> LadderWithMonthlyPrice(decimal monthly)
    {
        var id = Guid.NewGuid();
        var tier = new SubscriptionTier
        {
            Id = id, Name = "Free", MinMembers = 1, MaxMembers = null,
            SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        };
        tier.Prices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = id,
            Interval = BillingInterval.Monthly, Price = monthly, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = tier.CreatedByAppUserId,
        });
        return [tier];
    }
}
