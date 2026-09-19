using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The plan a visitor sees, and what it refuses to say (item 235 phase 6).
/// </summary>
/// <remarks>
/// <para><b>Two claims carry the weight here, and both are about what is NOT in the answer.</b>
/// The first is that a square says only what it is — a stranger reading a seating plan must not be
/// able to learn who is in row C, and the record has no field for it, so what is tested is that
/// none of the private machinery leaks through the shape. The second is that free squares are
/// absent altogether, which is the difference between a few hundred bytes and twelve hundred rows
/// of "nothing here" on a four-hundred-seat house across three nights.</para>
///
/// <para><b>A real relational database</b>, because <c>IsHolding</c> and the released-night flag
/// are the same columns the arbiter index filters on, and a test double that agreed with the C#
/// but not with the index would be testing the wrong half.</para>
/// </remarks>
public sealed class PublicHostedEventPlanTests
{
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid OtherGuestId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private sealed record Seeded(Guid FridayId, Guid SaturdayId, Guid BlueUnitId, Guid SuiteUnitId);

    private static PublicHostedEventController Build(
        IDbContextFactory<BenDataContext> factory, Guid? reader = null)
        => new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = reader is { } id
                        ? new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
                        : new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };

    private static async Task<PublicHostedEventPlanRecord> PlanAsync(
        SqliteTestDb sqlite, Guid? reader = null)
    {
        var answer = await Build(sqlite.Factory, reader).GetPlan(EventId, default);
        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        return Assert.IsType<PublicHostedEventPlanRecord>(ok.Value);
    }

    /// <summary>A published two-night event offering two rooms.</summary>
    private static async Task<Seeded> SeedAsync(
        SqliteTestDb sqlite,
        HostedEventLifecycleState state = HostedEventLifecycleState.Published,
        HostedEventBookingMode mode = HostedEventBookingMode.Pick,
        DateTime? bookingsCloseAtUtc = null)
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
            Name = "Blue Room", Capacity = 2, IsBookable = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var suite = new PlaceRoom
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "The Suite", Capacity = 4, IsBookable = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.PlaceRooms.AddRange(blue, suite);

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            LifecycleState = state, BookingMode = mode, HoldMinutes = 2880,
            BookingsCloseAtUtc = bookingsCloseAtUtc,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        var friday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = new DateTime(2026, 10, 30),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var saturday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = new DateTime(2026, 10, 31),
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventNights.AddRange(friday, saturday);

        var blueUnit = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, PlaceRoomId = blue.Id, Price = 180m,
            LayoutRow = 0, LayoutColumn = 0,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var suiteUnit = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, PlaceRoomId = suite.Id, SortOrder = 1,
            LayoutRow = 0, LayoutColumn = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventLayoutUnits.AddRange(blueUnit, suiteUnit);

        await db.SaveChangesAsync();
        return new Seeded(friday.Id, saturday.Id, blueUnit.Id, suiteUnit.Id);
    }

    /// <summary>Puts a party in a room on a night, in whatever state the test needs.</summary>
    private static async Task HoldAsync(
        SqliteTestDb sqlite, Guid leadId, HostedEventBookingStatus status,
        Guid nightId, Guid unitId)
    {
        await using var db = await sqlite.NewContextAsync();

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = leadId,
            PartySize = 2, Kind = HostedEventBookingKind.Overnight,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = leadId,
        };
        booking.Nights.Add(new HostedEventBookingNight
        {
            Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
            HostedEventNightId = nightId, HostedEventLayoutUnitId = unitId,
            DateCreated = DateTime.UtcNow,
        });

        db.HostedEventBookings.Add(booking);

        // Through the single writer, so IsHolding and the status can never disagree here in a way
        // they could not in the running site.
        if (status == HostedEventBookingStatus.Confirmed)
            BookingTransitions.Confirm(booking, HostId, null, DateTime.UtcNow);
        else if (status == HostedEventBookingStatus.Held)
            BookingTransitions.Hold(
                booking,
                await db.HostedEvents.FirstAsync(e => e.Id == EventId),
                DateTime.UtcNow);
        else
            BookingTransitions.Request(booking, DateTime.UtcNow);

        await db.SaveChangesAsync();
    }

    // ── what a square says ───────────────────────────────────────────────────

    [Fact]
    public async Task A_square_nobody_has_is_not_in_the_answer_at_all()
    {
        // The size claim. Two rooms across two nights, none of them taken: four squares, and the
        // answer carries none of them. On a four-hundred-seat house this is the whole difference.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        var plan = await PlanAsync(sqlite);

        Assert.Equal(2, plan.Units.Count);
        Assert.Equal(2, plan.Nights.Count);
        Assert.Empty(plan.Cells);
    }

    [Fact]
    public async Task A_held_square_is_pending_and_a_confirmed_one_is_taken()
    {
        // Two different facts to somebody choosing: one is being decided and one is settled.
        // Drawing them the same way tells a guest a seat is gone when it may be free by teatime.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await HoldAsync(sqlite, OtherGuestId, HostedEventBookingStatus.Held,
                        seeded.FridayId, seeded.BlueUnitId);
        await HoldAsync(sqlite, GuestId, HostedEventBookingStatus.Confirmed,
                        seeded.SaturdayId, seeded.SuiteUnitId);

        var plan = await PlanAsync(sqlite);

        Assert.Equal(HostedEventPlanCellState.Pending,
            plan.Cells.Single(c => c.HostedEventNightId == seeded.FridayId).State);
        Assert.Equal(HostedEventPlanCellState.Taken,
            plan.Cells.Single(c => c.HostedEventNightId == seeded.SaturdayId).State);
    }

    [Fact]
    public async Task A_request_holds_nothing_so_the_square_stays_free()
    {
        // The rule an Ask event runs on: many parties may name one room, and the room is still
        // there for whoever the venue gives it to.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await HoldAsync(sqlite, OtherGuestId, HostedEventBookingStatus.Requested,
                        seeded.FridayId, seeded.BlueUnitId);

        Assert.Empty((await PlanAsync(sqlite)).Cells);
    }

    [Fact]
    public async Task My_own_square_is_mine_and_not_somebody_elses_hold()
    {
        // The one thing a guest must never be told about their own choice. A page that drew it as
        // "held by somebody else" would have them picking again over the top of themselves.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await HoldAsync(sqlite, GuestId, HostedEventBookingStatus.Held,
                        seeded.FridayId, seeded.BlueUnitId);

        Assert.Equal(HostedEventPlanCellState.Mine,
            (await PlanAsync(sqlite, reader: GuestId)).Cells.Single().State);
        Assert.Equal(HostedEventPlanCellState.Pending,
            (await PlanAsync(sqlite, reader: OtherGuestId)).Cells.Single().State);
    }

    [Fact]
    public async Task A_room_the_venue_keeps_back_reads_as_the_venue_using_it()
    {
        // The two kinds are different sentences to a guest: "out of use" and "the venue is in it".
        // Neither is the venue's own note, which never leaves the board.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock
            {
                Id = Guid.NewGuid(), HostedEventLayoutUnitId = seeded.SuiteUnitId,
                HostedEventNightId = seeded.FridayId, Kind = HostedEventBlockKind.HouseHeld,
                Note = "The owner's family are in it.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            db.HostedEventUnitBlocks.Add(new HostedEventUnitBlock
            {
                Id = Guid.NewGuid(), HostedEventLayoutUnitId = seeded.BlueUnitId,
                Kind = HostedEventBlockKind.Blocked,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            await db.SaveChangesAsync();
        }

        var plan = await PlanAsync(sqlite);

        Assert.Equal(HostedEventPlanCellState.Blocked,
            plan.Cells.Single(c => c.HostedEventLayoutUnitId == seeded.SuiteUnitId
                                && c.HostedEventNightId == seeded.FridayId).State);

        // A block with no night is every night of the run.
        Assert.Equal(2, plan.Cells.Count(c => c.HostedEventLayoutUnitId == seeded.BlueUnitId
                                           && c.State == HostedEventPlanCellState.NotOffered));
    }

    // ── whether anything can be chosen ───────────────────────────────────────

    [Fact]
    public async Task An_event_that_asks_rather_than_picks_draws_the_plan_and_answers_nothing()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, mode: HostedEventBookingMode.Ask);

        var plan = await PlanAsync(sqlite);

        Assert.False(plan.IsPicking);
        // And no sentence, because nothing is wrong: this event simply takes its bookings in words.
        Assert.Null(plan.ClosedSentence);
    }

    [Fact]
    public async Task A_closed_or_called_off_event_says_which_it_is()
    {
        // A greyed-out grid with no sentence beside it is the commonest way a page wastes
        // somebody's afternoon. These are three different facts and only one is worth waiting for.
        await using (var closed = await SqliteTestDb.CreateAsync())
        {
            await SeedAsync(closed, bookingsCloseAtUtc: DateTime.UtcNow.AddDays(-1));
            var plan = await PlanAsync(closed);
            Assert.False(plan.IsPicking);
            Assert.Contains("closed", plan.ClosedSentence);
        }

        await using (var off = await SqliteTestDb.CreateAsync())
        {
            await SeedAsync(off, state: HostedEventLifecycleState.Cancelled);
            // Called off is not on the public site at all, so there is no plan to draw.
            var answer = await Build(off.Factory).GetPlan(EventId, default);
            Assert.IsType<NotFoundResult>(answer.Result);
        }

        await using (var over = await SqliteTestDb.CreateAsync())
        {
            await SeedAsync(over, state: HostedEventLifecycleState.Ended);
            var plan = await PlanAsync(over);
            Assert.False(plan.IsPicking);
            Assert.Contains("happened", plan.ClosedSentence);
        }
    }

    [Fact]
    public async Task A_draft_has_no_plan_to_read()
    {
        // Not an empty plan: a draft has no page at all, because answering anything would leak
        // that something is being planned and when.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, state: HostedEventLifecycleState.Draft);

        Assert.IsType<NotFoundResult>((await Build(sqlite.Factory).GetPlan(EventId, default)).Result);
    }

    // ── what a unit says ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_unit_carries_its_price_and_what_it_holds_and_not_the_venues_note()
    {
        // Price is shown and never charged; how many it holds is what lets the picker count a
        // party. The venue's own note about a square is not on this record at all — the shape is
        // the guarantee, so this asserts the shape rather than a value.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        var plan = await PlanAsync(sqlite);
        var blue = plan.Units.Single(u => u.Id == seeded.BlueUnitId);

        Assert.Equal("Blue Room", blue.Name);
        Assert.Equal(180m, blue.Price);
        Assert.Equal(2, blue.Holds);
        Assert.Equal(2880, plan.HoldMinutes);

        Assert.Null(typeof(PublicHostedEventPlanUnitRecord).GetProperty("Note"));
        Assert.Null(typeof(PublicHostedEventPlanCellRecord).GetProperty("BookingId"));
        Assert.Null(typeof(PublicHostedEventPlanCellRecord).GetProperty("LeadName"));
    }
}
