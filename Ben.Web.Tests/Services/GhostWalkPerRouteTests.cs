using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Data.WebApi.Services.Tours;
using Ben.Service.Models;
using Ben.Service.Models.Entities;
using Ben.Web.Services.Help;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The Ghost Walk Tour subscription priced per tour route (Ben, 10/21/2026): "$29 a month per tour
/// route, unlimited walks and guests", merged with item 233's per-tour plan.
/// </summary>
public sealed class GhostWalkPerRouteTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private sealed class RecordingGateway : IStripeGateway
    {
        public readonly List<StripeCheckoutSpec> Sessions = [];
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool IsConfigured => true;

        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
        {
            Sessions.Add(spec);
            return Task.FromResult(new StripeCheckoutHandle($"https://checkout.stripe.test/{Sessions.Count}", "cus_test"));
        }

        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(true, $"pi_renew_{Charges.Count}", null));
        }

        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid StrangerId, Guid AddressId);

    private static async Task<World> SeedAsync(bool on = true, int routes = 0)
    {
        var f = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), strangerId = Guid.NewGuid(), addressId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.AddRange(
            new AppUser { Id = ownerId, UserName = "owner", Email = "o@t.com", DisplayName = "Olive", DateCreated = now.AddYears(-1) },
            new AppUser { Id = strangerId, UserName = "stranger", Email = "s@t.com", DisplayName = "Sam", DateCreated = now });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = "paw", Kind = OrganizationKind.GhostWalkingTour,
            DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId, Role = OrganizationMemberRole.Owner,
            IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = now, CreatedByAppUserId = ownerId,
        });
        for (var i = 0; i < routes; i++)
            db.Tours.Add(new Tour
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = $"Route {i + 1}", UrlName = $"route-{i + 1}",
                StartOrganizationAddressId = addressId, DurationMinutes = 90, IsBookable = true,
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
        if (on)
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.GhostWalkEnabled, Value = "true", DateCreated = now, CreatedByAppUserId = ownerId,
            });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId, strangerId, addressId);
    }

    /// <summary>A standing month that started ten days ago, thirty days long, paying for <paramref name="quantity"/> routes.</summary>
    private static async Task<GhostWalkSubscription> SubscribeAsync(World w, int quantity, DateTime? start = null, bool card = true)
    {
        var from = start ?? DateTime.UtcNow.Date.AddDays(-10);
        var sub = new GhostWalkSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Active,
            Quantity = quantity, UnitPriceAtPeriodStart = 29m, PriceAtPeriodStart = 29m * quantity,
            CurrentPeriodStart = from, CurrentPeriodEnd = from.AddDays(30), ProviderName = "Stripe",
            ProviderCustomerRef = card ? "cus_x" : null, ProviderPaymentMethodRef = card ? "pm_x" : null,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        };
        await using var db = await w.F.CreateDbContextAsync();
        db.GhostWalkSubscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub;
    }

    private static ControllerContext As(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
        },
    };

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://ishaunted.test" }).Build();

    private static TourController Tours(World w, RecordingGateway? gateway = null)
        => new(w.F, new Mock<IMapper>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
               new CmsMarkupSanitizer(), new TourAddOnService(gateway ?? new RecordingGateway(), NullLogger<TourAddOnService>.Instance))
        { ControllerContext = As(w.OwnerId) };

    private static UpsertTourRequest Tour(World w, string name, bool addRoute = false)
        => new(name, null, w.AddressId, 90, 20, "America/Chicago", AddRouteToSubscription: addRoute);

    private static OrganizationBillingController Billing(World w, Guid asUser)
        => new(w.F, new Mock<IMapper>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F))
        { ControllerContext = As(asUser) };

    private static StripeFulfillmentService Fulfillment(World w) => new(w.F, NullLogger<StripeFulfillmentService>.Instance);

    // ── the price ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unset_price_is_29_dollars_a_route()
    {
        var w = await SeedAsync();
        await using var db = await w.F.CreateDbContextAsync();

        var offer = await GhostWalkOffer.ReadAsync(db, default);

        Assert.True(offer.OnSale);
        Assert.Equal(29m, offer.MonthlyPrice);
    }

    [Fact]
    public void A_route_added_ten_days_into_a_thirty_day_month_owes_the_twenty_days_left_and_retiring_credits_them()
    {
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var sub = new GhostWalkSubscription
        {
            Quantity = 2, UnitPriceAtPeriodStart = 29m, CurrentPeriodStart = start, CurrentPeriodEnd = start.AddDays(30),
        };

        Assert.Equal(19.33m, GhostWalkRoutes.Prorate(sub, start.AddDays(10), +1));
        Assert.Equal(-19.33m, GhostWalkRoutes.Prorate(sub, start.AddDays(10), -1));
        Assert.Equal(38.67m, GhostWalkRoutes.Prorate(sub, start.AddDays(10), +2));
    }

    [Fact]
    public void The_help_shows_the_per_tour_plan_only_while_the_subscription_is_off()
    {
        var doc = new HelpContentService().SectionsFor(new HelpViewer(HelpAudience.AppAdministrator))
            .SelectMany(s => s.Documents).Single(d => d.Slug == "organization-administration");
        var events = new PublicEventOffer(true, 99m, 365, 20, 48, 2000, 90, 20m, 19.80m, []);

        var on = HelpContentService.ApplyLiveValues(doc.Markdown,
            HelpLiveValues.From(new PublicPricingExtras(2048, events, new PublicGhostWalkOffer(29m)), m => $"${m:0}"))
            .ReplaceLineEndings(" ");
        var off = HelpContentService.ApplyLiveValues(doc.Markdown,
            HelpLiveValues.From(new PublicPricingExtras(2048, events), m => $"${m:0}"))
            .ReplaceLineEndings(" ");

        Assert.Contains("$29 a month per tour route", on);
        Assert.DoesNotContain("On the per-tour business plan**, the plan covers one tour", on);
        Assert.Contains("On the per-tour business plan**, the plan covers one tour", off);
        Assert.DoesNotContain("a month per tour route", off);
    }

    // ── checkout ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribing_pays_for_every_route_the_group_runs()
    {
        var w = await SeedAsync(routes: 3);
        var gateway = new RecordingGateway();
        var checkout = new OrganizationCheckoutController(w.F, new Mock<IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F), gateway, Fulfillment(w), Config())
        { ControllerContext = As(w.OwnerId) };

        Assert.IsType<BadRequestObjectResult>((await checkout.StartGhostWalkCheckout(w.OrgId, default, routes: 2)).Result);
        Assert.IsType<OkObjectResult>((await checkout.StartGhostWalkCheckout(w.OrgId, default)).Result);

        var spec = Assert.Single(gateway.Sessions);
        Assert.Equal(87m, spec.Payable);
        Assert.Equal("3", spec.Metadata[StripeFulfillmentService.CheckoutFacts.Keys.GhostWalkRoutes]);

        await Fulfillment(w).FulfillAsync(new StripeCompletedCheckout("cs_1", "pi_1", "cus_1", "pm_1", spec.Metadata));
        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.GhostWalkSubscriptions.SingleAsync();
        Assert.Equal(3, sub.Quantity);
        Assert.Equal(29m, sub.UnitPriceAtPeriodStart);
        Assert.Contains("3 tour routes", (await db.BillingLedgerEntries.FirstAsync(e => e.Kind == BillingLedgerKind.Charge)).Description);
    }

    // ── gating ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task With_every_paid_route_running_a_new_route_is_asked_for_and_the_yes_adds_one_to_the_next_bill()
    {
        var w = await SeedAsync(routes: 2);
        await SubscribeAsync(w, quantity: 2);

        var refused = Assert.IsType<BadRequestObjectResult>((await Tours(w).Create(w.OrgId, Tour(w, "Third walk"), default)).Result);
        Assert.Contains("pays for 2 routes", Assert.IsType<string>(refused.Value));

        var created = await Tours(w).Create(w.OrgId, Tour(w, "Third walk", addRoute: true), default);
        var record = Assert.IsType<TourRecord>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Contains("now pays for 3 routes", record.PlanNote);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(3, (await db.GhostWalkSubscriptions.SingleAsync()).Quantity);
        var change = await db.GhostWalkRouteChanges.SingleAsync();
        Assert.Equal((2, 3), (change.FromQuantity, change.ToQuantity));
        Assert.True(change.ProratedAmount is > 0m and < 29m);
        Assert.Null(change.SettledUtc);
    }

    [Fact]
    public async Task A_route_the_subscription_already_pays_for_is_used_without_asking_or_charging()
    {
        var w = await SeedAsync(routes: 1);
        await SubscribeAsync(w, quantity: 2);
        var gateway = new RecordingGateway();

        Assert.IsType<CreatedAtActionResult>((await Tours(w, gateway).Create(w.OrgId, Tour(w, "Second walk"), default)).Result);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(2, (await db.GhostWalkSubscriptions.SingleAsync()).Quantity);
        Assert.Empty(db.GhostWalkRouteChanges);
        Assert.Empty(gateway.Charges);
    }

    [Fact]
    public async Task Dates_on_a_paid_route_are_unlimited()
    {
        var w = await SeedAsync(routes: 1);
        await SubscribeAsync(w, quantity: 1);
        await using var db = await w.F.CreateDbContextAsync();

        // Every route is in use, yet a date needs only a standing subscription.
        for (var i = 0; i < 5; i++)
            Assert.Null(await GhostWalkAccess.WhyNotAsync(db, w.OrgId, DateTime.UtcNow, default));
        Assert.True((await GhostWalkAccess.ForOneMoreRouteAsync(db, w.OrgId, DateTime.UtcNow, default)).NeedsARoute);
    }

    [Fact]
    public async Task Retiring_a_route_takes_it_off_the_subscription_with_a_credit_but_never_below_one()
    {
        var w = await SeedAsync(routes: 2);
        await SubscribeAsync(w, quantity: 2);
        Guid[] ids;
        await using (var db = await w.F.CreateDbContextAsync())
            ids = await db.Tours.Select(t => t.Id).ToArrayAsync();

        var first = Assert.IsType<OkObjectResult>((await Tours(w).Retire(w.OrgId, ids[0], default)).Result);
        Assert.Contains("comes off your next bill", ((TourRecord)first.Value!).PlanNote);
        await Tours(w).Retire(w.OrgId, ids[1], default);

        await using var check = await w.F.CreateDbContextAsync();
        Assert.Equal(1, (await check.GhostWalkSubscriptions.SingleAsync()).Quantity);
        var change = await check.GhostWalkRouteChanges.SingleAsync();
        Assert.True(change.ProratedAmount < 0m);
    }

    [Fact]
    public async Task Bringing_a_route_back_when_every_paid_route_runs_asks_too()
    {
        var w = await SeedAsync(routes: 1);
        await SubscribeAsync(w, quantity: 1);
        var retiredId = Guid.NewGuid();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Tours.Add(new Tour
            {
                Id = retiredId, OrganizationId = w.OrgId, Name = "Old walk", UrlName = "old-walk",
                StartOrganizationAddressId = w.AddressId, RetiredAtUtc = DateTime.UtcNow.AddMonths(-1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<BadRequestObjectResult>((await Tours(w).Restore(w.OrgId, retiredId, default)).Result);
        Assert.IsType<OkObjectResult>((await Tours(w).Restore(w.OrgId, retiredId, default, addRoute: true)).Result);

        await using var check = await w.F.CreateDbContextAsync();
        Assert.Equal(2, (await check.GhostWalkSubscriptions.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task The_billing_card_adds_a_route_takes_off_only_an_unused_one_and_is_not_a_stranger_s()
    {
        var w = await SeedAsync(routes: 1);
        await SubscribeAsync(w, quantity: 1);

        Assert.IsType<BadRequestObjectResult>((await Billing(w, w.OwnerId).RemoveGhostWalkRoute(w.OrgId, default)).Result);
        Assert.IsType<ForbidResult>((await Billing(w, w.StrangerId).AddGhostWalkRoute(w.OrgId, default)).Result);

        var added = Assert.IsType<GhostWalkSubscriptionView>(
            Assert.IsType<OkObjectResult>((await Billing(w, w.OwnerId).AddGhostWalkRoute(w.OrgId, default)).Result).Value);
        Assert.Equal(2, added.RoutesPaid);
        Assert.True(added.PendingOnNextBill > 0m);

        var removed = Assert.IsType<GhostWalkSubscriptionView>(
            Assert.IsType<OkObjectResult>((await Billing(w, w.OwnerId).RemoveGhostWalkRoute(w.OrgId, default)).Result).Value);
        Assert.Equal(1, removed.RoutesPaid);
        // The add and the take-off on the same day cancel out on the next bill.
        Assert.Equal(0m, removed.PendingOnNextBill);
    }

    // ── renewal ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_renewal_charges_each_route_at_today_s_price_plus_last_month_s_route_changes_and_settles_them()
    {
        var w = await SeedAsync(routes: 2);
        var sub = await SubscribeAsync(w, quantity: 2, start: DateTime.UtcNow.AddDays(-29.9));
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.GhostWalkRouteChanges.AddRange(
                new GhostWalkRouteChange { Id = Guid.NewGuid(), GhostWalkSubscriptionId = sub.Id, ChangedUtc = DateTime.UtcNow.AddDays(-20), FromQuantity = 1, ToQuantity = 2, ProratedAmount = 10m, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId },
                new GhostWalkRouteChange { Id = Guid.NewGuid(), GhostWalkSubscriptionId = sub.Id, ChangedUtc = DateTime.UtcNow.AddDays(-5), FromQuantity = 3, ToQuantity = 2, ProratedAmount = -4m, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId });
            await db.SaveChangesAsync();
        }
        var gateway = new RecordingGateway();

        await new StripeRenewalJob(w.F, gateway, Fulfillment(w), NullLogger<StripeRenewalJob>.Instance).RunAsync(default);

        Assert.Equal(64m, Assert.Single(gateway.Charges).Total);   // 2 × $29 + $10 − $4, no tax rule
        await using var check = await w.F.CreateDbContextAsync();
        Assert.All(await check.GhostWalkRouteChanges.ToListAsync(), c => Assert.Equal("pi_renew_1", c.SettledPaymentRef));
        var renewed = await check.GhostWalkSubscriptions.SingleAsync();
        Assert.Equal(sub.CurrentPeriodEnd, renewed.CurrentPeriodStart);
        Assert.Equal(64m, renewed.PriceAtPeriodStart);
        Assert.Equal(29m, renewed.UnitPriceAtPeriodStart);
    }

    [Fact]
    public async Task Credits_bigger_than_the_bill_charge_nothing_open_the_month_and_carry_the_rest()
    {
        var w = await SeedAsync(routes: 1);
        var sub = await SubscribeAsync(w, quantity: 1, start: DateTime.UtcNow.AddDays(-29.9), card: false);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.GhostWalkRouteChanges.Add(new GhostWalkRouteChange
            {
                Id = Guid.NewGuid(), GhostWalkSubscriptionId = sub.Id, ChangedUtc = DateTime.UtcNow.AddDays(-3),
                FromQuantity = 3, ToQuantity = 1, ProratedAmount = -40m, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }
        var gateway = new RecordingGateway();

        await new StripeRenewalJob(w.F, gateway, Fulfillment(w), NullLogger<StripeRenewalJob>.Instance).RunAsync(default);

        Assert.Empty(gateway.Charges);
        await using var check = await w.F.CreateDbContextAsync();
        Assert.Equal(sub.CurrentPeriodEnd, (await check.GhostWalkSubscriptions.SingleAsync()).CurrentPeriodStart);
        var carried = await check.GhostWalkRouteChanges.SingleAsync(c => c.SettledUtc == null);
        Assert.Equal(-11m, carried.ProratedAmount);
        Assert.Empty(check.BillingLedgerEntries.Where(e => e.Kind == BillingLedgerKind.Payment));
    }

    // ── the per-tour plan merged in ──────────────────────────────────────────────────────────────

    private static async Task<(Guid TierId, OrganizationSubscription Plan)> PerTourPlanAsync(World w, int toursPaid, DateTime end)
    {
        var tierId = Guid.NewGuid();
        var plan = new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Active, SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, PriceAtPeriodStart = 29m * toursPaid, TourCountAtPeriodStart = toursPaid,
            CurrentPeriodStart = end.AddMonths(-1), CurrentPeriodEnd = end,
            ProviderName = "Stripe", ProviderCustomerRef = "cus_plan", ProviderPaymentMethodRef = "pm_plan",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        };
        await using var db = await w.F.CreateDbContextAsync();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Tour operator", MinMembers = 1, IsActive = true, IsBandedByMembers = false, SortOrder = 9,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId, Interval = BillingInterval.Monthly, Price = 29m, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        db.OrganizationSubscriptions.Add(plan);
        await db.SaveChangesAsync();
        return (tierId, plan);
    }

    private static Task RunTransitionAsync(World w)
        => new GhostWalkTierTransitionJob(w.F, NullLogger<GhostWalkTierTransitionJob>.Instance).RunAsync(default);

    [Fact]
    public async Task A_per_tour_plan_group_moves_onto_the_subscription_for_the_time_it_already_paid_for()
    {
        var w = await SeedAsync(routes: 3);
        var end = DateTime.UtcNow.AddDays(20);
        var (tierId, _) = await PerTourPlanAsync(w, toursPaid: 3, end);

        await RunTransitionAsync(w);
        await RunTransitionAsync(w);

        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.GhostWalkSubscriptions.SingleAsync();
        Assert.Equal(3, sub.Quantity);
        Assert.Equal(0m, sub.PriceAtPeriodStart);
        Assert.Equal(end, sub.CurrentPeriodEnd);
        Assert.Equal(("cus_plan", "pm_plan"), (sub.ProviderCustomerRef, sub.ProviderPaymentMethodRef));
        Assert.True((await db.OrganizationSubscriptions.SingleAsync()).CancelAtPeriodEnd);
        Assert.False((await db.SubscriptionTiers.SingleAsync(t => t.Id == tierId)).IsActive);
        Assert.Empty(db.BillingLedgerEntries);
    }

    [Fact]
    public async Task At_the_end_of_the_paid_period_only_the_subscription_bills_never_the_plan_as_well()
    {
        var w = await SeedAsync(routes: 2);
        await PerTourPlanAsync(w, toursPaid: 2, DateTime.UtcNow.AddHours(6));
        await RunTransitionAsync(w);
        var gateway = new RecordingGateway();

        await new StripeRenewalJob(w.F, gateway, Fulfillment(w), NullLogger<StripeRenewalJob>.Instance).RunAsync(default);

        var charge = Assert.Single(gateway.Charges);
        Assert.Equal(58m, charge.Total);
        Assert.True(charge.Metadata.ContainsKey(StripeFulfillmentService.CheckoutFacts.Keys.GhostWalk));
    }

    [Fact]
    public async Task While_the_subscription_is_off_per_tour_groups_are_left_exactly_as_they_are()
    {
        var w = await SeedAsync(on: false, routes: 2);
        var (tierId, _) = await PerTourPlanAsync(w, toursPaid: 2, DateTime.UtcNow.AddDays(20));

        await RunTransitionAsync(w);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Empty(db.GhostWalkSubscriptions);
        Assert.False((await db.OrganizationSubscriptions.SingleAsync()).CancelAtPeriodEnd);
        Assert.True((await db.SubscriptionTiers.SingleAsync(t => t.Id == tierId)).IsActive);
    }

    [Fact]
    public async Task A_moved_group_adding_a_route_is_never_charged_the_old_per_tour_add_on()
    {
        // Paid for one tour on the old plan, still standing until its period ends. Under the old rule a
        // second tour was charged on the card at once; now it is a route on the subscription, on the next bill.
        var w = await SeedAsync(routes: 1);
        await PerTourPlanAsync(w, toursPaid: 1, DateTime.UtcNow.AddDays(20));
        await RunTransitionAsync(w);
        var gateway = new RecordingGateway();

        Assert.IsType<CreatedAtActionResult>((await Tours(w, gateway).Create(w.OrgId, Tour(w, "Second walk", addRoute: true), default)).Result);

        Assert.Empty(gateway.Charges);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(2, (await db.GhostWalkSubscriptions.SingleAsync()).Quantity);
    }
}
