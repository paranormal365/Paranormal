using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A guest's own list of what they have coming up, and asking for their pass again
/// (item 235 phase 6).
/// </summary>
/// <remarks>
/// <para><b>The interesting rule is the one exception.</b> Everywhere else on the guest's door a
/// released booking is gone, because "have I got a place" is answered no and a screen that says
/// otherwise would stop them ever asking again. The list of what they have coming up is the one
/// place where dropping it is wrong: they kept the date free, and a list that quietly loses the
/// weekend they were released from answers a question nobody asked.</para>
///
/// <para><b>And it stops mattering once the event is over</b>, or the list would fill up with
/// weekends that did not happen years ago.</para>
/// </remarks>
public sealed class MyHostedEventsTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();

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

    /// <summary>A mailer with nothing behind it — the default in every environment today.</summary>
    private static EventGuestMailer NoMail()
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(x => x.IsConfigured).Returns(false);
        email.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                                     It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("Email service is not configured."));

        return new EventGuestMailer(
            email.Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity()),
            NullLogger<EventGuestMailer>.Instance);
    }

    /// <summary>One event on the given dates, with one booking in the given state.</summary>
    private static async Task<Guid> SeedAsync(
        SqliteTestDb sqlite, DateTime startsOn, HostedEventBookingStatus status)
    {
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;
        var eventId = Guid.NewGuid();

        if (!await db.Users.AnyAsync(u => u.Id == HostId))
        {
            foreach (var (id, name) in new[] { (HostId, "The Host"), (GuestId, "A Guest") })
                db.Users.Add(new AppUser
                {
                    Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test",
                    DisplayName = name, DateCreated = now,
                });

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
        }

        db.HostedEvents.Add(new HostedEvent
        {
            Id = eventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = $"A weekend on {startsOn:MM/dd/yyyy}", UrlName = $"w-{eventId:N}",
            StartsOn = startsOn, EndsOn = startsOn.AddDays(1),
            LifecycleState = HostedEventLifecycleState.Published,
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, LeadAppUserId = GuestId,
            PartySize = 2, Kind = HostedEventBookingKind.DayPass, Status = status,
            DateCreated = now, CreatedByAppUserId = GuestId,
        };
        db.HostedEventBookings.Add(booking);

        await db.SaveChangesAsync();
        return eventId;
    }

    private static async Task<IReadOnlyList<MyHostedEventBookingRecord>> MineAsync(SqliteTestDb sqlite)
    {
        var answer = await As(sqlite, GuestId).GetMine(default);
        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<MyHostedEventBookingRecord>>(ok.Value);
    }

    // ── what I have coming up ────────────────────────────────────────────────

    [Fact]
    public async Task A_weekend_the_venue_released_still_shows_until_it_is_over()
    {
        // They kept the date free. Dropping it the moment the venue let go is how somebody finds
        // out on the Friday, and the one place this belongs is the list of what they have coming
        // up — not the answer to "have I got a place here", which is still no.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var soon = await SeedAsync(
            sqlite, DateTime.UtcNow.Date.AddDays(20), HostedEventBookingStatus.Cancelled);

        var mine = await MineAsync(sqlite);

        var row = Assert.Single(mine);
        Assert.Equal(soon, row.HostedEventId);
        Assert.Equal(HostedEventBookingStatus.Cancelled, row.Status);
    }

    [Fact]
    public async Task A_released_booking_for_an_event_that_has_happened_is_gone()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, DateTime.UtcNow.Date.AddDays(-40), HostedEventBookingStatus.Cancelled);

        Assert.Empty(await MineAsync(sqlite));
    }

    [Fact]
    public async Task Asking_whether_I_have_a_place_still_answers_no_after_a_release()
    {
        // The other half of the rule, and the reason it is a parameter rather than a change of
        // heart: if this said yes, the guest could never ask the venue for a place again.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var eventId = await SeedAsync(
            sqlite, DateTime.UtcNow.Date.AddDays(20), HostedEventBookingStatus.Cancelled);

        var answer = await As(sqlite, GuestId).GetMyBooking(eventId, default);

        Assert.IsType<NotFoundResult>(answer.Result);
    }

    [Fact]
    public async Task A_history_of_refusals_never_hides_the_pass_somebody_actually_holds()
    {
        // A guest may ask several times at one event: released, then confirmed. Only one can be
        // live, because the database says so — but without an order this door took whichever row
        // came back first, which in practice was the OLDEST. The pass page told a confirmed guest
        // "this booking was released" while their live pass sat one row below. Found in a browser
        // by doing what a guest does, which is to try more than once.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var eventId = await SeedAsync(
            sqlite, DateTime.UtcNow.Date.AddDays(20), HostedEventBookingStatus.Cancelled);

        await using (var db = await sqlite.NewContextAsync())
        {
            var confirmed = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = eventId, LeadAppUserId = GuestId,
                PartySize = 2, Kind = HostedEventBookingKind.DayPass,
                Status = HostedEventBookingStatus.Confirmed,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
            };
            db.HostedEventBookings.Add(confirmed);
            await EventPasses.EnsureAsync(db, confirmed, HostId, default);
            await db.SaveChangesAsync();
        }

        var answer = await As(sqlite, GuestId).GetMyPass(eventId, default);

        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        var pass = Assert.IsType<MyHostedEventPassRecord>(ok.Value);
        Assert.Null(pass.Pass.RevokedUtc);
    }

    // ── send me my pass again ────────────────────────────────────────────────

    [Fact]
    public async Task Nobody_can_post_themselves_a_pass_for_a_place_that_is_not_agreed()
    {
        // The refusal a guest is likeliest to meet, and it has to say what they are waiting for
        // rather than reporting a failure.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var eventId = await SeedAsync(
            sqlite, DateTime.UtcNow.Date.AddDays(20), HostedEventBookingStatus.Requested);

        var answer = await As(sqlite, GuestId).EmailMyPass(eventId, NoMail(), default);

        var refused = Assert.IsType<ConflictObjectResult>(answer.Result);
        Assert.Contains("confirms your place", refused.Value?.ToString());
    }

    [Fact]
    public async Task A_confirmed_guest_on_a_site_with_no_mail_is_told_the_screen_is_the_pass()
    {
        // Most deployments have no mail at all. "Could not send" would read as a fault; the truth
        // is that the pass on the screen is the same pass, and saying so ends the matter.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var eventId = await SeedAsync(
            sqlite, DateTime.UtcNow.Date.AddDays(20), HostedEventBookingStatus.Confirmed);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.HostedEventId == eventId);
            await EventPasses.EnsureAsync(db, booking, HostId, default);
            await db.SaveChangesAsync();
        }

        var answer = await As(sqlite, GuestId).EmailMyPass(eventId, NoMail(), default);

        var refused = Assert.IsType<ConflictObjectResult>(answer.Result);
        Assert.Contains("pass on this screen", refused.Value?.ToString());
    }

    [Fact]
    public async Task Somebody_elses_booking_is_not_found_rather_than_forbidden()
    {
        // Invisible rather than merely refused: 403 on a booking that exists confirms it exists.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var eventId = await SeedAsync(
            sqlite, DateTime.UtcNow.Date.AddDays(20), HostedEventBookingStatus.Confirmed);

        var answer = await As(sqlite, HostId).EmailMyPass(eventId, NoMail(), default);

        Assert.IsType<NotFoundResult>(answer.Result);
    }
}
