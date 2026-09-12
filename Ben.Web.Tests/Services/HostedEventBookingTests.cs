using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A booking against a real relational database: what confirming holds, what cancelling frees,
/// and the umbrella attendee row that makes the rest of the site notice (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Why a real database.</b> The rules here are about rows relating to rows — a party in a
/// room on a night, counted against everybody else in that room on that night — and the foreign
/// keys are part of what is being tested. A booking night pointing at a night that was deleted is
/// a bug the database itself refuses, and that refusal is worth having in a test.</para>
///
/// <para><b>The property under test throughout is that only a confirmed booking holds anything.</b>
/// A request that held a bed would let a queue of hopefuls fill a weekend the venue could then
/// never work through, and a cancellation that kept its umbrella row would leave a party counted
/// as coming for ever.</para>
/// </remarks>
public sealed class HostedEventBookingTests
{
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid OtherGuestId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private sealed record Seeded(Guid FridayId, Guid SaturdayId, Guid BlueRoomId, Guid SuiteId);

    /// <summary>
    /// A two-night event at a venue with a double and a suite. Parent rows are real because the
    /// database insists, which is the point of using it.
    /// </summary>
    private static async Task<Seeded> SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        foreach (var (id, name) in new[]
                 { (HostId, "The Host"), (GuestId, "A Guest"), (OtherGuestId, "Another Guest") })
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
            Name = "Blue Room", Capacity = 2, IsBookable = true, BedNote = "one double",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var suite = new PlaceRoom
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "The Suite", Capacity = 4, IsBookable = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.PlaceRooms.AddRange(blue, suite);

        var hosted = new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            DayPassCapacity = 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEvents.Add(hosted);

        var friday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = new DateTime(2026, 10, 30),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var saturday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = new DateTime(2026, 10, 31),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventNights.AddRange(friday, saturday);

        db.HostedEventRooms.AddRange(
            new HostedEventRoom
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, PlaceRoomId = blue.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            },
            new HostedEventRoom
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, PlaceRoomId = suite.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });

        await db.SaveChangesAsync();
        return new Seeded(friday.Id, saturday.Id, blue.Id, suite.Id);
    }

    private static HostedEventBooking NewBooking(
        Guid leadId, int partySize, HostedEventBookingStatus status,
        HostedEventBookingKind kind = HostedEventBookingKind.Overnight,
        params (Guid nightId, Guid roomId)[] nights)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = leadId,
            PartySize = partySize, Kind = kind, Status = status,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = leadId,
        };
        foreach (var (nightId, roomId) in nights)
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                HostedEventNightId = nightId, PlaceRoomId = roomId,
                DateCreated = DateTime.UtcNow,
            });
        }
        return booking;
    }

    private static Task<List<HostedEventBooking>> AllAsync(BenDataContext db)
        => db.HostedEventBookings.Include(b => b.Nights)
             .Where(b => b.HostedEventId == EventId).ToListAsync();

    // ── what holds a bed ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_requested_booking_holds_no_bed_so_a_queue_cannot_fill_the_house()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            // Three separate parties all asking for the same double.
            db.HostedEventBookings.AddRange(
                NewBooking(GuestId, 2, HostedEventBookingStatus.Requested,
                           nights: (seeded.FridayId, seeded.BlueRoomId)),
                NewBooking(OtherGuestId, 2, HostedEventBookingStatus.Requested,
                           nights: (seeded.FridayId, seeded.BlueRoomId)),
                NewBooking(HostId, 2, HostedEventBookingStatus.Requested,
                           nights: (seeded.FridayId, seeded.BlueRoomId)));
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var all = await AllAsync(db);
            // Six people have asked; not one of them is in the room.
            Assert.Equal(0, EventCapacity.PeopleIn(all, seeded.FridayId, seeded.BlueRoomId));
            // And the venue can still confirm any one of them.
            Assert.Null(EventCapacity.WhyThisRoomCannotTakeThem(
                "Blue Room", new DateTime(2026, 10, 30), capacity: 2,
                taken: EventCapacity.PeopleIn(all, seeded.FridayId, seeded.BlueRoomId),
                partySize: 2));
        }
    }

    [Fact]
    public async Task A_confirmed_party_fills_the_room_for_that_night_only()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventBookings.Add(
                NewBooking(GuestId, 2, HostedEventBookingStatus.Confirmed,
                           nights: (seeded.FridayId, seeded.BlueRoomId)));
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var all = await AllAsync(db);
            Assert.Equal(2, EventCapacity.PeopleIn(all, seeded.FridayId, seeded.BlueRoomId));
            Assert.Equal(0, EventCapacity.PeopleIn(all, seeded.SaturdayId, seeded.BlueRoomId));
            Assert.Equal(0, EventCapacity.PeopleIn(all, seeded.FridayId, seeded.SuiteId));

            // And the room now refuses one more person, naming the date.
            var why = EventCapacity.WhyThisRoomCannotTakeThem(
                "Blue Room", new DateTime(2026, 10, 30), capacity: 2, taken: 2, partySize: 1);
            Assert.Contains("full", why);
            Assert.Contains("10/30/2026", why);
        }
    }

    [Fact]
    public async Task A_party_of_three_does_not_fit_the_double_but_does_fit_the_suite()
    {
        // The walk this phase is verified by, in one test: the refusal has to name a number the
        // host can act on, and the suite has to take them.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var rooms = await db.HostedEventRooms.Include(r => r.PlaceRoom)
            .Where(r => r.HostedEventId == EventId).ToListAsync();

        var blue = rooms.First(r => r.PlaceRoomId == seeded.BlueRoomId);
        var suite = rooms.First(r => r.PlaceRoomId == seeded.SuiteId);

        var refusal = EventCapacity.WhyThisRoomCannotTakeThem(
            blue.PlaceRoom.Name, new DateTime(2026, 10, 30),
            EventCapacity.CapacityOf(blue), taken: 0, partySize: 3);
        Assert.Contains("Blue Room", refusal);
        Assert.Contains("sleeps 2 more", refusal);

        Assert.Null(EventCapacity.WhyThisRoomCannotTakeThem(
            suite.PlaceRoom.Name, new DateTime(2026, 10, 30),
            EventCapacity.CapacityOf(suite), taken: 0, partySize: 3));
    }

    // ── the umbrella row every existing count reads ──────────────────────────

    [Fact]
    public async Task Confirming_writes_an_umbrella_attendee_carrying_the_whole_party()
    {
        // This is what makes the public list, the reminder job, the calendar file and the phone
        // already in people's pockets all count the party — none of which knows what a booking is.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        Guid umbrellaId, attendeeId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var umbrella = new OrgCalendarEvent
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, HostedEventId = EventId,
                Title = "Halloween Lock-In",
                StartDateTime = new DateTime(2026, 10, 30, 19, 0, 0),
                EndDateTime = new DateTime(2026, 10, 31, 23, 0, 0),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            };
            db.OrgCalendarEvents.Add(umbrella);

            var attendee = new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = umbrella.Id, AppUserId = GuestId,
                RsvpStatus = RsvpStatus.Accepted, SeatStatus = TourSeatStatus.Reserved,
                Seats = 3, DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            };
            db.OrgCalendarEventAttendees.Add(attendee);

            var booking = NewBooking(GuestId, 3, HostedEventBookingStatus.Confirmed,
                                     nights: (seeded.FridayId, seeded.SuiteId));
            booking.UmbrellaAttendeeId = attendee.Id;
            db.HostedEventBookings.Add(booking);

            await db.SaveChangesAsync();
            umbrellaId = umbrella.Id;
            attendeeId = attendee.Id;
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var attendee = await db.OrgCalendarEventAttendees.FirstAsync(a => a.Id == attendeeId);

            // The pair every existing count already understands, and the party size in Seats.
            Assert.Equal(RsvpStatus.Accepted, attendee.RsvpStatus);
            Assert.Equal(TourSeatStatus.Reserved, attendee.SeatStatus);
            Assert.Equal(3, attendee.Seats);

            // Which is exactly what TourSeats counts, without knowing what a hosted event is.
            var attendees = await db.OrgCalendarEventAttendees
                .Where(a => a.OrgCalendarEventId == umbrellaId).ToListAsync();
            Assert.Equal(3, Ben.Data.WebApi.Services.Tours.TourSeats.PlacesTaken(attendees));
        }
    }

    // ── day passes are a separate count ──────────────────────────────────────

    [Fact]
    public async Task Day_passes_and_beds_are_counted_apart()
    {
        // One number for both would let a venue that sleeps six sell forty.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventBookings.AddRange(
                NewBooking(GuestId, 2, HostedEventBookingStatus.Confirmed,
                           nights: (seeded.FridayId, seeded.BlueRoomId)),
                NewBooking(OtherGuestId, 4, HostedEventBookingStatus.Confirmed,
                           HostedEventBookingKind.DayPass));
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var all = await AllAsync(db);
            Assert.Equal(2, EventCapacity.PeopleIn(all, seeded.FridayId, seeded.BlueRoomId));
            Assert.Equal(4, EventCapacity.DayPassesTaken(all));

            var ev = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
            Assert.Null(EventCapacity.WhyTheseDayPassesCannotBeGiven(
                ev.DayPassCapacity, EventCapacity.DayPassesTaken(all), partySize: 6));
            Assert.NotNull(EventCapacity.WhyTheseDayPassesCannotBeGiven(
                ev.DayPassCapacity, EventCapacity.DayPassesTaken(all), partySize: 7));
        }
    }

    // ── the guests, and what the kitchen reads ───────────────────────────────

    [Fact]
    public async Task A_guest_needs_no_account_and_keeps_their_own_dietary_note()
    {
        // Somebody brings a partner who will never hear of this site. Requiring an account to eat
        // dinner would make the kitchen's list wrong rather than making anybody sign up.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        Guid bookingId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = NewBooking(GuestId, 2, HostedEventBookingStatus.Confirmed,
                                     nights: (seeded.FridayId, seeded.BlueRoomId));
            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                DisplayName = "A Guest", AppUserId = GuestId, SortOrder = 0,
                DateCreated = DateTime.UtcNow,
            });
            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                DisplayName = "Their partner", AppUserId = null,
                DietaryNotes = "Coeliac", SortOrder = 1, DateCreated = DateTime.UtcNow,
            });
            db.HostedEventBookings.Add(booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var guests = await db.HostedEventBookingGuests
                .Where(g => g.HostedEventBookingId == bookingId)
                .OrderBy(g => g.SortOrder).ToListAsync();

            Assert.Equal(2, guests.Count);
            Assert.Null(guests[1].AppUserId);
            Assert.Equal("Coeliac", guests[1].DietaryNotes);
        }
    }

    // ── what the database itself refuses ─────────────────────────────────────

    [Fact]
    public async Task A_party_cannot_hold_two_rooms_on_one_night()
    {
        // The unique index is what stops an edit quietly creating one, and a party in two rooms is
        // a party counted twice against the house.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var booking = NewBooking(GuestId, 2, HostedEventBookingStatus.Confirmed,
                                 nights: [(seeded.FridayId, seeded.BlueRoomId),
                                          (seeded.FridayId, seeded.SuiteId)]);
        db.HostedEventBookings.Add(booking);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_the_event_takes_its_bookings_with_it()
    {
        // The bookings ARE the event, not records of their own. An orphan booking would appear on
        // a guest's "my events" for an event that no longer exists.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = NewBooking(GuestId, 2, HostedEventBookingStatus.Confirmed,
                                     nights: (seeded.FridayId, seeded.BlueRoomId));
            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                DisplayName = "A Guest", DateCreated = DateTime.UtcNow,
            });
            db.HostedEventBookings.Add(booking);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            // The nights have to go first: SQL Server refuses two cascade paths into one table, so
            // the booking-night rows hang off the booking alone and the event's own nights do not
            // cascade into them.
            db.HostedEventBookingNights.RemoveRange(
                db.HostedEventBookingNights.Where(
                    n => n.HostedEventBooking.HostedEventId == EventId));
            await db.SaveChangesAsync();

            db.HostedEvents.RemoveRange(db.HostedEvents.Where(e => e.Id == EventId));
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            Assert.Empty(await db.HostedEventBookings.ToListAsync());
            Assert.Empty(await db.HostedEventBookingGuests.ToListAsync());
            // The venue's own description of its building is untouched — a PlaceRoom outlives
            // any event that borrowed it.
            Assert.Equal(2, await db.PlaceRooms.CountAsync());
        }
    }
}
