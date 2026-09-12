using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The email door into a hosted event: what it refuses, and what a confirmed link lands as
/// (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>The property under test is that clicking a link never holds anything.</b> A day-pass
/// request lands in the venue's queue and the venue decides, exactly as it does for somebody with
/// an account. A link that reserved a place would let a stranger with an email address fill a
/// weekend the host had never looked at.</para>
///
/// <para>The refusals are tested for their words as well as their existence, because each one has
/// to tell somebody what to do instead. "No" on its own sends them hunting for a phone number the
/// page may not carry.</para>
/// </remarks>
public sealed class HostedEventGuestDoorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static HostedEvent Live() => new()
    {
        Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
        Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
        StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
        IsPublished = true, DayPassCapacity = 10,
        DateCreated = Now, CreatedByAppUserId = HostId,
    };

    // ── what the door refuses ────────────────────────────────────────────────

    [Fact]
    public void A_live_event_selling_day_passes_is_open()
        => Assert.Null(HostedEventGuestDoor.WhyTheEmailDoorIsClosed(Live(), Now));

    [Fact]
    public void A_draft_is_not_a_thing_anybody_can_be_invited_to()
    {
        var draft = Live();
        draft.IsPublished = false;

        Assert.Equal("This event isn't taking bookings.",
            HostedEventGuestDoor.WhyTheEmailDoorIsClosed(draft, Now));
    }

    [Fact]
    public void An_event_that_was_called_off_says_so_rather_than_that_it_is_full()
    {
        var off = Live();
        off.CancelledAtUtc = Now;

        Assert.Equal("This event has been called off.",
            HostedEventGuestDoor.WhyTheEmailDoorIsClosed(off, Now));
    }

    [Fact]
    public void An_event_selling_no_day_passes_names_the_page_where_a_room_can_be_asked_for()
    {
        // The email door only ever sells a day pass: a stranger cannot be asked to pick the Blue
        // Room in a hyperlink. Taking their address and doing nothing with it would be worse.
        var noPasses = Live();
        noPasses.DayPassCapacity = 0;

        var refusal = HostedEventGuestDoor.WhyTheEmailDoorIsClosed(noPasses, Now);

        Assert.Contains("doesn't sell day passes", refusal);
        Assert.Contains("ask the venue for a room", refusal);
    }

    [Fact]
    public void A_capacity_of_none_stated_is_not_the_same_as_a_capacity_of_zero()
    {
        // Null is "the venue has not limited them"; zero is "we do not sell them". Reading the
        // first as the second would close the door on every event that never filled the box in.
        var unstated = Live();
        unstated.DayPassCapacity = null;

        Assert.Null(HostedEventGuestDoor.WhyTheEmailDoorIsClosed(unstated, Now));
    }

    // ── the deadline, and whose link it is ───────────────────────────────────

    [Fact]
    public void Past_the_deadline_a_stranger_is_turned_away()
    {
        var closed = Live();
        closed.BookingsCloseAtUtc = Now.AddDays(-1);

        Assert.Equal("This event has stopped taking bookings.",
            HostedEventGuestDoor.WhyTheEmailDoorIsClosed(closed, Now));
    }

    [Fact]
    public void The_hosts_own_link_still_works_past_the_deadline()
    {
        // A host who wrote to somebody after bookings closed made that call themselves, and
        // refusing their own link would be the site overruling the venue about its own weekend.
        var closed = Live();
        closed.BookingsCloseAtUtc = Now.AddDays(-1);

        Assert.Null(HostedEventGuestDoor.WhyTheEmailDoorIsClosed(
            closed, Now, theHostSentThisLink: true));
    }

    [Fact]
    public void The_hosts_own_link_does_not_reopen_an_event_that_was_called_off()
    {
        // The latitude is over the deadline and nothing else.
        var off = Live();
        off.CancelledAtUtc = Now;
        off.BookingsCloseAtUtc = Now.AddDays(-1);

        Assert.Equal("This event has been called off.",
            HostedEventGuestDoor.WhyTheEmailDoorIsClosed(off, Now, theHostSentThisLink: true));
    }

    // ── what the click lands as ──────────────────────────────────────────────

    [Fact]
    public async Task Confirming_a_link_asks_for_a_day_pass_and_holds_nothing()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
            await HostedEventGuestDoor.AddDayPassRequestAsync(
                db, hosted, GuestId, partySize: 3, note: null, default);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.SingleAsync(b => b.HostedEventId == EventId);

            Assert.Equal(HostedEventBookingKind.DayPass, booking.Kind);
            Assert.Equal(HostedEventBookingStatus.Requested, booking.Status);
            Assert.Equal(3, booking.PartySize);
            Assert.Null(booking.UmbrellaAttendeeId);

            // And nothing is held: the day-pass count is untouched until the venue confirms.
            var all = await db.HostedEventBookings.Include(b => b.Nights).ToListAsync();
            Assert.Equal(0, EventCapacity.DayPassesTaken(all));
        }
    }

    [Fact]
    public async Task A_second_link_for_the_same_person_is_the_same_party_not_another_one()
    {
        // Somebody sent two links, or who asked and was then invited by the host, is one party
        // either way — a second row would have the venue decide twice on the same people.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
            await HostedEventGuestDoor.AddDayPassRequestAsync(
                db, hosted, GuestId, partySize: 2, note: null, default);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
            var second = await HostedEventGuestDoor.AddDayPassRequestAsync(
                db, hosted, GuestId, partySize: 9, note: null, default);
            await db.SaveChangesAsync();

            Assert.Null(second);
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.SingleAsync(b => b.HostedEventId == EventId);
            // The first ask stands. The venue is deciding on one party of two, not two parties.
            Assert.Equal(2, booking.PartySize);
        }
    }

    [Fact]
    public async Task Somebody_who_cancelled_in_the_spring_can_come_in_the_autumn()
    {
        // "One booking per person per event" means one LIVE booking, or a change of plan in March
        // would bar somebody from the same weekend for ever.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId,
                PartySize = 2, Kind = HostedEventBookingKind.DayPass,
                Status = HostedEventBookingStatus.Cancelled,
                DateCreated = Now, CreatedByAppUserId = GuestId,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
            var again = await HostedEventGuestDoor.AddDayPassRequestAsync(
                db, hosted, GuestId, partySize: 2, note: null, default);
            await db.SaveChangesAsync();

            Assert.NotNull(again);
        }
    }

    [Fact]
    public async Task A_party_size_nobody_sent_is_one_person_rather_than_none()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var hosted = await db.HostedEvents.FirstAsync(e => e.Id == EventId);
        var booking = await HostedEventGuestDoor.AddDayPassRequestAsync(
            db, hosted, GuestId, partySize: null, note: null, default);

        Assert.Equal(1, booking!.PartySize);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static async Task SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        foreach (var (id, name) in new[] { (HostId, "The Host"), (GuestId, "A Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.com", UserName = $"{id:N}@example.com",
                DisplayName = name, DateCreated = Now,
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = Now, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = Now, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(Live());

        await db.SaveChangesAsync();
    }
}
