using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models;
using Ben.Service.Models.Support;
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
/// The SuperAdmin switch that takes plans and seats off sale (beta feedback, 2026-09-14).
/// </summary>
/// <remarks>
/// Off must close the door at the API, not only hide the button — a page open from before the switch was flipped
/// still has one — and must never reach the payment provider. Unset must keep selling, because the site has sold
/// plans since 2026-08-30.
/// </remarks>
public sealed class PlanPurchaseSwitchTests
{
    private sealed class CountingGateway : IStripeGateway
    {
        public int Sessions;
        public bool IsConfigured => true;

        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
        {
            Sessions++;
            return Task.FromResult(new StripeCheckoutHandle($"https://checkout.stripe.test/{Sessions}", "cus_fake"));
        }

        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
            => throw new NotSupportedException();

        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid MemberId);

    private static async Task<World> SeedAsync(string? switchValue)
    {
        var f = new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), memberId = Guid.NewGuid(), tierId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = ownerId, UserName = "o@t.com", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = memberId, UserName = "m@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "G", UrlName = $"g-{orgId:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId, Role = OrganizationMemberRole.Owner,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Small group", MinMembers = 1, MaxMembers = null, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId, Interval = BillingInterval.Monthly, Price = 15.00m,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Status = SubscriptionStatus.PendingPayment,
            Interval = BillingInterval.Monthly, PriceAtStart = 5m, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        if (switchValue is not null)
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.PlanPurchasesEnabled, Value = switchValue,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId, memberId);
    }

    private static OrganizationCheckoutController Build(World w, IStripeGateway gateway, Guid asUser)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://ishaunted.test" })
            .Build();
        return new OrganizationCheckoutController(
            w.F, new Mock<IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
            gateway, new StripeFulfillmentService(w.F, NullLogger<StripeFulfillmentService>.Instance), config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, asUser.ToString())], "Bearer")),
                },
            },
        };
    }

    [Fact]
    public void The_switch_is_a_listed_on_off_setting_with_words_for_the_settings_page()
    {
        Assert.Contains(SiteSettingKeys.PlanPurchasesEnabled, SiteSettingKeys.BooleanKeys);
        Assert.Contains(SiteSettingKeys.Seed, s => s.Key == SiteSettingKeys.PlanPurchasesEnabled && s.Label.Length > 0);
        Assert.Contains(SiteSettingKeys.Groups, g => g.Keys.Contains(SiteSettingKeys.PlanPurchasesEnabled));
    }

    [Fact]
    public async Task Off_refuses_a_plan_checkout_in_a_neutral_sentence_and_never_reaches_the_payment_provider()
    {
        var w = await SeedAsync("false");
        var gateway = new CountingGateway();

        var result = (await Build(w, gateway, w.OwnerId).Start(w.OrgId, new StartCheckoutRequest(BillingInterval.Monthly, null), default)).Result;

        var refusal = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(OrganizationCheckoutController.PlansNotOnSale, refusal.Value);
        Assert.Equal(0, gateway.Sessions);
    }

    [Fact]
    public async Task Off_refuses_a_seat_checkout_too()
    {
        var w = await SeedAsync("false");
        var gateway = new CountingGateway();

        var result = (await Build(w, gateway, w.MemberId).StartSeatCheckout(w.OrgId, default)).Result;

        var refusal = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(OrganizationCheckoutController.SeatsNotOnSale, refusal.Value);
        Assert.Equal(0, gateway.Sessions);
    }

    [Theory]
    [InlineData(null)]     // never touched: the site keeps selling
    [InlineData("true")]
    public async Task Unset_or_on_still_sells(string? switchValue)
    {
        var w = await SeedAsync(switchValue);
        var gateway = new CountingGateway();

        var result = (await Build(w, gateway, w.OwnerId).Start(w.OrgId, new StartCheckoutRequest(BillingInterval.Monthly, null), default)).Result;

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, gateway.Sessions);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task The_public_features_answer_carries_the_switch(string? switchValue, bool expected)
    {
        var w = await SeedAsync(switchValue);
        var ok = Assert.IsType<OkObjectResult>((await new PublicSiteFeaturesController(w.F).Get(default)).Result);
        Assert.Equal(expected, Assert.IsType<SiteFeaturesInfo>(ok.Value).PlanPurchasesEnabled);
    }
}
