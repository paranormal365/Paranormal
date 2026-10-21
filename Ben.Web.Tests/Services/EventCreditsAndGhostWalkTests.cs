using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Events;
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
/// Ben's rules of 10/21/2026, end to end at the API: subscriptions cover investigations and never
/// events; event credits are bought by anyone, at a lower price for a paid subscriber and in bundles;
/// the Ghost Walk Tour subscription is its own monthly purchase and is what unlocks new tours.
/// </summary>
public sealed class EventCreditsAndGhostWalkTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    /// <summary>Records every session and charge, and never talks to Stripe.</summary>
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

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid StrangerId, Guid AddressId, Guid TierId);

    /// <summary>
    /// A group with an owner, an address and one paid tier — a tier with NO exclusions, which under
    /// the old rule meant it included hosting events.
    /// </summary>
    private static async Task<World> SeedAsync(
        bool paidPlan = false, OrganizationKind kind = OrganizationKind.InvestigationGroup,
        bool tierBandedByMembers = true, params (string Key, string Value)[] settings)
    {
        var f = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), strangerId = Guid.NewGuid(),
             addressId = Guid.NewGuid(), tierId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.AddRange(
            new AppUser { Id = ownerId, UserName = "owner", Email = "owner@t.com", DisplayName = "Olive Owner", DateCreated = now.AddYears(-1) },
            new AppUser { Id = strangerId, UserName = "stranger", Email = "s@t.com", DisplayName = "Sam Stranger", DateCreated = now });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = "paw", Kind = kind,
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
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Enterprise", MinMembers = 1, MaxMembers = null, SortOrder = 1, IsActive = true,
            IsBandedByMembers = tierBandedByMembers,
            DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId, Interval = BillingInterval.Monthly, Price = 99.99m,
            IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
        });
        if (paidPlan)
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Status = SubscriptionStatus.Active,
                SubscriptionTierId = tierId, Interval = BillingInterval.Monthly, PriceAtPeriodStart = 99.99m,
                CurrentPeriodStart = now.AddDays(-5), CurrentPeriodEnd = now.AddDays(25),
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
        foreach (var (key, value) in settings)
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = key, Value = value, DateCreated = now, CreatedByAppUserId = ownerId,
            });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId, strangerId, addressId, tierId);
    }

    private static readonly (string, string)[] GhostWalkOn =
        [(SiteSettingKeys.GhostWalkEnabled, "true"), (SiteSettingKeys.GhostWalkMonthlyPriceUsd, "29")];

    private static ControllerContext As(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
        },
    };

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://ishaunted.test" }).Build();

    private static OrganizationCheckoutController Checkout(World w, IStripeGateway gateway, Guid asUser)
        => new(w.F, new Mock<IMapper>().Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
               gateway, new StripeFulfillmentService(w.F, NullLogger<StripeFulfillmentService>.Instance), Config())
        { ControllerContext = As(asUser) };

    private static MyEventCreditsController Mine(World w, IStripeGateway gateway, Guid asUser)
        => new(w.F, gateway, Config()) { ControllerContext = As(asUser) };

    private static StripeFulfillmentService Fulfillment(World w) => new(w.F, NullLogger<StripeFulfillmentService>.Instance);

    /// <summary>The webhook's delivery of a session we opened, carrying exactly its metadata.</summary>
    private static StripeCompletedCheckout Delivered(StripeCheckoutSpec spec, string intent = "pi_1")
        => new("cs_1", intent, "cus_test", "pm_test", spec.Metadata);

    // ── no plan includes events ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Even_the_top_plan_with_nothing_excluded_publishes_no_event_without_a_credit()
    {
        // Before 10/21/2026 a tier with no HostEvents exclusion published events against a cap
        // instead of a credit, so this group — on the top band, paid up — would have been told yes.
        var w = await SeedAsync(paidPlan: true);
        await using var db = await w.F.CreateDbContextAsync();

        var verdict = await new HostedEventEntitlement(new SubscriptionLimitGuard(w.F)).DescribeAsync(db, w.OrgId);

        Assert.Equal(HostedEventEntitlement.EntitlementKind.Credit, verdict.Kind);
        Assert.False(verdict.MayPublish);
        Assert.Contains("$99.00", verdict.Refusal);
        // And the paid group is told its own lower price in the same sentence.
        Assert.Contains("$19.80", verdict.Refusal);
    }

    [Fact]
    public async Task The_price_list_never_reports_an_event_capability_as_part_of_a_plan()
    {
        var w = await SeedAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            // A tier carrying an exclusion, so the payload lists inclusions rather than null.
            db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
            {
                Id = Guid.NewGuid(), SubscriptionTierId = w.TierId, Capability = TierCapability.CaseTransfers,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await new PublicPricingController(w.F).GetTiers(default);
        var tiers = Assert.IsAssignableFrom<IEnumerable<PublicSubscriptionTier>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var included = Assert.Single(tiers).IncludedCapabilities!;

        Assert.DoesNotContain(TierCapability.HostEvents, included);
        Assert.DoesNotContain(TierCapability.EventCredits, included);
        Assert.DoesNotContain(TierCapability.EventTicketing, included);
        Assert.Contains(TierCapability.PrivateResidenceCases, included);
    }

    [Fact]
    public async Task The_handover_gives_a_credit_for_each_draft_a_plan_used_to_cover_once_only()
    {
        var w = await SeedAsync(paidPlan: true);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.HostedEvents.AddRange(
                Draft(w, "Upcoming weekend", DateTime.UtcNow.AddMonths(2)),
                Draft(w, "A weekend long gone", DateTime.UtcNow.AddMonths(-2)),
                Draft(w, "Already published once", DateTime.UtcNow.AddMonths(1), firstPublished: DateTime.UtcNow.AddDays(-3)));
            await db.SaveChangesAsync();
        }

        var job = new EventPlanTransitionJob(w.F, NullLogger<EventPlanTransitionJob>.Instance);
        await job.RunAsync(default);
        await job.RunAsync(default);

        await using var check = await w.F.CreateDbContextAsync();
        var credit = Assert.Single(check.EventCredits);
        Assert.Equal(w.OrgId, credit.OwnerOrganizationId);
        Assert.Equal(EventPlanTransitionJob.Reason, credit.GrantedReason);
        Assert.Equal(0m, credit.PriceAtPurchase);
        Assert.Empty(check.BillingLedgerEntries);
    }

    [Fact]
    public async Task The_handover_gives_nothing_to_a_group_whose_plan_never_covered_events()
    {
        var w = await SeedAsync(paidPlan: false);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.HostedEvents.Add(Draft(w, "Upcoming weekend", DateTime.UtcNow.AddMonths(2)));
            await db.SaveChangesAsync();
        }

        await new EventPlanTransitionJob(w.F, NullLogger<EventPlanTransitionJob>.Instance).RunAsync(default);

        await using var check = await w.F.CreateDbContextAsync();
        Assert.Empty(check.EventCredits);
    }

    [Fact]
    public async Task The_handover_gives_nothing_where_the_plan_had_hosting_switched_off()
    {
        // A paid plan whose band had HostEvents excluded never covered an event, so its drafts
        // always needed credits and the change takes nothing from it.
        var w = await SeedAsync(paidPlan: true);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
            {
                Id = Guid.NewGuid(), SubscriptionTierId = w.TierId, Capability = TierCapability.HostEvents,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            db.HostedEvents.Add(Draft(w, "Upcoming weekend", DateTime.UtcNow.AddMonths(2)));
            await db.SaveChangesAsync();
        }

        await new EventPlanTransitionJob(w.F, NullLogger<EventPlanTransitionJob>.Instance).RunAsync(default);

        await using var check = await w.F.CreateDbContextAsync();
        Assert.Empty(check.EventCredits);
    }

    private static HostedEvent Draft(World w, string name, DateTime starts, DateTime? firstPublished = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = w.OrgId, Name = name, UrlName = Guid.NewGuid().ToString("N"),
        StartsOn = starts.Date, EndsOn = starts.Date.AddDays(1),
        LifecycleState = HostedEventLifecycleState.Draft, FirstPublishedUtc = firstPublished,
        DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
    };

    // ── event-credit checkout ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_group_with_a_paid_plan_is_charged_twenty_percent()
    {
        var w = await SeedAsync(paidPlan: true);
        var gateway = new RecordingGateway();

        var result = await Checkout(w, gateway, w.OwnerId).StartEventCreditCheckout(w.OrgId, 1, null, default);

        Assert.IsType<OkObjectResult>(result.Result);
        var spec = Assert.Single(gateway.Sessions);
        Assert.Equal(19.80m, spec.Payable);
        Assert.Equal(19.80m, decimal.Parse(spec.Metadata[StripeFulfillmentService.CheckoutFacts.Keys.List],
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("+subscriber", spec.Metadata[StripeFulfillmentService.CheckoutFacts.Keys.CreditBasis]);
    }

    [Fact]
    public async Task A_group_without_a_paid_plan_pays_the_full_price_from_the_settings()
    {
        var w = await SeedAsync(settings: [(SiteSettingKeys.EventCreditPriceUsd, "120")]);
        var gateway = new RecordingGateway();

        await Checkout(w, gateway, w.OwnerId).StartEventCreditCheckout(w.OrgId, 2, null, default);

        Assert.Equal(240m, Assert.Single(gateway.Sessions).Payable);
    }

    [Fact]
    public async Task A_bundle_is_charged_its_own_price_and_a_missing_bundle_is_refused()
    {
        var w = await SeedAsync(paidPlan: true, settings: [(SiteSettingKeys.EventCreditBundles, "3 = 249")]);
        var gateway = new RecordingGateway();
        var controller = Checkout(w, gateway, w.OwnerId);

        await controller.StartEventCreditCheckout(w.OrgId, 1, bundle: 3, default);
        var spec = Assert.Single(gateway.Sessions);
        Assert.Equal(49.80m, spec.Payable);
        Assert.Equal("3", spec.Metadata[StripeFulfillmentService.CheckoutFacts.Keys.EventCredits]);

        var refused = await controller.StartEventCreditCheckout(w.OrgId, 1, bundle: 5, default);
        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Single(gateway.Sessions);
    }

    [Fact]
    public async Task Somebody_outside_the_group_cannot_spend_its_money_on_credits()
    {
        var w = await SeedAsync();
        var gateway = new RecordingGateway();

        var result = await Checkout(w, gateway, w.StrangerId).StartEventCreditCheckout(w.OrgId, 1, null, default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(gateway.Sessions);
    }

    [Fact]
    public async Task Credits_off_sale_are_refused_before_stripe_is_asked()
    {
        var w = await SeedAsync(settings: [(SiteSettingKeys.EventCreditsEnabled, "false")]);
        var gateway = new RecordingGateway();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await Checkout(w, gateway, w.OwnerId).StartEventCreditCheckout(w.OrgId, 1, null, default)).Result);
        Assert.Equal(EventCreditCheckout.NotOnSale, refused.Value);
        Assert.Empty(gateway.Sessions);
    }

    [Fact]
    public async Task A_person_buys_credits_for_themselves_at_the_full_price_and_holds_them()
    {
        var w = await SeedAsync(paidPlan: true);
        var gateway = new RecordingGateway();

        Assert.IsType<OkObjectResult>((await Mine(w, gateway, w.StrangerId).Checkout(2, null, default)).Result);
        var spec = Assert.Single(gateway.Sessions);
        Assert.Equal(198m, spec.Payable);
        Assert.False(spec.Metadata.ContainsKey(StripeFulfillmentService.CheckoutFacts.Keys.Organization));

        await Fulfillment(w).FulfillAsync(Delivered(spec));

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(2, await db.EventCredits.CountAsync(c => c.OwnerAppUserId == w.StrangerId && c.OwnerOrganizationId == null));
        var payment = await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment);
        Assert.Null(payment.OrganizationId);
        Assert.Equal(w.StrangerId, payment.PayerAppUserId);
        Assert.Equal(198m, payment.Amount);
        Assert.NotNull(payment.ReceiptNumber);
    }

    [Fact]
    public async Task Signed_out_nobody_can_open_a_personal_checkout()
    {
        var w = await SeedAsync();
        var gateway = new RecordingGateway();
        var controller = new MyEventCreditsController(w.F, gateway, Config())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<UnauthorizedResult>((await controller.Checkout(1, null, default)).Result);
        Assert.Empty(gateway.Sessions);
    }

    // ── fulfilment ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_bundle_delivered_twice_hands_over_one_set_of_credits_that_add_up_to_the_receipt()
    {
        var w = await SeedAsync(settings: [(SiteSettingKeys.EventCreditBundles, "3 = 250")]);
        var gateway = new RecordingGateway();
        await Checkout(w, gateway, w.OwnerId).StartEventCreditCheckout(w.OrgId, 1, bundle: 3, default);
        var delivery = Delivered(Assert.Single(gateway.Sessions));

        // checkout.session.completed and payment_intent.succeeded, at the same moment.
        await Task.WhenAll(Fulfillment(w).FulfillAsync(delivery), Fulfillment(w).FulfillAsync(delivery));
        await Fulfillment(w).FulfillAsync(delivery);

        await using var db = await w.F.CreateDbContextAsync();
        var credits = await db.EventCredits.Where(c => c.OwnerOrganizationId == w.OrgId).ToListAsync();
        Assert.Equal(3, credits.Count);
        Assert.Equal(250m, credits.Sum(c => c.PriceAtPurchase));
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Payment));
        Assert.Equal(250m, (await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment)).Amount);
    }

    [Fact]
    public async Task A_person_s_credit_is_spent_when_their_group_has_none()
    {
        var w = await SeedAsync();
        var hosted = Draft(w, "Lock-in", DateTime.UtcNow.AddMonths(1));
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.HostedEvents.Add(hosted);
            db.EventCredits.Add(new EventCredit
            {
                Id = Guid.NewGuid(), OwnerAppUserId = w.OwnerId, PriceAtPurchase = 99m, PurchasedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(365), DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await using var ctx = await w.F.CreateDbContextAsync();
        var entitlement = new HostedEventEntitlement(new SubscriptionLimitGuard(w.F));
        Assert.False((await entitlement.DescribeAsync(ctx, w.OrgId)).MayPublish);
        Assert.True((await entitlement.DescribeAsync(ctx, w.OrgId, appUserId: w.OwnerId)).MayPublish);

        var tracked = await ctx.HostedEvents.FirstAsync(e => e.Id == hosted.Id);
        var (spent, refusal) = await entitlement.TakeForAsync(ctx, tracked, w.OwnerId, default);
        Assert.Null(refusal);
        Assert.Equal(w.OwnerId, spent!.OwnerAppUserId);
    }

    // ── the Ghost Walk Tour subscription ─────────────────────────────────────────────────────────

    private static TourController Tours(World w, Guid asUser)
        => new(w.F, new Mock<IMapper>().Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
               new CmsMarkupSanitizer(),
               new TourAddOnService(new RecordingGateway(), NullLogger<TourAddOnService>.Instance))
        { ControllerContext = As(asUser) };

    private static UpsertTourRequest Tour(World w, string name = "Printers Alley Walk")
        => new(name, null, w.AddressId, 90, 20, "America/Chicago");

    [Fact]
    public async Task While_the_subscription_is_off_tours_are_not_held_back_and_nothing_mentions_it()
    {
        var w = await SeedAsync(kind: OrganizationKind.GhostWalkingTour);

        var created = await Tours(w, w.OwnerId).Create(w.OrgId, Tour(w), default);

        Assert.IsType<CreatedAtActionResult>(created.Result);
        var extras = Assert.IsType<PublicPricingExtras>(
            Assert.IsType<OkObjectResult>((await new PublicPricingController(w.F).GetExtras(default)).Result).Value);
        Assert.Null(extras.GhostWalk);
    }

    [Fact]
    public async Task On_with_a_price_a_new_tour_needs_the_subscription_and_the_refusal_names_the_price()
    {
        var w = await SeedAsync(kind: OrganizationKind.GhostWalkingTour, settings: GhostWalkOn);

        var refused = Assert.IsType<BadRequestObjectResult>((await Tours(w, w.OwnerId).Create(w.OrgId, Tour(w), default)).Result);

        Assert.Contains("$29.00", Assert.IsType<string>(refused.Value));
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Empty(db.Tours);
    }

    [Fact]
    public async Task On_with_no_price_set_it_sells_at_Ben_s_29_dollars_and_gates_tours()
    {
        // Ben, 10/21/2026: "$29/month". An unset price is that price, so the switch alone puts it on sale.
        var w = await SeedAsync(kind: OrganizationKind.GhostWalkingTour, settings: [(SiteSettingKeys.GhostWalkEnabled, "true")]);

        var refused = Assert.IsType<BadRequestObjectResult>((await Tours(w, w.OwnerId).Create(w.OrgId, Tour(w), default)).Result);
        Assert.Contains("$29.00 a month per tour route", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task A_standing_subscription_unlocks_tours_and_one_that_ran_out_does_not()
    {
        var w = await SeedAsync(kind: OrganizationKind.GhostWalkingTour, settings: GhostWalkOn);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.GhostWalkSubscriptions.Add(new GhostWalkSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Active, PriceAtPeriodStart = 29m,
                CurrentPeriodStart = DateTime.UtcNow.AddDays(-3), CurrentPeriodEnd = DateTime.UtcNow.AddDays(27),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<CreatedAtActionResult>((await Tours(w, w.OwnerId).Create(w.OrgId, Tour(w), default)).Result);

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var sub = await db.GhostWalkSubscriptions.SingleAsync();
            sub.CurrentPeriodEnd = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.IsType<BadRequestObjectResult>((await Tours(w, w.OwnerId).Create(w.OrgId, Tour(w, "Second walk"), default)).Result);
    }

    [Fact]
    public async Task A_new_tour_date_on_the_calendar_is_held_back_too_but_a_plain_event_is_not()
    {
        var w = await SeedAsync(kind: OrganizationKind.GhostWalkingTour);
        var tourId = Guid.NewGuid();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Tours.Add(new Tour
            {
                Id = tourId, OrganizationId = w.OrgId, Name = "Old walk", UrlName = "old-walk",
                StartOrganizationAddressId = w.AddressId, DurationMinutes = 90, IsBookable = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            foreach (var (key, value) in GhostWalkOn)
                db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = key, Value = value, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId });
            await db.SaveChangesAsync();
        }

        var calendar = Calendar(w);
        var start = DateTime.UtcNow.AddDays(3);
        var tourDate = await calendar.Create(w.OrgId, new UpsertCalendarEventRequest(
            "Saturday walk", null, null, start, start.AddHours(2), false, false, null, null, null, TourId: tourId), default);
        Assert.IsType<BadRequestObjectResult>(tourDate.Result);

        var meeting = await calendar.Create(w.OrgId, new UpsertCalendarEventRequest(
            "Members' meeting", null, null, start, start.AddHours(2), false, false, null, null, null), default);
        Assert.IsNotType<BadRequestObjectResult>(meeting.Result);
    }

    private static OrgCalendarEventController Calendar(World w)
    {
        var email = new Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(x => x.IsConfigured).Returns(false);
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<OrgCalendarEventRecord>(It.IsAny<object>()))
            .Returns(new OrgCalendarEventRecord { Title = "", StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow });
        return new OrgCalendarEventController(w.F, mapper.Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F), email.Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            NullLogger<OrgCalendarEventController>.Instance, new CmsMarkupSanitizer(),
            Support.SilentTourMail.Instance, new ForwardingOutboxQueue(email.Object))
        { ControllerContext = As(w.OwnerId) };
    }

    [Fact]
    public async Task Subscribing_opens_a_checkout_for_one_month_at_the_set_price()
    {
        var w = await SeedAsync(settings: GhostWalkOn);
        var gateway = new RecordingGateway();

        Assert.IsType<OkObjectResult>((await Checkout(w, gateway, w.OwnerId).StartGhostWalkCheckout(w.OrgId, default)).Result);

        var spec = Assert.Single(gateway.Sessions);
        Assert.Equal(29m, spec.Payable);
        Assert.Equal(w.OrgId.ToString(), spec.Metadata[StripeFulfillmentService.CheckoutFacts.Keys.GhostWalk]);
    }

    [Fact]
    public async Task Subscribing_is_refused_when_it_is_off_for_a_stranger_and_when_already_paid_through()
    {
        var off = await SeedAsync();
        var gateway = new RecordingGateway();
        var offResult = Assert.IsType<BadRequestObjectResult>((await Checkout(off, gateway, off.OwnerId).StartGhostWalkCheckout(off.OrgId, default)).Result);
        // Says nothing about the product (Ben: when it's off, say nothing about it).
        Assert.DoesNotContain("Ghost", Assert.IsType<string>(offResult.Value));

        var on = await SeedAsync(settings: GhostWalkOn);
        Assert.IsType<ForbidResult>((await Checkout(on, gateway, on.StrangerId).StartGhostWalkCheckout(on.OrgId, default)).Result);

        await using (var db = await on.F.CreateDbContextAsync())
        {
            db.GhostWalkSubscriptions.Add(new GhostWalkSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = on.OrgId, Status = SubscriptionStatus.Active, PriceAtPeriodStart = 29m,
                CurrentPeriodStart = DateTime.UtcNow, CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = on.OwnerId,
            });
            await db.SaveChangesAsync();
        }
        Assert.IsType<BadRequestObjectResult>((await Checkout(on, gateway, on.OwnerId).StartGhostWalkCheckout(on.OrgId, default)).Result);
        Assert.Empty(gateway.Sessions);
    }

    [Fact]
    public async Task A_month_delivered_twice_opens_one_period_with_one_receipt_and_leaves_the_plan_alone()
    {
        var w = await SeedAsync(paidPlan: true, settings: GhostWalkOn);
        var gateway = new RecordingGateway();
        await Checkout(w, gateway, w.OwnerId).StartGhostWalkCheckout(w.OrgId, default);
        var delivery = Delivered(Assert.Single(gateway.Sessions), "pi_walk");

        await Task.WhenAll(Fulfillment(w).FulfillAsync(delivery), Fulfillment(w).FulfillAsync(delivery));
        await Fulfillment(w).FulfillAsync(delivery);

        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.GhostWalkSubscriptions.SingleAsync();
        Assert.True(sub.IsStanding(DateTime.UtcNow));
        Assert.Equal(29m, sub.PriceAtPeriodStart);
        Assert.Equal("pm_test", sub.ProviderPaymentMethodRef);
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Payment));
        // The investigation plan is a different row, and its price and period are untouched.
        var plan = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(99.99m, plan.PriceAtPeriodStart);
        Assert.Equal(w.TierId, plan.SubscriptionTierId);
    }

    [Fact]
    public async Task The_renewal_charges_the_live_price_and_stitches_the_next_month_on()
    {
        var w = await SeedAsync(settings: [(SiteSettingKeys.GhostWalkEnabled, "true"), (SiteSettingKeys.GhostWalkMonthlyPriceUsd, "35")]);
        var end = DateTime.UtcNow.AddHours(6);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.GhostWalkSubscriptions.Add(new GhostWalkSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Active, PriceAtPeriodStart = 29m,
                CurrentPeriodStart = end.AddMonths(-1), CurrentPeriodEnd = end, ProviderName = "Stripe",
                ProviderCustomerRef = "cus_x", ProviderPaymentMethodRef = "pm_x",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }
        var gateway = new RecordingGateway();
        var job = new StripeRenewalJob(w.F, gateway, Fulfillment(w), NullLogger<StripeRenewalJob>.Instance);

        await job.RunAsync(default);

        Assert.Equal(35m, Assert.Single(gateway.Charges).Total);
        await using var check = await w.F.CreateDbContextAsync();
        var sub = await check.GhostWalkSubscriptions.SingleAsync();
        Assert.Equal(end, sub.CurrentPeriodStart);
        Assert.Equal(end.AddMonths(1), sub.CurrentPeriodEnd);
        Assert.Equal(35m, sub.PriceAtPeriodStart);
    }

    [Fact]
    public async Task Taken_off_sale_or_set_not_to_renew_nobody_is_charged()
    {
        foreach (var (settings, cancel) in new[] { (Array.Empty<(string, string)>(), false), (GhostWalkOn, true) })
        {
            var w = await SeedAsync(settings: settings);
            await using (var db = await w.F.CreateDbContextAsync())
            {
                db.GhostWalkSubscriptions.Add(new GhostWalkSubscription
                {
                    Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Active, PriceAtPeriodStart = 29m,
                    CurrentPeriodStart = DateTime.UtcNow.AddMonths(-1), CurrentPeriodEnd = DateTime.UtcNow.AddHours(2),
                    CancelAtPeriodEnd = cancel, ProviderName = "Stripe", ProviderCustomerRef = "cus_x", ProviderPaymentMethodRef = "pm_x",
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
                });
                await db.SaveChangesAsync();
            }
            var gateway = new RecordingGateway();

            await new StripeRenewalJob(w.F, gateway, Fulfillment(w), NullLogger<StripeRenewalJob>.Instance).RunAsync(default);

            Assert.Empty(gateway.Charges);
        }
    }

    // ── prices shown come from the settings ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_price_list_carries_the_settings_prices_bundles_and_the_ghost_walk_offer()
    {
        var w = await SeedAsync(settings:
        [
            (SiteSettingKeys.EventCreditPriceUsd, "80"), (SiteSettingKeys.EventCreditSubscriberPercent, "25"),
            (SiteSettingKeys.EventCreditBundles, "3 = 200"), .. GhostWalkOn,
        ]);

        var extras = Assert.IsType<PublicPricingExtras>(
            Assert.IsType<OkObjectResult>((await new PublicPricingController(w.F).GetExtras(default)).Result).Value);

        Assert.Equal(80m, extras.Events.UnitPrice);
        Assert.Equal(25m, extras.Events.SubscriberPercent);
        Assert.Equal(20m, extras.Events.SubscriberUnitPrice);
        var bundle = Assert.Single(extras.Events.Bundles!);
        Assert.Equal((3, 200m, 50m), (bundle.Quantity, bundle.FullPrice, bundle.SubscriberPrice));
        Assert.Equal(29m, extras.GhostWalk!.MonthlyPrice);
    }

    [Fact]
    public void Help_reads_today_s_prices_and_drops_the_ghost_walk_passage_when_it_is_off()
    {
        const string markdown = "A credit is {{price.event-credit}} ({{price.event-credit-subscriber}}).\n"
                              + "{{#if ghost-walk}}\nTours are {{price.ghost-walk-month}} a month.\n{{/if}}\nEnd.";
        var on = HelpLiveValues.From(new PublicPricingExtras(2048,
                new PublicEventOffer(true, 99m, 365, 20, 48, 2000, 90, 20m, 19.80m, []),
                new PublicGhostWalkOffer(29m)),
            m => $"${m:0.00}");
        var off = HelpLiveValues.From(new PublicPricingExtras(2048,
                new PublicEventOffer(true, 99m, 365, 20, 48, 2000, 90, 20m, 19.80m, [])),
            m => $"${m:0.00}");

        var shown = HelpContentService.ApplyLiveValues(markdown, on);
        Assert.Contains("A credit is $99.00 ($19.80).", shown);
        Assert.Contains("Tours are $29.00 a month.", shown);
        Assert.DoesNotContain("{{", shown);

        var hidden = HelpContentService.ApplyLiveValues(markdown, off);
        Assert.DoesNotContain("Tours are", hidden);
        Assert.DoesNotContain("{{", hidden);
        Assert.Contains("End.", hidden);
    }

    [Fact]
    public void Every_value_marker_in_the_help_has_words_to_fall_back_on_and_no_help_quotes_a_fixed_event_price()
    {
        var service = new HelpContentService();
        var viewer = HelpViewerAll();
        foreach (var doc in service.SectionsFor(viewer).SelectMany(s => s.Documents))
        {
            var printed = HelpContentService.ApplyLiveValues(doc.Markdown, live: null);
            Assert.DoesNotContain("{{", printed);
            // Event and tour prices are read from the settings; a dollar figure beside the words
            // "per event" or "event credit" would be one typed in.
            Assert.DoesNotMatch(@"\$\d+(\.\d\d)?\s+(per|a|an)\s+(hosted\s+)?event", printed);
            Assert.DoesNotMatch(@"credit(s)?\s*(is|are|costs?)\s*\*{0,2}\$\d", printed);
        }
    }

    private static HelpViewer HelpViewerAll() => new(HelpAudience.AppAdministrator);
}
