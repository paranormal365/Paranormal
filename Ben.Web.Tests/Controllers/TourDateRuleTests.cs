using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A date on a tour business's calendar belongs to a tour (item 233).
/// </summary>
/// <remarks>
/// <para>The tour is what the business pays for and what a guest is actually told about — the
/// meeting point, how long it runs, who is leading it. A public date without one would be a tour
/// run without being counted, and a sign-up page with no answer to "where do I stand?".</para>
///
/// <para>Groups that do not run tours are untouched, which is the other half of this suite: the
/// rule must not turn an investigation group's open evening into an error.</para>
/// </remarks>
public sealed class TourDateRuleTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrgCalendarEventRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrgCalendarEvent e
                ? new OrgCalendarEventRecord
                {
                    Id = e.Id, OrganizationId = e.OrganizationId, Title = e.Title,
                    StartDateTime = e.StartDateTime, EndDateTime = e.EndDateTime,
                    IsPublic = e.IsPublic, TourId = e.TourId,
                    OrganizationAddressId = e.OrganizationAddressId,
                    AttendeeCapacity = e.AttendeeCapacity, DateCreated = e.DateCreated,
                }
                : new OrgCalendarEventRecord { Title = "", StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow });
        return m.Object;
    }

    private static OrgCalendarEventController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var email = new Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(x => x.IsConfigured).Returns(false);
        return new OrgCalendarEventController(
            factory, Mapper(),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory),
            email.Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer())
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
    }

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid OwnerId,
        Guid GuideId, Guid StrangerId, Guid TourId, Guid AddressId);

    private static async Task<World> SeedAsync(
        OrganizationKind kind = OrganizationKind.GhostWalkingTour,
        bool retired = false, bool bookable = true, int? tourCapacity = 20, int? durationMinutes = 90)
    {
        var factory = Factory();
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), guideId = Guid.NewGuid(),
             strangerId = Guid.NewGuid(), tourId = Guid.NewGuid(), addressId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.AddRange(
            new AppUser { Id = ownerId, UserName = "owner", Email = "owner@t.com", DisplayName = "Olive Owner", DateCreated = now },
            new AppUser { Id = guideId, UserName = "guide", Email = "guide@t.com", DisplayName = "Gale Guide", DateCreated = now },
            new AppUser { Id = strangerId, UserName = "s", Email = "s@t.com", DisplayName = "Sam Stranger", DateCreated = now });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = "paw", Kind = kind,
            RunsPublicTours = kind == OrganizationKind.GhostWalkingTour,
            DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = guideId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.Tours.Add(new Tour
        {
            Id = tourId, OrganizationId = orgId, Name = "Printers Alley Walk", UrlName = "printers-alley-walk",
            StartOrganizationAddressId = addressId, DurationMinutes = durationMinutes,
            DefaultCapacity = tourCapacity, IsBookable = bookable,
            RetiredAtUtc = retired ? now.AddDays(-1) : null,
            DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.TourGuides.Add(new TourGuide
        {
            Id = Guid.NewGuid(), TourId = tourId, AppUserId = guideId, SortOrder = 0,
            DateCreated = now, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return new World(factory, orgId, ownerId, guideId, strangerId, tourId, addressId);
    }

    private static UpsertCalendarEventRequest Date(
        bool isPublic = true, Guid? tourId = null, IReadOnlyList<Guid>? guides = null,
        DateTime? start = null, DateTime? end = null, int? capacity = null, Guid? addressId = null)
    {
        var from = start ?? DateTime.UtcNow.AddDays(3);
        return new UpsertCalendarEventRequest(
            "Saturday walk", null, null, from, end ?? from, false, isPublic, null, null, null,
            OrganizationAddressId: addressId, MeetingUrl: null, PlaceId: null,
            HideExactLocation: false, AttendeeCapacity: capacity, RsvpClosesAt: null,
            TourId: tourId, GuideAppUserIds: guides);
    }

    // ── the rule ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_tour_business_cannot_put_a_public_date_on_the_calendar_without_a_tour()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: null), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("belongs to one of your tours", refusal.Value!.ToString());
    }

    [Fact]
    public async Task A_private_date_of_a_tour_business_needs_no_tour()
    {
        // A guides' meeting is not a tour. The rule is about what is sold, not about the calendar.
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId)
            .Create(w.OrgId, Date(isPublic: false, tourId: null), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task A_group_that_runs_no_tours_is_untouched()
    {
        var w = await SeedAsync(OrganizationKind.InvestigationGroup);
        var result = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: null), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task A_retired_tour_takes_no_new_dates_and_says_why()
    {
        var w = await SeedAsync(retired: true);
        var result = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: w.TourId), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("is retired", refusal.Value!.ToString());
    }

    [Fact]
    public async Task A_paused_tour_takes_no_public_dates()
    {
        var w = await SeedAsync(bookable: false);
        var result = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: w.TourId), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("is paused", refusal.Value!.ToString());
    }

    [Fact]
    public async Task A_date_cannot_name_a_tour_belonging_to_somebody_else()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: other.TourId), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("isn't one of yours", refusal.Value!.ToString());
    }

    // ── what the tour fills in ───────────────────────────────────────────────

    [Fact]
    public async Task A_date_takes_its_meeting_point_length_and_size_from_its_tour()
    {
        // Re-typing them per date is how three dates of one tour end up meeting in three places.
        var w = await SeedAsync();
        var start = DateTime.UtcNow.AddDays(3);
        var result = await Build(w.Factory, w.OwnerId)
            .Create(w.OrgId, Date(tourId: w.TourId, start: start, end: start), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        await using var db = await w.Factory.CreateDbContextAsync();
        var ev = await db.OrgCalendarEvents.SingleAsync();
        Assert.Equal(w.AddressId, ev.OrganizationAddressId);
        Assert.Equal(20, ev.AttendeeCapacity);
        Assert.Equal(start.AddMinutes(90), ev.EndDateTime);
    }

    [Fact]
    public async Task A_date_that_says_its_own_size_keeps_it()
    {
        var w = await SeedAsync();
        await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: w.TourId, capacity: 8), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(8, (await db.OrgCalendarEvents.SingleAsync()).AttendeeCapacity);
    }

    // ── guides ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_date_inherits_the_tours_guides()
    {
        var w = await SeedAsync();
        await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: w.TourId), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var guide = await db.OrgCalendarEventGuides.SingleAsync();
        Assert.Equal(w.GuideId, guide.AppUserId);
    }

    [Fact]
    public async Task A_date_can_name_its_own_guides_because_one_night_is_not_another()
    {
        // Ben, 2026-09-10: "if it is led by more than one person, it would not be the same picture
        // for each tour."
        var w = await SeedAsync();
        await Build(w.Factory, w.OwnerId)
            .Create(w.OrgId, Date(tourId: w.TourId, guides: [w.OwnerId]), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var guide = await db.OrgCalendarEventGuides.SingleAsync();
        Assert.Equal(w.OwnerId, guide.AppUserId);
    }

    [Fact]
    public async Task A_guide_who_is_not_a_member_is_refused_by_name_and_the_date_is_not_left_behind()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, w.OwnerId)
            .Create(w.OrgId, Date(tourId: w.TourId, guides: [w.StrangerId]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Sam Stranger", refusal.Value!.ToString());

        // A half-made date nobody is leading is worse than no date.
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.OrgCalendarEvents.CountAsync());
    }

    [Fact]
    public async Task An_edit_that_says_nothing_about_guides_leaves_them_alone()
    {
        var w = await SeedAsync();
        var created = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: w.TourId), default);
        var id = ((OrgCalendarEventRecord)((CreatedAtActionResult)created.Result!).Value!).Id;

        await Build(w.Factory, w.OwnerId).Update(w.OrgId, id, Date(tourId: w.TourId), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(w.GuideId, (await db.OrgCalendarEventGuides.SingleAsync()).AppUserId);
    }

    [Fact]
    public async Task An_edit_that_names_guides_replaces_them()
    {
        var w = await SeedAsync();
        var created = await Build(w.Factory, w.OwnerId).Create(w.OrgId, Date(tourId: w.TourId), default);
        var id = ((OrgCalendarEventRecord)((CreatedAtActionResult)created.Result!).Value!).Id;

        await Build(w.Factory, w.OwnerId)
            .Update(w.OrgId, id, Date(tourId: w.TourId, guides: [w.OwnerId]), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(w.OwnerId, (await db.OrgCalendarEventGuides.SingleAsync()).AppUserId);
    }
}
