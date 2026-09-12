using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The guest's own door to a hosted event: what a pass request answers once a pass has been
/// withdrawn, and what withdrawing means in each state (item 235 phase 1).
/// </summary>
/// <remarks>
/// <para><b>Two defects the planners found, pinned here so they stay fixed.</b> A guest whose only
/// pass had been revoked was told "the venue hasn't issued your pass yet" — a sentence about
/// waiting, said to somebody whose booking had just been turned down. And a guest withdrawing a
/// booking the venue had already turned down had a cancellation request recorded against it, so
/// the host saw "asked to cancel" on a party that was never coming.</para>
///
/// <para>Driven through the controller rather than the tables, because both defects were in the
/// controller's own branching and a test of the rows underneath would have passed throughout.</para>
/// </remarks>
public sealed class PublicHostedEventBookingRulesTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();

    // ── the pass, after it was withdrawn ─────────────────────────────────────

    [Fact]
    public async Task A_withdrawn_pass_is_still_shown_with_the_reason_on_it()
    {
        // A guest must be able to show a withdrawn pass to somebody who can tell them why.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed);
        var passId = await IssuePassAsync(sqlite, bookingId);
        await RevokeAsync(sqlite, passId, "The booking changed. Ask them for the newer pass.");

        var result = await Guest(sqlite).GetMyPass(EventId, default);

        var shown = Assert.IsType<MyHostedEventPassRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(passId, shown.Pass.Id);
        Assert.NotNull(shown.Pass.RevokedUtc);
        Assert.Equal("The booking changed. Ask them for the newer pass.", shown.Pass.RevokedReason);
    }

    [Fact]
    public async Task A_live_replacement_is_the_pass_and_the_withdrawn_one_is_history()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed);
        var oldId = await IssuePassAsync(sqlite, bookingId);

        Guid newId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            newId = (await EventPasses.ReissueAsync(db, booking, HostId, "Lost the letter.", default)).Id;
            await db.SaveChangesAsync();
        }

        var result = await Guest(sqlite).GetMyPass(EventId, default);

        var shown = Assert.IsType<MyHostedEventPassRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(newId, shown.Pass.Id);
        Assert.NotEqual(oldId, shown.Pass.Id);
        Assert.Null(shown.Pass.RevokedUtc);
        Assert.True(shown.Pass.ReplacedAnEarlierOne);
    }

    [Fact]
    public async Task A_turned_down_booking_that_once_had_a_pass_still_shows_it()
    {
        // The case that produced the wrong sentence: confirmed, given a pass, then turned down.
        // The old answer was "issued when the venue confirms your place", to somebody the venue
        // had just refused.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed);
        var passId = await IssuePassAsync(sqlite, bookingId);
        await RevokeAsync(sqlite, passId, "The venue could not take this booking.");
        await SetStatusAsync(sqlite, bookingId, HostedEventBookingStatus.TurnedDown);

        var result = await Guest(sqlite).GetMyPass(EventId, default);

        var shown = Assert.IsType<MyHostedEventPassRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("The venue could not take this booking.", shown.Pass.RevokedReason);
    }

    [Fact]
    public async Task A_confirmed_booking_never_given_a_pass_is_told_to_ask_for_one()
    {
        // The one case the "not issued yet" sentence is true of.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        await BookAsync(sqlite, HostedEventBookingStatus.Confirmed);

        var result = await Guest(sqlite).GetMyPass(EventId, default);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains("hasn't issued your pass yet", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task A_booking_still_waiting_is_told_what_it_is_waiting_for()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        await BookAsync(sqlite, HostedEventBookingStatus.Requested);

        var result = await Guest(sqlite).GetMyPass(EventId, default);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains("when the venue confirms your place", Assert.IsType<string>(refused.Value));
    }

    // ── withdrawing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdrawing_a_turned_down_booking_is_refused_and_records_nothing()
    {
        // There is nothing to withdraw, and the old code recorded a cancellation request the host
        // would have read as "asked to cancel" on a party that was never coming.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.TurnedDown);

        var result = await Guest(sqlite).Withdraw(EventId, "Changed my mind.", default);

        var refusal = Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(result.Result).Value);
        Assert.Contains("already turned down", refusal);

        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
        Assert.Null(booking.CancellationRequestedUtc);
        Assert.Null(booking.CancellationReason);
        Assert.Equal(HostedEventBookingStatus.TurnedDown, booking.Status);
    }

    [Fact]
    public async Task Withdrawing_a_request_simply_removes_it()
    {
        // Unchanged, and pinned so the turned-down branch above never grows to swallow it.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        await BookAsync(sqlite, HostedEventBookingStatus.Requested);

        var result = await Guest(sqlite).Withdraw(EventId, null, default);

        Assert.IsType<NoContentResult>(result.Result);
        await using var db = await sqlite.NewContextAsync();
        Assert.Empty(await db.HostedEventBookings.ToListAsync());
    }

    [Fact]
    public async Task Withdrawing_a_confirmed_booking_asks_the_venue_rather_than_acting()
    {
        // The venue catered against it; the host releases it from their own screen.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed);

        var result = await Guest(sqlite).Withdraw(EventId, "Can't make it after all.", default);

        var mine = Assert.IsType<MyHostedEventBookingRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(mine.CancellationRequestedUtc);
        Assert.Equal(HostedEventBookingStatus.Confirmed, mine.Status);

        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
        Assert.Equal("Can't make it after all.", booking.CancellationReason);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>The guest's controller, signed in as the guest.</summary>
    private static PublicHostedEventBookingController Guest(SqliteTestDb sqlite)
        => new(sqlite.Factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, GuestId.ToString())], "Bearer")),
                },
            },
        };

    private static async Task<Guid> IssuePassAsync(SqliteTestDb sqlite, Guid bookingId)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
        var pass = await EventPasses.EnsureAsync(db, booking, HostId, default);
        await db.SaveChangesAsync();
        return pass.Id;
    }

    private static async Task RevokeAsync(SqliteTestDb sqlite, Guid passId, string reason)
    {
        await using var db = await sqlite.NewContextAsync();
        EventPasses.Revoke(await db.HostedEventPasses.FirstAsync(p => p.Id == passId), HostId, reason);
        await db.SaveChangesAsync();
    }

    private static async Task SetStatusAsync(
        SqliteTestDb sqlite, Guid bookingId, HostedEventBookingStatus status)
    {
        await using var db = await sqlite.NewContextAsync();
        (await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId)).Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> BookAsync(SqliteTestDb sqlite, HostedEventBookingStatus status)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId,
            PartySize = 2, Kind = HostedEventBookingKind.DayPass, Status = status,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
        };
        db.HostedEventBookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task SeedAsync(SqliteTestDb sqlite)
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
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            IsPublished = true, DayPassCapacity = 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        await db.SaveChangesAsync();
    }
}
