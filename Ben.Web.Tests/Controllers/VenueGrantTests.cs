using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A verified venue's yes: asked for, given, and taken back (item 235 phase 9).
/// </summary>
/// <remarks>
/// Against a real SQLite database, because the claims here are about rows other rows depend on — a
/// withdrawal that stops an event, revokes its passes and gives its credit back has to do all three
/// in one save, and only a database with foreign keys switched on can say it did.
/// </remarks>
public sealed class VenueGrantTests
{
    private static readonly Guid VenueOrgId = Guid.NewGuid();     // the Thomas House
    private static readonly Guid OrganizerOrgId = Guid.NewGuid(); // Nashville Paranormal
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid VenueManager = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid Porter = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();

    private static readonly DateTime Friday = DateTime.UtcNow.Date.AddDays(40);

    /// <summary>
    /// The manager answers for the Thomas House, the organizer for Nashville Paranormal, and the
    /// porter may read the Thomas House's own bookings and run its door — and nothing anywhere else.
    /// </summary>
    private static Mock<IOrganizationSecurityService> Security()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid org, OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken _) =>
                (user == VenueManager && org == VenueOrgId)
             || (user == Organizer && org == OrganizerOrgId)
             || (user == Porter && org == VenueOrgId
                 && table is OrganizationSecurityTable.EventBooking or OrganizationSecurityTable.EventCheckIn
                 && action is OrganizationSecurityAction.Read or OrganizationSecurityAction.Create));
        return security;
    }

    private static T As<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return controller;
    }

    private static EventVenueController OrganizerSide(SqliteTestDb sqlite)
        => As(new EventVenueController(sqlite.Factory, null!, Security().Object,
            new HostedEventAccess(Security().Object), new PlatformMessageService(sqlite.Factory)), Organizer);

    private static VenueRequestsController VenueSide(SqliteTestDb sqlite, Guid? who = null)
        => As(new VenueRequestsController(sqlite.Factory, null!, Security().Object,
            new PlatformMessageService(sqlite.Factory), new HostedEventCalendarSync()), who ?? VenueManager);

    private static EventGuestMailer NoMail()
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        return new EventGuestMailer(email.Object, Options.Create(new SiteIdentity()), NullLogger<EventGuestMailer>.Instance);
    }

    private static async Task<SqliteTestDb> SeedAsync(bool verified = true)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (VenueManager, "Mrs Cole"), (Organizer, "Sam"), (Porter, "Night Porter"), (Guest, "A Guest") })
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test",
                DisplayName = name, DateCreated = now,
            });

        db.Organizations.AddRange(
            new Organization { Id = VenueOrgId, Name = "The Thomas House", UrlName = "thomas-house",
                               DateCreated = now, CreatedByAppUserId = VenueManager },
            new Organization { Id = OrganizerOrgId, Name = "Nashville Paranormal", UrlName = "nashville-paranormal",
                               DateCreated = now, CreatedByAppUserId = Organizer });

        foreach (var (user, org) in new[] { (VenueManager, VenueOrgId), (Organizer, OrganizerOrgId) })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), AppUserId = user, OrganizationId = org, IsActive = true,
                Role = OrganizationMemberRole.Owner, DateCreated = now, CreatedByAppUserId = user,
            });

        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = Organizer });

        db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
        {
            Id = Guid.NewGuid(), OrganizationId = VenueOrgId, PlaceId = PlaceId,
            VerifiedUtc = verified ? now.AddDays(-30) : null,
            DateCreated = now, CreatedByAppUserId = VenueManager,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<Guid> AnEventAsync(
        SqliteTestDb sqlite, HostedEventLifecycleState state = HostedEventLifecycleState.Draft,
        Guid? orgId = null, int nights = 2, string? name = null)
    {
        await using var db = await sqlite.NewContextAsync();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.HostedEvents.Add(new HostedEvent
        {
            Id = id, OrganizationId = orgId ?? OrganizerOrgId, PlaceId = PlaceId,
            Name = name ?? "Halloween Lock-In", UrlName = $"lock-in-{id:N}",
            StartsOn = Friday, EndsOn = Friday.AddDays(nights - 1),
            LifecycleState = state, ContactLine = "Call us.", DayPassCapacity = 40,
            FirstPublishedUtc = state == HostedEventLifecycleState.Draft ? null : now.AddDays(-5),
            DateCreated = now, CreatedByAppUserId = Organizer,
        });
        for (var i = 0; i < nights; i++)
            db.HostedEventNights.Add(new HostedEventNight
            {
                Id = Guid.NewGuid(), HostedEventId = id, Date = Friday.AddDays(i), SortOrder = i,
                DateCreated = now, CreatedByAppUserId = Organizer,
            });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> AskAndApproveAsync(SqliteTestDb sqlite, Guid eventId,
        bool rooms = false, bool staff = false)
    {
        Assert.IsType<OkObjectResult>((await OrganizerSide(sqlite).Ask(OrganizerOrgId, eventId, new("We'd love to."), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        var requestId = await db.VenueHostingRequests.Where(r => r.HostedEventId == eventId).Select(r => r.Id).SingleAsync();

        Assert.IsType<OkObjectResult>((await VenueSide(sqlite).Approve(VenueOrgId, requestId, new(rooms, true, staff), default)).Result);

        return (await db.HostedEvents.AsNoTracking().SingleAsync(e => e.Id == eventId)).VenueGrantId!.Value;
    }

    private static async Task<bool> ReadyAsync(SqliteTestDb sqlite, Guid eventId)
    {
        await using var db = await sqlite.NewContextAsync();
        var hosted = await db.HostedEvents.Include(e => e.Nights).Include(e => e.LayoutUnits).SingleAsync(e => e.Id == eventId);
        return HostedEventReadiness.IsReady(hosted, await VenueGrants.ForEventAsync(db, hosted, default));
    }

    // ── asking and answering ─────────────────────────────────────────────────

    [Fact]
    public async Task An_organizer_cannot_publish_at_a_verified_venue_until_it_says_yes()
    {
        // The plan's "verified by", on real rows: Nashville Paranormal at the Thomas House.
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);

        Assert.False(await ReadyAsync(sqlite, eventId));

        await AskAndApproveAsync(sqlite, eventId);

        Assert.True(await ReadyAsync(sqlite, eventId));
        await using var db = await sqlite.NewContextAsync();
        var hosted = await db.HostedEvents.SingleAsync(e => e.Id == eventId);
        Assert.Equal(HostedEventVenueArrangement.PlatformGrant, hosted.VenueArrangement);
    }

    [Fact]
    public async Task An_unverified_profile_stops_nobody()
    {
        // Anybody may describe a hall as their venue. Only a proved one makes other groups ask,
        // or a competitor could stop every organizer at a building with one form.
        await using var sqlite = await SeedAsync(verified: false);
        var eventId = await AnEventAsync(sqlite);

        Assert.True(await ReadyAsync(sqlite, eventId));

        var result = await OrganizerSide(sqlite).Ask(OrganizerOrgId, eventId, new(null), default);
        var refused = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("nobody here to ask", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task Asking_twice_before_an_answer_is_refused_in_words()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);

        await OrganizerSide(sqlite).Ask(OrganizerOrgId, eventId, new(null), default);
        var again = await OrganizerSide(sqlite).Ask(OrganizerOrgId, eventId, new(null), default);

        var refused = Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Contains("haven't answered yet", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task A_no_needs_a_reason_and_the_organizer_can_see_it()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);
        await OrganizerSide(sqlite).Ask(OrganizerOrgId, eventId, new(null), default);

        await using var db = await sqlite.NewContextAsync();
        var requestId = await db.VenueHostingRequests.Select(r => r.Id).SingleAsync();

        Assert.IsType<BadRequestObjectResult>((await VenueSide(sqlite).Decline(VenueOrgId, requestId, new("  "), default)).Result);
        Assert.IsType<OkObjectResult>((await VenueSide(sqlite).Decline(VenueOrgId, requestId, new("We're closed for renovation."), default)).Result);

        var seen = Assert.IsType<EventVenueRecord>(Assert.IsType<OkObjectResult>(
            (await OrganizerSide(sqlite).Get(OrganizerOrgId, eventId, default)).Result).Value);
        Assert.Equal(VenueHostingRequestStatus.Declined, seen.LatestRequest!.Status);
        Assert.Equal("We're closed for renovation.", seen.LatestRequest.DecisionNote);
        Assert.True(seen.CanAsk);
    }

    [Fact]
    public async Task A_group_that_is_not_the_venue_cannot_answer_for_it()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);
        await OrganizerSide(sqlite).Ask(OrganizerOrgId, eventId, new(null), default);

        await using var db = await sqlite.NewContextAsync();
        var requestId = await db.VenueHostingRequests.Select(r => r.Id).SingleAsync();

        // The organizer trying to approve their own request through the venue's door.
        var result = await VenueSide(sqlite, Organizer).Approve(VenueOrgId, requestId, new(true, true, true), default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task A_night_added_after_the_yes_needs_asking_again()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);
        await AskAndApproveAsync(sqlite, eventId);

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventNights.Add(new HostedEventNight
            {
                Id = Guid.NewGuid(), HostedEventId = eventId, Date = Friday.AddDays(2), SortOrder = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Organizer,
            });
            await db.SaveChangesAsync();
        }

        Assert.False(await ReadyAsync(sqlite, eventId));
    }

    // ── taking it back ───────────────────────────────────────────────────────

    [Fact]
    public async Task Taking_back_a_yes_stops_the_event_revokes_its_passes_and_returns_the_credit()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);
        var grantId = await AskAndApproveAsync(sqlite, eventId);

        var creditId = Guid.NewGuid();
        var passId = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.SingleAsync(e => e.Id == eventId);
            hosted.LifecycleState = HostedEventLifecycleState.Published;
            hosted.FirstPublishedUtc = DateTime.UtcNow.AddDays(-1);

            db.EventCredits.Add(new EventCredit
            {
                Id = creditId, OwnerOrganizationId = OrganizerOrgId, PriceAtPurchase = 99m,
                PurchasedUtc = DateTime.UtcNow.AddMonths(-1), ExpiresUtc = DateTime.UtcNow.AddMonths(11),
                SpentUtc = DateTime.UtcNow.AddDays(-1), SpentOnHostedEventId = eventId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Organizer,
            });

            var bookingId = Guid.NewGuid();
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = bookingId, HostedEventId = eventId, LeadAppUserId = Guest, PartySize = 2,
                Kind = HostedEventBookingKind.DayPass, Status = HostedEventBookingStatus.Confirmed,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guest,
            });
            db.HostedEventPasses.Add(new HostedEventPass
            {
                Id = passId, HostedEventBookingId = bookingId, Token = Guid.NewGuid().ToString("N"),
                IssuedUtc = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Organizer,
            });
            await db.SaveChangesAsync();
        }

        var refusedWithoutReason = await VenueSide(sqlite).Revoke(VenueOrgId, grantId, new(""), NoMail(), default);
        Assert.IsType<BadRequestObjectResult>(refusedWithoutReason.Result);

        var result = await VenueSide(sqlite).Revoke(VenueOrgId, grantId, new("The roof is being replaced"), NoMail(), default);
        var list = Assert.IsType<VenueRequestListRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Contains("Halloween Lock-In has stopped", list.Note);

        await using var check = await sqlite.NewContextAsync();
        var after = await check.HostedEvents.SingleAsync(e => e.Id == eventId);
        Assert.Equal(HostedEventLifecycleState.VenueWithdrawn, after.LifecycleState);
        Assert.Equal("The roof is being replaced", after.CancelledReason);
        Assert.Null(after.FirstPublishedUtc);

        var credit = await check.EventCredits.SingleAsync(c => c.Id == creditId);
        Assert.Null(credit.SpentOnHostedEventId);

        var pass = await check.HostedEventPasses.SingleAsync(p => p.Id == passId);
        Assert.NotNull(pass.RevokedUtc);
        Assert.Contains("The roof is being replaced", pass.RevokedReason);
    }

    [Fact]
    public async Task Taking_back_a_yes_leaves_an_event_that_already_happened_alone()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);
        var grantId = await AskAndApproveAsync(sqlite, eventId);

        await using (var db = await sqlite.NewContextAsync())
        {
            (await db.HostedEvents.SingleAsync(e => e.Id == eventId)).LifecycleState = HostedEventLifecycleState.Ended;
            await db.SaveChangesAsync();
        }

        await VenueSide(sqlite).Revoke(VenueOrgId, grantId, new("Changed our minds"), NoMail(), default);

        await using var check = await sqlite.NewContextAsync();
        Assert.Equal(HostedEventLifecycleState.Ended, (await check.HostedEvents.SingleAsync(e => e.Id == eventId)).LifecycleState);
    }

    // ── what a yes lends ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_venues_own_people_see_the_board_only_when_lent_and_never_decide()
    {
        await using var sqlite = await SeedAsync();
        var lent = await AnEventAsync(sqlite);
        var notLent = await AnEventAsync(sqlite, name: "Saturday Supper");
        await AskAndApproveAsync(sqlite, lent, staff: true);
        await AskAndApproveAsync(sqlite, notLent, staff: false);

        var access = new HostedEventAccess(Security().Object);
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await access.CanReadBookingsAsync(Porter, OrganizerOrgId, lent, db, default));
        Assert.True(await access.CanRunTheDoorAsync(Porter, OrganizerOrgId, lent, db, default));
        Assert.False(await access.CanDecideBookingsAsync(Porter, OrganizerOrgId, lent, db, default));

        Assert.False(await access.CanReadBookingsAsync(Porter, OrganizerOrgId, notLent, db, default));
    }

    [Fact]
    public async Task A_withdrawn_yes_lends_nothing()
    {
        await using var sqlite = await SeedAsync();
        var eventId = await AnEventAsync(sqlite);
        var grantId = await AskAndApproveAsync(sqlite, eventId, rooms: true, staff: true);

        await VenueSide(sqlite).Revoke(VenueOrgId, grantId, new("No longer"), NoMail(), default);

        var access = new HostedEventAccess(Security().Object);
        await using var db = await sqlite.NewContextAsync();
        var hosted = await db.HostedEvents.SingleAsync(e => e.Id == eventId);

        Assert.False(await access.CanReadBookingsAsync(Porter, OrganizerOrgId, eventId, db, default));
        Assert.Null(await VenueGrants.RoomsLentToAsync(db, hosted, default));
    }

    [Fact]
    public async Task Rooms_are_lent_only_when_the_venue_said_so()
    {
        await using var sqlite = await SeedAsync();
        var withRooms = await AnEventAsync(sqlite);
        var without = await AnEventAsync(sqlite, name: "Saturday Supper");
        await AskAndApproveAsync(sqlite, withRooms, rooms: true);
        await AskAndApproveAsync(sqlite, without, rooms: false);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(VenueOrgId, await VenueGrants.RoomsLentToAsync(db, await db.HostedEvents.SingleAsync(e => e.Id == withRooms), default));
        Assert.Null(await VenueGrants.RoomsLentToAsync(db, await db.HostedEvents.SingleAsync(e => e.Id == without), default));
    }
}
