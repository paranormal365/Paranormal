using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The letters a hosted event sends a guest (item 235 phase 2.3).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"We should probably send a confirmation e-mail or offer it. Generate
/// a QR code for the confirmation the event organizer can scan to check them in when they arrive
/// so check in is smoother."</i></para>
///
/// <para><b>The claim under test is that the letter is enough on its own.</b> A guest reading it
/// on a phone at a hotel door has to be able to get in from what is in front of them: the code
/// drawn into the letter rather than linked from it, the nights and the rooms in words, and the
/// venue's own reason when the answer was no.</para>
/// </remarks>
public sealed class EventGuestMailerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly DateTime Friday = new(2026, 10, 30);
    private static readonly DateTime Saturday = new(2026, 10, 31);

    private sealed record Sent(List<EmailMessage> Messages, EventGuestMailer Mailer);

    // ── the yes ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_confirmation_carries_the_code_drawn_in_rather_than_linked()
    {
        // Most mail clients block remote pictures until somebody clicks, and a guest at a door
        // whose pass never loaded has no pass.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);
        await IssuePassAsync(sqlite, bookingId);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            Assert.True(await mailer.SendDecisionAsync(db, bookingId, default));

        var letter = Assert.Single(sent);
        Assert.Contains("confirmed", letter.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data:image/png;base64,", letter.HtmlBody);
        // And the link as well, for the client that strips data URIs instead.
        Assert.Contains("/api/public/event-passes/", letter.HtmlBody);
        Assert.Contains("One code admits your whole party", letter.HtmlBody);
    }

    [Fact]
    public async Task A_confirmation_says_the_nights_and_the_rooms_in_words()
    {
        // A door with a flat battery, or a camera that will not focus in the dark, still has to be
        // able to see who this is. A pass only a machine can read fails on the one evening it
        // matters.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded,
                                        overnight: true);
        await IssuePassAsync(sqlite, bookingId);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        var letter = Assert.Single(sent);
        Assert.Contains("Blue Room", letter.HtmlBody);
        Assert.Contains("Friday, October 30", letter.HtmlBody);
        Assert.Contains("Saturday, October 31", letter.HtmlBody);
    }

    [Fact]
    public async Task Each_booked_night_is_its_own_calendar_entry()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded,
                                        overnight: true);
        await IssuePassAsync(sqlite, bookingId);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        var calendar = Assert.Single(Assert.Single(sent).Attachments!);
        var text = System.Text.Encoding.UTF8.GetString(calendar.Content);

        Assert.Equal("event.ics", calendar.FileName);
        // Two nights, two entries. One block across the weekend would tell a guest nothing about
        // where they sleep on Saturday.
        Assert.Equal(2, Count(text, "BEGIN:VEVENT"));
        Assert.Contains("Blue Room", text);
    }

    [Fact]
    public async Task Sending_the_pass_records_that_it_went_out()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);
        await IssuePassAsync(sqlite, bookingId);

        var (_, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        await using (var db = await sqlite.NewContextAsync())
        {
            var pass = await db.HostedEventPasses.SingleAsync();
            // On the PASS, not the booking, so a reissue starts unsent and a host can see whose
            // replacement has not gone out yet.
            Assert.NotNull(pass.EmailedUtc);
        }
    }

    // ── the no ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_refusal_carries_the_venues_own_reason()
    {
        // A refusal with no reason reads as arbitrary, and the commonest reason is one the guest
        // can act on.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.TurnedDown, seeded,
                                        decisionNote: "We only have the Suite left, if four suits you.");

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        var letter = Assert.Single(sent);
        Assert.Contains("We only have the Suite left", letter.HtmlBody);
        Assert.Contains("nothing has been charged", letter.HtmlBody, StringComparison.OrdinalIgnoreCase);
        // No pass and no diary entry for a weekend nobody is going to.
        Assert.Empty(letter.Attachments ?? []);
        Assert.DoesNotContain("data:image/png", letter.HtmlBody);
    }

    [Fact]
    public async Task A_released_booking_says_the_pass_has_stopped_working()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Cancelled, seeded);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        var letter = Assert.Single(sent);
        Assert.Contains("released", letter.HtmlBody);
        Assert.Contains("no longer works", letter.HtmlBody);
    }

    // ── what must never break a decision ─────────────────────────────────────

    [Fact]
    public async Task A_send_that_throws_never_undoes_the_decision()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);

        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("no smtp host"));

        var mailer = new EventGuestMailer(email.Object, Site(), NullLogger<EventGuestMailer>.Instance);

        await using var db = await sqlite.NewContextAsync();
        // Answers false rather than throwing. A guest who is confirmed but whose letter bounced is
        // a confirmed guest.
        Assert.False(await mailer.SendDecisionAsync(db, bookingId, default));
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_deployment_has_no_mail()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);

        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        var mailer = new EventGuestMailer(email.Object, Site(), NullLogger<EventGuestMailer>.Instance);

        await using var db = await sqlite.NewContextAsync();
        Assert.False(await mailer.SendDecisionAsync(db, bookingId, default));
        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()),
                     Times.Never);
    }

    [Fact]
    public async Task A_booking_that_no_longer_exists_is_not_an_error()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        var (_, mailer) = Mailer();
        await using var db = await sqlite.NewContextAsync();
        Assert.False(await mailer.SendDecisionAsync(db, Guid.NewGuid(), default));
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static IOptions<SiteIdentity> Site()
        => Options.Create(new SiteIdentity { Name = "Test", BaseUrl = "https://test.local" });

    private static Sent Mailer()
    {
        var messages = new List<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .Callback<EmailMessage, CancellationToken>((m, _) => messages.Add(m))
             .Returns(Task.CompletedTask);

        return new Sent(messages,
            new EventGuestMailer(email.Object, Site(), NullLogger<EventGuestMailer>.Instance));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }

    private sealed record Seeded(Guid FridayId, Guid SaturdayId, Guid BlueRoomId);

    private static async Task IssuePassAsync(SqliteTestDb sqlite, Guid bookingId)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
        await EventPasses.EnsureAsync(db, booking, HostId, default);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> BookAsync(
        SqliteTestDb sqlite, HostedEventBookingStatus status, Seeded seeded,
        bool overnight = false, string? decisionNote = null)
    {
        await using var db = await sqlite.NewContextAsync();

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId,
            PartySize = 2, Status = status, DecisionNote = decisionNote,
            Kind = overnight ? HostedEventBookingKind.Overnight : HostedEventBookingKind.DayPass,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
        };

        if (overnight)
        {
            foreach (var nightId in new[] { seeded.FridayId, seeded.SaturdayId })
            {
                booking.Nights.Add(new HostedEventBookingNight
                {
                    Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                    HostedEventNightId = nightId, PlaceRoomId = seeded.BlueRoomId,
                    DateCreated = DateTime.UtcNow,
                });
            }
        }

        db.HostedEventBookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task<Seeded> SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        db.Users.Add(new AppUser
        {
            Id = HostId, Email = "host@example.com", UserName = "host@example.com",
            DisplayName = "The Host", DateCreated = DateTime.UtcNow,
        });
        db.Users.Add(new AppUser
        {
            Id = GuestId, Email = "guest@example.com", UserName = "guest@example.com",
            DisplayName = "Ada Lovelace", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            PublicEmail = "stay@thomashouse.example", DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = HostId,
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
            StartsOn = Friday, EndsOn = Saturday, IsPublished = true, DayPassCapacity = 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        var friday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = Friday,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var saturday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = Saturday,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventNights.AddRange(friday, saturday);

        await db.SaveChangesAsync();
        return new Seeded(friday.Id, saturday.Id, blue.Id);
    }
}
