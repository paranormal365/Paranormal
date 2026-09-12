using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// One plan per event, of one kind, and what a party holds on a night (item 235 phase 2.4).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"Instead of just seat designer, we might need room designer as well in
/// case the event is overnight… They only get one per event."</i> A room and a seat are the same
/// shape underneath — each is chosen while booking and each bounds how many people can come — so
/// one model carries both and these pin the places where they are deliberately different.</para>
///
/// <para>The other rule under test is the one his three-day question found: <b>a night row means
/// "we are here that night", and holding a room is a separate fact about it.</b> That is what lets
/// a party take two nights of three, and what lets a day pass name the Saturday.</para>
/// </remarks>
public sealed class EventLayoutTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly DateTime Friday = new(2026, 10, 30);

    // ── what a unit is called ────────────────────────────────────────────────

    [Fact]
    public void A_room_takes_its_name_from_the_venue_so_renaming_it_reaches_every_event()
    {
        // A Rooms unit deliberately carries no label of its own. Copying the name onto the event
        // would leave last year's spelling on this year's plan.
        var unit = new HostedEventLayoutUnit
        {
            PlaceRoom = new PlaceRoom { Name = "Blue Room", Capacity = 2 },
        };

        Assert.Equal("Blue Room", EventCapacity.NameOf(unit));
    }

    [Fact]
    public void A_seat_carries_its_own_label_because_no_room_stands_behind_it()
    {
        var seat = new HostedEventLayoutUnit { Label = "H9", Capacity = 1 };

        Assert.Equal("H9", EventCapacity.NameOf(seat));
        Assert.Equal(1, EventCapacity.CapacityOf(seat));
    }

    [Fact]
    public void A_unit_with_neither_still_says_something_rather_than_nothing()
    {
        // An empty cell on a booking board reads as broken. A sentence reads as a gap in the data.
        Assert.Equal("That space", EventCapacity.NameOf(new HostedEventLayoutUnit()));
    }

    // ── the words a refusal uses ─────────────────────────────────────────────

    [Fact]
    public void A_room_sleeps_and_a_seat_seats()
    {
        // A theatre told its seats sleep nobody reads as a bug, and the whole point of naming the
        // number is that a host can act on the sentence without translating it.
        var room = EventCapacity.WhyThisRoomCannotTakeThem(
            "Blue Room", Friday, capacity: 2, taken: 0, partySize: 3);
        var seat = EventCapacity.WhyThisRoomCannotTakeThem(
            "Row C", Friday, capacity: 2, taken: 0, partySize: 3, HostedEventLayoutKind.Seats);

        Assert.Contains("sleeps 2 more", room);
        Assert.Contains("seats 2 more", seat);
    }

    [Fact]
    public void A_full_seat_is_told_to_seat_them_elsewhere_rather_than_to_free_a_place()
    {
        var seat = EventCapacity.WhyThisRoomCannotTakeThem(
            "H9", Friday, capacity: 1, taken: 1, partySize: 1, HostedEventLayoutKind.Seats);

        Assert.Contains("is taken", seat);
        Assert.Contains("seat them somewhere else", seat);
    }

    // ── here for the night, holding nothing ──────────────────────────────────

    [Fact]
    public async Task A_party_can_take_two_nights_of_three_and_be_absent_on_the_last()
    {
        // Ben, 2026-09-12: "maybe a three day event they want to be there only two days."
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = Booking(HostedEventBookingStatus.Confirmed, 2);
            foreach (var nightId in new[] { seeded.NightIds[0], seeded.NightIds[1] })
            {
                booking.Nights.Add(new HostedEventBookingNight
                {
                    Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                    HostedEventNightId = nightId, HostedEventLayoutUnitId = seeded.BlueUnitId,
                });
            }
            db.HostedEventBookings.Add(booking);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var all = await db.HostedEventBookings.Include(b => b.Nights).ToListAsync();

            Assert.Equal(2, EventCapacity.PeopleIn(all, seeded.NightIds[0], seeded.BlueUnitId));
            Assert.Equal(2, EventCapacity.PeopleIn(all, seeded.NightIds[1], seeded.BlueUnitId));
            // The Sunday is free, which is the whole point: the room can be sold to somebody else.
            Assert.Equal(0, EventCapacity.PeopleIn(all, seeded.NightIds[2], seeded.BlueUnitId));
        }
    }

    [Fact]
    public async Task A_day_pass_can_name_the_saturday_without_holding_a_room()
    {
        // "Here that day, sleeping elsewhere." Making the unit required would have forced a fake
        // room onto every day guest and left "which day is this pass for" unanswerable.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = Booking(HostedEventBookingStatus.Confirmed, 3,
                                  HostedEventBookingKind.DayPass);
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                HostedEventNightId = seeded.NightIds[1], HostedEventLayoutUnitId = null,
            });
            db.HostedEventBookings.Add(booking);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var night = await db.HostedEventBookingNights
                .Include(n => n.HostedEventLayoutUnit)
                .SingleAsync();

            Assert.Null(night.HostedEventLayoutUnitId);
            Assert.Equal("Just for the day", EventCapacity.NameOf(night));

            // They hold a day pass and no bed, which is exactly what was asked for.
            var all = await db.HostedEventBookings.Include(b => b.Nights).ToListAsync();
            Assert.Equal(3, EventCapacity.DayPassesTaken(all));
            Assert.Equal(0, EventCapacity.PeopleIn(all, seeded.NightIds[1], seeded.BlueUnitId));
        }
    }

    [Fact]
    public async Task A_seat_holds_one_person_and_the_second_party_is_refused_by_name()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite, HostedEventLayoutKind.Seats);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = Booking(HostedEventBookingStatus.Confirmed, 1);
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                HostedEventNightId = seeded.NightIds[0], HostedEventLayoutUnitId = seeded.SeatId,
            });
            db.HostedEventBookings.Add(booking);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var all = await db.HostedEventBookings.Include(b => b.Nights).ToListAsync();
            var seat = await db.HostedEventLayoutUnits.FirstAsync(u => u.Id == seeded.SeatId);

            var taken = EventCapacity.PeopleIn(all, seeded.NightIds[0], seeded.SeatId);
            Assert.Equal(1, taken);

            var refusal = EventCapacity.WhyThisRoomCannotTakeThem(
                EventCapacity.NameOf(seat), Friday, EventCapacity.CapacityOf(seat), taken,
                partySize: 1, HostedEventLayoutKind.Seats);
            Assert.Contains("H9", refusal);
            Assert.Contains("is taken", refusal);
        }
    }

    [Fact]
    public async Task A_seats_plan_and_a_rooms_plan_never_share_an_event()
    {
        // "One per event." The kind is a column rather than something inferred from which fields
        // happen to be filled in, so a plan cannot be half of each.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, HostedEventLayoutKind.Seats);

        await using var db = await sqlite.NewContextAsync();
        var ev = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
        var units = await db.HostedEventLayoutUnits
            .Where(u => u.HostedEventId == EventId).ToListAsync();

        Assert.Equal(HostedEventLayoutKind.Seats, ev.LayoutKind);
        Assert.All(units, u => Assert.Null(u.PlaceRoomId));
        Assert.All(units, u => Assert.NotNull(u.Label));
    }

    // ── the price nobody collects ────────────────────────────────────────────

    [Fact]
    public async Task A_unit_can_carry_a_price_that_is_shown_and_never_charged()
    {
        // Ben, 2026-09-12: "We can tell them the price, but we do not collect money."
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var unit = await db.HostedEventLayoutUnits.FirstAsync(u => u.Id == seeded.BlueUnitId);
            unit.Price = 145.50m;
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var unit = await db.HostedEventLayoutUnits.FirstAsync(u => u.Id == seeded.BlueUnitId);
            Assert.Equal(145.50m, unit.Price);

            // Nothing about a price is a payment: no ledger row, no charge, no receipt exists
            // anywhere for it, which is what makes displaying it safe.
            Assert.Empty(await db.BillingLedgerEntries.ToListAsync());
        }
    }

    [Fact]
    public async Task A_price_nobody_stated_is_not_free()
    {
        // Null is "ask the venue" and zero is genuinely included. Printing a zero against a room
        // whose venue never filled the box in would be quoting a price nobody offered.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var unit = await db.HostedEventLayoutUnits.FirstAsync(u => u.Id == seeded.BlueUnitId);

        Assert.Null(unit.Price);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private sealed record Seeded(IReadOnlyList<Guid> NightIds, Guid BlueUnitId, Guid SeatId);

    private static HostedEventBooking Booking(
        HostedEventBookingStatus status, int partySize,
        HostedEventBookingKind kind = HostedEventBookingKind.Overnight)
        => new()
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId,
            PartySize = partySize, Kind = kind, Status = status,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
        };

    /// <summary>A three-night event, so "two of the three" is a thing the tests can say.</summary>
    private static async Task<Seeded> SeedAsync(
        SqliteTestDb sqlite, HostedEventLayoutKind kind = HostedEventLayoutKind.Rooms)
    {
        await using var db = await sqlite.NewContextAsync();

        foreach (var (id, name) in new[] { (HostId, "The Host"), (GuestId, "A Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.com", UserName = $"{id:N}@example.com",
                DisplayName = name, DateCreated = DateTime.UtcNow,
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        var blue = new PlaceRoom
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Blue Room", Capacity = 2, IsBookable = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.PlaceRooms.Add(blue);

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = Friday, EndsOn = Friday.AddDays(2), IsPublished = true,
            DayPassCapacity = 10, LayoutKind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        var nightIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var night = new HostedEventNight
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, Date = Friday.AddDays(i),
                SortOrder = i, DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            };
            db.HostedEventNights.Add(night);
            nightIds.Add(night.Id);
        }

        var blueUnit = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId,
            PlaceRoomId = kind == HostedEventLayoutKind.Rooms ? blue.Id : null,
            Label = kind == HostedEventLayoutKind.Rooms ? null : "H8",
            Capacity = kind == HostedEventLayoutKind.Rooms ? null : 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var seat = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Label = "H9", Capacity = 1, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventLayoutUnits.Add(blueUnit);
        if (kind == HostedEventLayoutKind.Seats) db.HostedEventLayoutUnits.Add(seat);

        await db.SaveChangesAsync();
        return new Seeded(nightIds, blueUnit.Id, seat.Id);
    }
}
