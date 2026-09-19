using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A guest picking places on the plan, and losing the race for one (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>The race is the point.</b> Two guests pressing the button a millisecond apart both see
/// the seat free before either writes, so the application layer cannot settle it — the unique index
/// does, and this endpoint's job is to turn the violation into a sentence naming the square that
/// went. These tests run against SQLite with foreign keys and unique indexes on, because an
/// in-memory provider would enforce neither and would let every one of them pass while proving
/// nothing.</para>
/// </remarks>
public sealed class HostedEventHoldTests
{
    /// <summary>A mailer with nothing behind it — what every environment has by default.</summary>
    /// <remarks>
    /// Injected per-action from phase 6, because the guest's own doors send letters now. Not
    /// configured, so nothing is sent and every one of these tests still tests the decision rather
    /// than the post.
    /// </remarks>
    private static EventGuestMailer NoMail()
    {
        var email = new Moq.Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);

        return new EventGuestMailer(
            email.Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EventGuestMailer>.Instance);
    }

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid RivalId = Guid.NewGuid();

    private static readonly Guid FridayId = Guid.NewGuid();
    private static readonly Guid SeatA1Id = Guid.NewGuid();
    private static readonly Guid SeatA2Id = Guid.NewGuid();

    private static async Task<SqliteTestDb> SeedAsync(
        HostedEventBookingMode mode = HostedEventBookingMode.Pick)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[]
                 { (HostId, "The Host"), (GuestId, "A Guest"), (RivalId, "Another Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test",
                DisplayName = name, DateCreated = now,
                // The profile already says who they are and how to reach them, so these guests are
                // asked for nothing more (slice 11d); BookingContactTests covers the ones who are.
                FirstName = name.Split(' ')[0], LastName = name.Split(' ')[^1], PhoneNumber = "615-555-0100",
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        var hosted = new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "An Evening of Evidence", UrlName = "an-evening-of-evidence",
            StartsOn = now.Date.AddDays(30), EndsOn = now.Date.AddDays(30),
            LifecycleState = HostedEventLifecycleState.Published,
            LayoutKind = HostedEventLayoutKind.Seats,
            BookingMode = mode,
            HoldMinutes = 2880,
            DateCreated = now, CreatedByAppUserId = HostId,
        };
        db.HostedEvents.Add(hosted);
        db.HostedEventNights.Add(new HostedEventNight
        {
            Id = FridayId, HostedEventId = EventId, Date = now.Date.AddDays(30), SortOrder = 0,
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        foreach (var (id, label, order) in new[] { (SeatA1Id, "A1", 0), (SeatA2Id, "A2", 1) })
        {
            db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit
            {
                Id = id, HostedEventId = EventId, Label = label, Capacity = 1, SortOrder = order,
                DateCreated = now, CreatedByAppUserId = HostId,
            });
        }

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static PublicHostedEventBookingController As(SqliteTestDb sqlite, Guid userId)
        => new(sqlite.Factory, new HostedEventCalendarSync())
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

    /// <summary>The organizer's own board controller, signed in as the host.</summary>
    private static Ben.Data.WebApi.Controllers.Entities.HostedEventBookingController BoardAs(
        SqliteTestDb sqlite, Guid userId)
    {
        var security = new Moq.Mock<Ben.Service.RepositoryService.GenericInterfaces
            .IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var email = new Moq.Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);

        var site = Microsoft.Extensions.Options.Options.Create(
            new Ben.Data.Common.SiteIdentity { Name = "IsHaunted.com" });

        return new Ben.Data.WebApi.Controllers.Entities.HostedEventBookingController(
            sqlite.Factory,
            new Moq.Mock<AutoMapper.IMapper>().Object,
            security.Object,
            new HostedEventCalendarSync(),
            new Ben.Data.WebApi.Services.Access.HostedEventAccess(security.Object),
            new EventGuestMailer(email.Object, site,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EventGuestMailer>.Instance),
            email.Object,
            site,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                Ben.Data.WebApi.Controllers.Entities.HostedEventBookingController>.Instance)
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

    private static HoldHostedEventPlacesRequest Picking(params Guid[] seats)
        => new([.. seats.Select(s => new HostedEventBookingNightChoice(FridayId, s))], seats.Length);

    // ── taking places ────────────────────────────────────────────────────────

    [Fact]
    public async Task Picking_seats_holds_them_with_a_deadline_from_the_event()
    {
        await using var sqlite = await SeedAsync();

        var result = await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.Include(b => b.Nights).SingleAsync();

        Assert.Equal(HostedEventBookingStatus.Held, booking.Status);
        Assert.NotNull(booking.HoldExpiresUtc);
        // Two days, which is the event's own HoldMinutes rather than anything this endpoint chose.
        Assert.InRange(booking.HoldExpiresUtc!.Value,
            DateTime.UtcNow.AddDays(2).AddMinutes(-5), DateTime.UtcNow.AddDays(2).AddMinutes(5));

        // And the night row says it is holding, which is what the arbiter index reads.
        // Counted first: Assert.All over an empty collection passes, and an earlier version of
        // this test did exactly that while the writer was silently discarding the seat.
        var night = Assert.Single(booking.Nights);
        Assert.True(night.IsHolding);
        Assert.Equal(SeatA1Id, night.HostedEventLayoutUnitId);
    }

    [Fact]
    public async Task A_held_booking_is_on_the_umbrella_as_waiting_rather_than_as_coming()
    {
        // Somebody waiting on an answer is on the list as waiting. Marking them Accepted would put
        // them in every count and every reminder for a place the venue has not agreed to.
        await using var sqlite = await SeedAsync();

        await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default);

        await using var db = await sqlite.NewContextAsync();
        var attendee = await db.OrgCalendarEventAttendees.SingleAsync();

        Assert.Equal(RsvpStatus.Invited, attendee.RsvpStatus);
        Assert.Equal(TourSeatStatus.Requested, attendee.SeatStatus);
    }

    // ── the race ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_second_guest_to_reach_a_seat_is_told_which_one_went()
    {
        // The whole reason this endpoint catches a database error rather than checking first.
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        var refused = Assert.IsType<ConflictObjectResult>(
            (await As(sqlite, RivalId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        var body = Assert.IsType<HoldRefusedRecord>(refused.Value);

        // Named, because "those seats are taken" in front of four hundred of them tells a guest
        // nothing they can act on.
        Assert.Contains("A1", body.Sentence);
        Assert.Contains(SeatA1Id, body.TakenUnitIds);
    }

    [Fact]
    public async Task Losing_one_seat_of_two_names_only_the_one_that_went()
    {
        // So a party of four who lost one seat does not have to choose all four again.
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        var refused = Assert.IsType<ConflictObjectResult>(
            (await As(sqlite, RivalId).HoldPlaces(EventId, Picking(SeatA1Id, SeatA2Id), NoMail(), default)).Result);

        var body = Assert.IsType<HoldRefusedRecord>(refused.Value);

        Assert.Contains(SeatA1Id, body.TakenUnitIds);
        Assert.DoesNotContain(SeatA2Id, body.TakenUnitIds);
    }

    [Fact]
    public async Task A_lost_race_leaves_nothing_behind()
    {
        // All or nothing. Half a party's seats held and half refused is a booking somebody has to
        // unpick by hand, and the guest would not know which half they had.
        await using var sqlite = await SeedAsync();

        await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default);
        await As(sqlite, RivalId).HoldPlaces(EventId, Picking(SeatA1Id, SeatA2Id), NoMail(), default);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(1, await db.HostedEventBookings.CountAsync());
        Assert.Equal(1, await db.HostedEventBookingNights.CountAsync());
    }

    // ── what it refuses before the database has to ───────────────────────────

    [Fact]
    public async Task An_event_where_the_venue_places_people_refuses_a_pick()
    {
        // Helping yourself to seats on an Ask event is taking the thing the venue meant to decide.
        await using var sqlite = await SeedAsync(HostedEventBookingMode.Ask);

        var refused = Assert.IsType<ConflictObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        Assert.Contains("asked for", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task Picking_nothing_is_refused_before_anything_is_written()
    {
        await using var sqlite = await SeedAsync();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, new([], 2), NoMail(), default)).Result);

        Assert.Contains("Pick at least one", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task Somebody_already_waiting_is_not_allowed_a_second_go()
    {
        // A party asking twice is one party asking twice, and two bookings would have the venue
        // decide the same people's evening separately.
        await using var sqlite = await SeedAsync();

        await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default);

        var refused = Assert.IsType<ConflictObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA2Id), NoMail(), default)).Result);

        Assert.Contains("already have places", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task A_seat_the_venue_is_holding_back_cannot_be_taken()
    {
        await using var sqlite = await SeedAsync();

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock
            {
                Id = Guid.NewGuid(), HostedEventLayoutUnitId = SeatA1Id,
                HostedEventNightId = FridayId, Kind = HostedEventBlockKind.HouseHeld,
                Note = "The owner's family",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            await db.SaveChangesAsync();
        }

        var refused = Assert.IsType<ConflictObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        var said = Assert.IsType<string>(refused.Value);
        Assert.Contains("being used by the venue", said);
        // And never the venue's own note. "The owner's family" is exactly what a host writes on a
        // block and exactly what must not reach a guest.
        Assert.DoesNotContain("owner's family", said);
    }

    [Fact]
    public async Task A_guest_who_changes_their_mind_gives_the_seat_straight_back()
    {
        // There was no branch for a held booking in the withdraw endpoint, so a guest who picked
        // seats and changed their mind fell through to the confirmed path and merely REQUESTED a
        // cancellation. The seats stayed theirs until the hold lapsed — out of everybody's reach,
        // for a booking they had abandoned. Nothing has been decided and nothing catered against,
        // so there is nobody to ask.
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        Assert.IsType<NoContentResult>(
            (await As(sqlite, GuestId).Withdraw(EventId, reason: null, default)).Result);

        // And somebody else can have it at once.
        Assert.IsType<OkObjectResult>(
            (await As(sqlite, RivalId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        var letGo = await db.HostedEventBookings
            .Include(b => b.Nights)
            .FirstAsync(b => b.LeadAppUserId == GuestId);

        Assert.Equal(HostedEventBookingStatus.Cancelled, letGo.Status);
        // Kept, not deleted: the row is the record of what they held and when they let it go.
        Assert.All(letGo.Nights, n => Assert.NotNull(n.ReleasedUtc));
        Assert.All(letGo.Nights, n => Assert.False(n.IsHolding));
    }

    [Fact]
    public async Task On_a_seating_plan_the_party_is_the_number_of_seats()
    {
        // A seat holds one person, so asking to hold ONE seat for four people is a booking the
        // venue could never confirm — confirming checks capacity, a seat seats one, and the guest
        // would sit in a queue that has no answer. Worse than being refused when they picked.
        await using var sqlite = await SeedAsync();

        var greedy = new HoldHostedEventPlacesRequest(
            [new HostedEventBookingNightChoice(FridayId, SeatA1Id)], PartySize: 4);

        Assert.IsType<OkObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, greedy, NoMail(), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.SingleAsync();

        Assert.Equal(1, booking.PartySize);
    }

    [Fact]
    public async Task Two_seats_is_a_party_of_two()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(
                EventId, Picking(SeatA1Id, SeatA2Id), NoMail(), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(2, (await db.HostedEventBookings.SingleAsync()).PartySize);
    }

    [Fact]
    public async Task A_held_party_can_be_confirmed_into_the_seat_they_picked()
    {
        // Every confirm test before this one confirmed a party that had ASKED. A party that HELD
        // arrives with night rows already marked as holding and an umbrella attendee already
        // written, and confirming has to move all three without tripping over the rows it is
        // replacing. A board walk found a 500 here; this is the smallest thing that reproduces it.
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>(
            (await As(sqlite, GuestId).HoldPlaces(EventId, Picking(SeatA1Id), NoMail(), default)).Result);

        Guid bookingId;
        await using (var db = await sqlite.NewContextAsync())
            bookingId = (await db.HostedEventBookings.SingleAsync()).Id;

        var board = BoardAs(sqlite, HostId);
        var confirmed = await board.Confirm(OrgId, EventId, bookingId,
            new ConfirmHostedEventBookingRequest(
                [new HostedEventBookingNightChoice(FridayId, SeatA1Id)], "See you Friday."),
            default);

        Assert.IsType<OkObjectResult>(confirmed.Result);

        await using var after = await sqlite.NewContextAsync();
        var booking = await after.HostedEventBookings
            .Include(b => b.Nights)
            .FirstAsync(b => b.Id == bookingId);

        Assert.Equal(HostedEventBookingStatus.Confirmed, booking.Status);
        Assert.Null(booking.HoldExpiresUtc);
        Assert.All(booking.Nights, n => Assert.True(n.IsHolding));

        var attendee = await after.OrgCalendarEventAttendees.SingleAsync();
        Assert.Equal(RsvpStatus.Accepted, attendee.RsvpStatus);
    }
}
