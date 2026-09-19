using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The door on the night: who is in, who may come in, and how many more (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>Per night is the whole claim.</b> Arrival used to be one stamp on a pass, so a
/// three-night weekend recorded that a party turned up — once — and never which nights. The
/// Saturday door could not tell whether the people in front of it had been in on the Friday, and
/// nobody could answer "who was here on the Saturday" afterwards (defect 19).</para>
///
/// <para><b>And the number, which Ben asked for on 2026-09-13:</b> a steward with somebody in
/// front of them offering cash needs one number, now. It counts everything that takes a place —
/// parties expected tonight and walk-ups already in — against whichever ceiling the event has,
/// and refuses a walk-up that would go past it.</para>
/// </remarks>
public sealed class HostedEventDoorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid OtherGuestId = Guid.NewGuid();

    private sealed record Seeded(Guid FridayId, Guid SaturdayId, Guid SeatId, Guid OtherSeatId);

    private static HostedEventDoorController Door(SqliteTestDb sqlite, Guid userId)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new HostedEventDoorController(
            sqlite.Factory,
            new Mock<AutoMapper.IMapper>().Object,
            security.Object,
            new Ben.Data.WebApi.Services.Access.HostedEventAccess(security.Object))
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

    /// <summary>A two-night event with two seats, so a ceiling and a night both mean something.</summary>
    private static async Task<Seeded> SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[]
                 { (HostId, "The Host"), (GuestId, "A Guest"), (OtherGuestId, "Another Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test",
                DisplayName = name, DateCreated = now,
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

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "An Evening of Evidence", UrlName = "an-evening-of-evidence",
            StartsOn = now.Date.AddDays(20), EndsOn = now.Date.AddDays(21),
            LifecycleState = HostedEventLifecycleState.Published,
            LayoutKind = HostedEventLayoutKind.Seats,
            TimeZoneId = "America/Chicago",
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        var friday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = now.Date.AddDays(20),
            DateCreated = now, CreatedByAppUserId = HostId,
        };
        var saturday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = now.Date.AddDays(21),
            SortOrder = 1, DateCreated = now, CreatedByAppUserId = HostId,
        };
        db.HostedEventNights.AddRange(friday, saturday);

        var a1 = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Label = "A1", Capacity = 1,
            DateCreated = now, CreatedByAppUserId = HostId,
        };
        var a2 = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Label = "A2", Capacity = 1, SortOrder = 1,
            DateCreated = now, CreatedByAppUserId = HostId,
        };
        db.HostedEventLayoutUnits.AddRange(a1, a2);

        await db.SaveChangesAsync();
        return new Seeded(friday.Id, saturday.Id, a1.Id, a2.Id);
    }

    /// <summary>A confirmed party, in a seat, on the nights given.</summary>
    private static async Task<Guid> BookAsync(
        SqliteTestDb sqlite, Guid leadId, Guid seatId, params Guid[] nights)
    {
        await using var db = await sqlite.NewContextAsync();

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = leadId,
            PartySize = 2, Kind = HostedEventBookingKind.Overnight,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = leadId,
        };

        foreach (var nightId in nights)
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                HostedEventNightId = nightId, HostedEventLayoutUnitId = seatId,
                DateCreated = DateTime.UtcNow,
            });
        }

        db.HostedEventBookings.Add(booking);
        BookingTransitions.Confirm(booking, HostId, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        return booking.Id;
    }

    private static async Task<HostedEventDoorRecord> ReadAsync(
        SqliteTestDb sqlite, Guid nightId)
    {
        var answer = await Door(sqlite, HostId).Get(OrgId, EventId, nightId, default);
        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        return Assert.IsType<HostedEventDoorRecord>(ok.Value);
    }

    // ── who is expected ──────────────────────────────────────────────────────

    [Fact]
    public async Task Tonights_list_is_tonights_list()
    {
        // A party staying Friday only is not at Saturday's door, and a steward reading a list that
        // included them would let in somebody with no place.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await BookAsync(sqlite, GuestId, seeded.SeatId, seeded.FridayId);
        await BookAsync(sqlite, OtherGuestId, seeded.OtherSeatId, seeded.FridayId, seeded.SaturdayId);

        Assert.Equal(2, (await ReadAsync(sqlite, seeded.FridayId)).Expected.Count);
        Assert.Single((await ReadAsync(sqlite, seeded.SaturdayId)).Expected);
    }

    [Fact]
    public async Task Arriving_is_recorded_against_the_night_and_not_the_whole_weekend()
    {
        // The defect this phase exists for. Friday's arrival must leave Saturday's door saying
        // "not here yet", or the Saturday steward cannot tell who has walked in.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var booking = await BookAsync(sqlite, GuestId, seeded.SeatId, seeded.FridayId, seeded.SaturdayId);

        var arrived = await Door(sqlite, HostId).Arrive(
            OrgId, EventId, new HostedEventDoorMoveRequest(booking, seeded.FridayId), default);
        Assert.IsType<OkObjectResult>(arrived.Result);

        Assert.NotNull((await ReadAsync(sqlite, seeded.FridayId)).Expected.Single().ArrivedUtc);
        Assert.Null((await ReadAsync(sqlite, seeded.SaturdayId)).Expected.Single().ArrivedUtc);
    }

    [Fact]
    public async Task Pressing_it_twice_is_the_same_arrival()
    {
        // A party walking back in from the car park is not a second arrival, and a door that
        // recorded one would double every count fed from these rows.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var booking = await BookAsync(sqlite, GuestId, seeded.SeatId, seeded.FridayId);

        var door = Door(sqlite, HostId);
        var move = new HostedEventDoorMoveRequest(booking, seeded.FridayId);

        await door.Arrive(OrgId, EventId, move, default);
        var first = (await ReadAsync(sqlite, seeded.FridayId)).Expected.Single().ArrivedUtc;

        await door.Arrive(OrgId, EventId, move, default);
        var again = (await ReadAsync(sqlite, seeded.FridayId)).Expected.Single().ArrivedUtc;

        Assert.Equal(first, again);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(1, await db.HostedEventCheckIns.CountAsync());
    }

    [Fact]
    public async Task Undo_takes_it_back_because_a_doorway_is_where_the_wrong_button_is_pressed()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var booking = await BookAsync(sqlite, GuestId, seeded.SeatId, seeded.FridayId);

        var door = Door(sqlite, HostId);
        var move = new HostedEventDoorMoveRequest(booking, seeded.FridayId);

        await door.Arrive(OrgId, EventId, move, default);
        await door.Undo(OrgId, EventId, move, default);

        Assert.Null((await ReadAsync(sqlite, seeded.FridayId)).Expected.Single().ArrivedUtc);
    }

    [Fact]
    public async Task A_booking_the_venue_has_not_agreed_to_cannot_walk_in()
    {
        // Not a refusal for its own sake: a steward admitting an unconfirmed party has let in
        // somebody nobody has a bed or a seat for, and the sentence says what to do instead.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        Guid booking;
        await using (var db = await sqlite.NewContextAsync())
        {
            var asked = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId,
                PartySize = 2, Kind = HostedEventBookingKind.Overnight,
                Status = HostedEventBookingStatus.Requested,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
            };
            db.HostedEventBookings.Add(asked);
            await db.SaveChangesAsync();
            booking = asked.Id;
        }

        var refused = await Door(sqlite, HostId).Arrive(
            OrgId, EventId, new HostedEventDoorMoveRequest(booking, seeded.FridayId), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains("Confirm it first", conflict.Value?.ToString());
    }

    // ── how many more can come in ────────────────────────────────────────────

    [Fact]
    public async Task The_number_left_counts_everything_that_takes_a_place()
    {
        // Ben, 2026-09-13. Two seats, one party of two expected: nothing left, and the sentence
        // says so in words as well as a number.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        await BookAsync(sqlite, GuestId, seeded.SeatId, seeded.FridayId);

        var door = await ReadAsync(sqlite, seeded.FridayId);

        Assert.Equal(0, door.PlacesLeft);
        Assert.Contains("Full tonight", door.PlacesLeftSentence);
    }

    [Fact]
    public async Task Somebody_who_turns_up_is_written_down_and_counted()
    {
        // No booking, no account, no pass — a head count, which is what a fire officer asks for
        // and what the kitchen is cooking against.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        var answer = await Door(sqlite, HostId).WalkUp(
            OrgId, EventId,
            new HostedEventWalkUpRequest(seeded.FridayId, People: 2, Name: "Somebody at the door"),
            default);

        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        var door = Assert.IsType<HostedEventDoorRecord>(ok.Value);

        Assert.Equal(2, door.WalkUps.Single().People);
        Assert.Equal(2, door.PeopleIn);
        Assert.Equal(0, door.PlacesLeft);
    }

    [Fact]
    public async Task A_walk_up_past_the_last_place_is_refused_in_words()
    {
        // The other half of what the number is for: a steward told there are two places left and
        // letting in four has been failed by the screen, not by their arithmetic.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        var refused = await Door(sqlite, HostId).WalkUp(
            OrgId, EventId, new HostedEventWalkUpRequest(seeded.FridayId, People: 3), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains("only 2 places left", conflict.Value?.ToString());
    }

    [Fact]
    public async Task An_event_with_no_ceiling_says_so_rather_than_inventing_one()
    {
        // A hotel weekend's ceiling is its rooms, and they are already allocated. A number here
        // would be a limit nobody set.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventLayoutUnits.RemoveRange(await db.HostedEventLayoutUnits.ToListAsync());
            await db.SaveChangesAsync();
        }

        var door = await ReadAsync(sqlite, seeded.FridayId);

        Assert.Null(door.PlacesLeft);
        Assert.Null(door.PlacesLeftSentence);
    }
}
