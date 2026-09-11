using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tours: the product a tour business pays for, and the rules that keep two of them apart
/// (item 233).
/// </summary>
/// <remarks>
/// The rules worth regressing are the ones Ben named: a tour must start somewhere, and two tours
/// of the same business must have different names — those two together are what make "a tour on
/// one street" and "another tour for another street" different things rather than the same thing
/// scheduled twice.
/// </remarks>
public sealed class TourControllerTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>A gateway that is configured but never asked to charge anything here.</summary>
    private sealed class SilentGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool IsConfigured => true;
        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(true, $"pi_{Charges.Count}", null));
        }
        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private static TourController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId, IStripeGateway? gateway = null)
    {
        var ctrl = new TourController(
            factory, new Mock<IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory),
            new CmsMarkupSanitizer(),
            new TourAddOnService(gateway ?? new SilentGateway(), NullLogger<TourAddOnService>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };
        return ctrl;
    }

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid OwnerId,
        Guid GuideId, Guid StrangerId, Guid AddressId, Guid OtherOrgAddressId);

    private static async Task<World> SeedAsync(OrganizationKind kind = OrganizationKind.GhostWalkingTour)
    {
        var factory = Factory();
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), otherOrgId = Guid.NewGuid(),
             ownerId = Guid.NewGuid(), guideId = Guid.NewGuid(), strangerId = Guid.NewGuid(),
             addressId = Guid.NewGuid(), otherAddressId = Guid.NewGuid(), addressTypeId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.AddRange(
            new AppUser { Id = ownerId, UserName = "owner", Email = "owner@t.com", DisplayName = "Olive Owner", DateCreated = now },
            new AppUser { Id = guideId, UserName = "guide", Email = "guide@t.com", DisplayName = "Gale Guide", DateCreated = now },
            new AppUser { Id = strangerId, UserName = "stranger", Email = "s@t.com", DisplayName = "Sam Stranger", DateCreated = now });
        db.Organizations.AddRange(
            new Organization { Id = orgId, Name = "Printers Alley Walks", UrlName = "paw", Kind = kind, DateCreated = now, CreatedByAppUserId = ownerId },
            new Organization { Id = otherOrgId, Name = "Somebody Else", UrlName = "else", DateCreated = now, CreatedByAppUserId = strangerId });
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = guideId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId });
        db.OrganizationAddresses.AddRange(
            new OrganizationAddress { Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = addressTypeId, StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", DateCreated = now, CreatedByAppUserId = ownerId },
            new OrganizationAddress { Id = otherAddressId, OrganizationId = otherOrgId, OrganizationAddressTypeId = addressTypeId, StreetAddress1 = "9 Elsewhere", City = "Memphis", State = "TN", ZipCode = "38103", Country = "US", DateCreated = now, CreatedByAppUserId = strangerId });
        await db.SaveChangesAsync();

        return new World(factory, orgId, ownerId, guideId, strangerId, addressId, otherAddressId);
    }

    private static UpsertTourRequest Request(Guid addressId, string name = "Printers Alley Walk")
        => new(name, null, addressId, 90, 20, "America/Chicago");

    // ── the two things that tell tours apart ─────────────────────────────────

    [Fact]
    public async Task A_tour_is_created_with_a_slug_and_the_meeting_point_it_was_given()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Request(w.AddressId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var tour = Assert.IsType<TourRecord>(created.Value);
        Assert.Equal("Printers Alley Walk", tour.Name);
        Assert.Equal("printers-alley-walk", tour.UrlName);
        Assert.Equal(w.AddressId, tour.StartOrganizationAddressId);
        Assert.Equal("1 Printers Alley, Nashville TN", tour.StartAddressLabel);
        Assert.True(tour.IsActive);
    }

    [Fact]
    public async Task Two_tours_of_one_business_cannot_share_a_name()
    {
        // Ben's open question, answered: the start location is required, and the NAME is what
        // separates two tours that leave from the same corner.
        var w = await SeedAsync();
        var controller = Build(w.Factory, w.OwnerId);
        await controller.Create(w.OrgId, Request(w.AddressId), default);

        var second = await controller.Create(w.OrgId, Request(w.AddressId), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(second.Result);
        Assert.Contains("already have a tour called", refusal.Value!.ToString());
    }

    [Fact]
    public async Task Two_tours_from_the_same_corner_with_different_names_are_two_tours()
    {
        var w = await SeedAsync();
        var controller = Build(w.Factory, w.OwnerId);
        await controller.Create(w.OrgId, Request(w.AddressId, "Printers Alley Walk"), default);
        var second = await controller.Create(w.OrgId, Request(w.AddressId, "Church Street Walk"), default);

        Assert.IsType<CreatedAtActionResult>(second.Result);
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Tours.CountAsync(t => t.OrganizationId == w.OrgId));
    }

    [Fact]
    public async Task A_tour_cannot_start_at_somebody_elses_address()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId)
            .Create(w.OrgId, Request(w.OtherOrgAddressId), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("one of your own addresses", refusal.Value!.ToString());
    }

    [Theory]
    [InlineData("", "needs a name")]
    [InlineData("   ", "needs a name")]
    public async Task A_tour_needs_a_name(string name, string expected)
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId)
            .Create(w.OrgId, Request(w.AddressId, name), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(expected, refusal.Value!.ToString());
    }

    [Fact]
    public async Task Nonsense_lengths_sizes_and_zones_are_refused_in_words()
    {
        var w = await SeedAsync();
        var c = Build(w.Factory, w.OwnerId);

        var tooLong = await c.Create(w.OrgId, new UpsertTourRequest("A", null, w.AddressId, 5000, 20, "America/Chicago"), default);
        Assert.Contains("15 minutes and 12 hours", Assert.IsType<BadRequestObjectResult>(tooLong.Result).Value!.ToString());

        var tooBig = await c.Create(w.OrgId, new UpsertTourRequest("B", null, w.AddressId, 90, 9000, "America/Chicago"), default);
        Assert.Contains("1 and 500 people", Assert.IsType<BadRequestObjectResult>(tooBig.Result).Value!.ToString());

        var noSuchZone = await c.Create(w.OrgId, new UpsertTourRequest("C", null, w.AddressId, 90, 20, "Mars/Olympus"), default);
        Assert.Contains("isn't a time zone", Assert.IsType<BadRequestObjectResult>(noSuchZone.Result).Value!.ToString());
    }

    [Fact]
    public async Task Renaming_a_tour_keeps_the_link_people_have_already_shared()
    {
        var w = await SeedAsync();
        var c = Build(w.Factory, w.OwnerId);
        var created = (TourRecord)((CreatedAtActionResult)(await c.Create(w.OrgId, Request(w.AddressId), default)).Result!).Value!;

        var updated = await c.Update(w.OrgId, created.Id,
            Request(w.AddressId, "The Printers Alley Ghost Walk"), default);

        var tour = Assert.IsType<TourRecord>(Assert.IsType<OkObjectResult>(updated.Result).Value);
        Assert.Equal("The Printers Alley Ghost Walk", tour.Name);
        Assert.Equal("printers-alley-walk", tour.UrlName);
    }

    // ── guides ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_owner_puts_guides_on_a_tour_and_need_not_be_one()
    {
        // Ben, 2026-09-10: "The tour owner is not necessarily the tour guide."
        var w = await SeedAsync();
        var c = Build(w.Factory, w.OwnerId);
        var created = (TourRecord)((CreatedAtActionResult)(await c.Create(w.OrgId, Request(w.AddressId), default)).Result!).Value!;

        var result = await c.SetGuides(w.OrgId, created.Id, new SetTourGuidesRequest([w.GuideId]), default);

        var tour = Assert.IsType<TourRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var guide = Assert.Single(tour.Guides);
        Assert.Equal(w.GuideId, guide.AppUserId);
        Assert.Equal("Gale Guide", guide.DisplayName);
        Assert.DoesNotContain(tour.Guides, g => g.AppUserId == w.OwnerId);
    }

    [Fact]
    public async Task Somebody_who_is_not_a_member_cannot_be_listed_as_a_guide()
    {
        var w = await SeedAsync();
        var c = Build(w.Factory, w.OwnerId);
        var created = (TourRecord)((CreatedAtActionResult)(await c.Create(w.OrgId, Request(w.AddressId), default)).Result!).Value!;

        var result = await c.SetGuides(w.OrgId, created.Id, new SetTourGuidesRequest([w.StrangerId]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Sam Stranger", refusal.Value!.ToString());
        Assert.Contains("isn't a member", refusal.Value!.ToString());
    }

    // ── retirement ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Retiring_a_tour_leaves_what_it_has_run_alone_and_stops_it_counting()
    {
        var w = await SeedAsync();
        var c = Build(w.Factory, w.OwnerId);
        var created = (TourRecord)((CreatedAtActionResult)(await c.Create(w.OrgId, Request(w.AddressId), default)).Result!).Value!;

        var retired = Assert.IsType<TourRecord>(
            Assert.IsType<OkObjectResult>((await c.Retire(w.OrgId, created.Id, default)).Result).Value);
        Assert.False(retired.IsActive);
        Assert.Contains("counts one tour fewer", retired.PlanNote);

        var restored = Assert.IsType<TourRecord>(
            Assert.IsType<OkObjectResult>((await c.Restore(w.OrgId, created.Id, default)).Result).Value);
        Assert.True(restored.IsActive);
    }

    // ── who may do what ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_ordinary_member_may_read_the_tours_but_not_change_them()
    {
        var w = await SeedAsync();
        await Build(w.Factory, w.OwnerId).Create(w.OrgId, Request(w.AddressId), default);

        var asGuide = Build(w.Factory, w.GuideId);
        var list = await asGuide.GetAll(w.OrgId, default);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<TourRecord>>(
            Assert.IsType<OkObjectResult>(list.Result).Value));

        var attempt = await asGuide.Create(w.OrgId, Request(w.AddressId, "Sneaky Walk"), default);
        Assert.IsType<ForbidResult>(attempt.Result);
    }

    [Fact]
    public async Task Somebody_outside_the_business_sees_nothing()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.StrangerId).GetAll(w.OrgId, default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── the plan sentence ────────────────────────────────────────────────────

    [Fact]
    public async Task The_plan_says_what_a_business_runs_and_what_it_has_paid_for()
    {
        var w = await SeedAsync();
        await Build(w.Factory, w.OwnerId).Create(w.OrgId, Request(w.AddressId), default);
        await Build(w.Factory, w.OwnerId).Create(w.OrgId, Request(w.AddressId, "Church Street Walk"), default);

        var plan = Assert.IsType<TourPlanRecord>(
            Assert.IsType<OkObjectResult>((await Build(w.Factory, w.OwnerId).GetPlan(w.OrgId, default)).Result).Value);

        Assert.Equal(2, plan.ActiveTours);
        Assert.Equal(0, plan.CoveredTours);   // nothing subscribed yet
        Assert.False(plan.HasCardOnFile);
    }
}
