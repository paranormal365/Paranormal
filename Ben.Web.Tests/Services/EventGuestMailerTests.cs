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
    public async Task A_confirmation_says_how_to_get_in_and_around_before_the_pass()
    {
        // Audit finding A7: stairs, lighting and parking are planned around before somebody travels.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        await using (var db = await sqlite.NewContextAsync())
        {
            var ev = await db.HostedEvents.SingleAsync(e => e.Id == EventId);
            ev.AccessNotes = "Stairs only to the ballroom.\nParking <behind> the hotel.";
            await db.SaveChangesAsync();
        }
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);
        await IssuePassAsync(sqlite, bookingId);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        var body = Assert.Single(sent).HtmlBody;
        Assert.Contains("Getting in and getting around", body);
        Assert.Contains("Stairs only to the ballroom.<br />Parking &lt;behind&gt; the hotel.", body);
        Assert.True(body.IndexOf("Getting in and getting around", StringComparison.Ordinal)
                  < body.IndexOf("Show this at the door", StringComparison.Ordinal));
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
        {
            await mailer.SendDecisionAsync(db, bookingId, default);

            // Not written yet: the stamp rides in the caller's save with the letter itself (item
            // 239b), so it can no longer be set for a letter that was never queued.
            await using (var peek = await sqlite.NewContextAsync())
                Assert.Null((await peek.HostedEventPasses.SingleAsync()).EmailedUtc);

            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var pass = await db.HostedEventPasses.SingleAsync();
            // On the PASS, not the booking, so a reissue starts unsent and a host can see whose
            // replacement has not gone out yet.
            Assert.NotNull(pass.EmailedUtc);
        }
    }

    // ── the diary keeps the venue's clock ────────────────────────────────────

    [Fact]
    public async Task A_night_in_the_diary_starts_at_six_at_the_venue_not_at_six_utc()
    {
        // The first version wrote Date.AddHours(18) into a builder that stamps everything as UTC,
        // so a Nashville dinner landed in a guest's calendar at one in the afternoon. The event is
        // in America/Chicago, where 30 October 2026 is still daylight time: six in the evening
        // there is 23:00Z, and ten the next morning is 15:00Z.
        var text = await CalendarTextAsync(overnight: true);

        Assert.Contains("DTSTART:20261030T230000Z", text);
        Assert.Contains("DTEND:20261031T150000Z", text);
    }

    [Fact]
    public async Task The_clocks_going_back_do_not_move_the_saturday_night()
    {
        // Central time leaves daylight saving at two in the morning on 1 November 2026 — in the
        // middle of the Saturday night. Each end is converted on its own: six on Saturday is still
        // 23:00Z, and ten on Sunday is now 16:00Z, an hour later than a fixed sixteen-hour span
        // from the start would have put the check-out.
        var text = await CalendarTextAsync(overnight: true);

        Assert.Contains("DTSTART:20261031T230000Z", text);
        Assert.Contains("DTEND:20261101T160000Z", text);
    }

    [Fact]
    public async Task A_day_pass_is_one_entry_that_also_starts_at_six_at_the_venue()
    {
        // No nights, so one entry for the whole event — converted through the same zone, not
        // written as if the venue kept Greenwich time.
        var text = await CalendarTextAsync(overnight: false);

        Assert.Equal(1, Count(text, "BEGIN:VEVENT"));
        Assert.Contains("DTSTART:20261030T230000Z", text);
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

    // ── before the venue has said anything (phase 6) ─────────────────────────

    [Fact]
    public async Task The_letter_that_answers_an_ask_says_nothing_is_held()
    {
        // Silence reads as a booking. Somebody who filled in a form and heard nothing assumes it
        // worked, and a guest who assumed that about a REQUEST arrives at a hotel with a suitcase.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(
            sqlite, HostedEventBookingStatus.Requested, seeded, overnight: true);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            Assert.True(await mailer.SendAskedAsync(db, bookingId, default));

        var letter = Assert.Single(sent);
        Assert.Contains("Nothing is held", letter.HtmlBody);
        Assert.Contains("Blue Room", letter.HtmlBody);

        // And NO diary entry: an appointment for a place nobody has agreed to is the same lie in
        // another form.
        Assert.Empty(letter.Attachments ?? []);
    }

    [Fact]
    public async Task The_letter_for_a_hold_says_when_it_runs_out_on_the_venues_clock()
    {
        // A hold that lapses is a decision the clock takes instead of the venue. A guest who was
        // never told the time cannot act before it — and "six o'clock" means the clock on the wall
        // where the seats are, not the server's.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(
            sqlite, HostedEventBookingStatus.Held, seeded, overnight: true);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            // Midnight UTC, which in Nashville is the evening before — so a letter that printed
            // the server's clock would name the wrong day as well as the wrong hour.
            booking.HoldExpiresUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            Assert.True(await mailer.SendHoldPlacedAsync(db, bookingId, default));

        var letter = Assert.Single(sent);
        Assert.Contains("held for you", letter.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10/19/2026", letter.HtmlBody);
        Assert.Empty(letter.Attachments ?? []);
    }

    // ── news about the whole event (phase 6) ─────────────────────────────────

    [Fact]
    public async Task Calling_an_event_off_writes_to_everybody_who_kept_the_date_free()
    {
        // The organizer's screen has claimed "everybody with a place has been told" since phase 3,
        // and until this letter existed nothing was sent to anybody. Waiting counts as keeping the
        // date free: a party still holding places has arranged their weekend just as hard as a
        // confirmed one.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);
        // A different person, because only one live booking per lead per event is allowed and the
        // database enforces it — which is itself worth knowing while writing a fixture.
        await BookAsync(sqlite, HostedEventBookingStatus.Held, seeded, lead: HostId);
        await BookAsync(sqlite, HostedEventBookingStatus.TurnedDown, seeded);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            Assert.Equal(2, await mailer.SendCalledOffAsync(
                db, EventId, "The venue flooded.", default));

        Assert.All(sent, letter =>
        {
            Assert.Contains("not going ahead", letter.Subject);
            Assert.Contains("The venue flooded.", letter.HtmlBody);
            // Nothing was ever paid here, and a cancellation letter must not imply otherwise.
            Assert.Contains("nothing to refund", letter.HtmlBody);
        });
    }

    [Fact]
    public async Task Reaching_the_numbers_is_told_to_the_people_who_were_waiting_on_it()
    {
        // The other half of a minimum number: somebody asked to keep a weekend free while a venue
        // counts heads has been holding a date on a maybe.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        await BookAsync(sqlite, HostedEventBookingStatus.Requested, seeded);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            Assert.Equal(1, await mailer.SendGoingAheadAsync(db, EventId, default));

        var letter = Assert.Single(sent);
        Assert.Contains("going ahead", letter.Subject);
        Assert.Contains("the venue will answer your booking", letter.HtmlBody,
                        StringComparison.OrdinalIgnoreCase);
    }

    // ── a decision and its letter ────────────────────────────────────────────

    /// <summary>
    /// A decision letter that cannot be queued is the caller's to know about, not swallowed here.
    /// </summary>
    /// <remarks>
    /// <para>This was <c>A_send_that_throws_never_undoes_the_decision</c>: answer false rather than
    /// throw, because a guest who is confirmed but whose letter bounced is a confirmed guest. Right
    /// while the letter was a call to a mail system. It is now a row in the caller's transaction
    /// (item 239b), and a swallowed failure there would let the decision commit without it — the
    /// confirmed guest with no pass, in silence, which is what the change exists to stop.</para>
    ///
    /// <para>What the caller then does is <c>ABookingDecisionCommitsWithItsLetterTests</c>: the
    /// decision is not saved and the host is told to try again.</para>
    /// </remarks>
    [Fact]
    public async Task A_decision_letter_that_cannot_be_queued_reaches_the_caller()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded);

        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);

        var queue = new Mock<Ben.Data.WebApi.Services.IOutboxEmailQueue>();
        queue.Setup(q => q.EnqueueAsync(
                 It.IsAny<Ben.Data.Source.Context.BenDataContext>(),
                 It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("the outbox is unreachable"));

        var mailer = new EventGuestMailer(email.Object, Site(), NullLogger<EventGuestMailer>.Instance,
                                          queue.Object);

        await using var db = await sqlite.NewContextAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mailer.SendDecisionAsync(db, bookingId, default));
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

        // SendAskedAsync no longer goes through IEmailService: it queues into the caller's
        // context so the letter commits with the request it describes (item 239b). Recording both
        // into the same list keeps every assertion below about WHAT the letter says, while the
        // route it took is what changed.
        var queue = new Mock<Ben.Data.WebApi.Services.IOutboxEmailQueue>();
        queue.Setup(q => q.EnqueueAsync(
                 It.IsAny<Ben.Data.Source.Context.BenDataContext>(),
                 It.IsAny<EmailMessage>(),
                 It.IsAny<CancellationToken>()))
             .Callback<Ben.Data.Source.Context.BenDataContext, EmailMessage, CancellationToken>(
                 (_, m, _) => messages.Add(m))
             .Returns(Task.CompletedTask);

        return new Sent(messages,
            new EventGuestMailer(email.Object, Site(), NullLogger<EventGuestMailer>.Instance,
                                 queue.Object));
    }

    /// <summary>The calendar file a confirmed booking's letter carries, as text.</summary>
    private static async Task<string> CalendarTextAsync(bool overnight)
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var bookingId = await BookAsync(sqlite, HostedEventBookingStatus.Confirmed, seeded,
                                        overnight: overnight);
        await IssuePassAsync(sqlite, bookingId);

        var (sent, mailer) = Mailer();
        await using (var db = await sqlite.NewContextAsync())
            await mailer.SendDecisionAsync(db, bookingId, default);

        var calendar = Assert.Single(Assert.Single(sent).Attachments!);
        return System.Text.Encoding.UTF8.GetString(calendar.Content);
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }

    /// <param name="BlueRoomId">
    /// The event's LAYOUT UNIT for the Blue Room. A booking holds what the event offers, not the
    /// venue's own row.
    /// </param>
    private sealed record Seeded(Guid FridayId, Guid SaturdayId, Guid BlueRoomId);

    private static async Task IssuePassAsync(SqliteTestDb sqlite, Guid bookingId)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
        await EventPasses.EnsureAsync(db, booking, HostId, default);
        await db.SaveChangesAsync();
    }

    /// <param name="lead">
    /// Whose booking it is. Only one LIVE booking per person per event is allowed — the database
    /// says so — so a test that wants two parties waiting has to use two people.
    /// </param>
    private static async Task<Guid> BookAsync(
        SqliteTestDb sqlite, HostedEventBookingStatus status, Seeded seeded,
        bool overnight = false, string? decisionNote = null, Guid? lead = null)
    {
        await using var db = await sqlite.NewContextAsync();

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = lead ?? GuestId,
            PartySize = 2, Status = status, DecisionNote = decisionNote,
            Kind = overnight ? HostedEventBookingKind.Overnight : HostedEventBookingKind.DayPass,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = lead ?? GuestId,
        };

        if (overnight)
        {
            foreach (var nightId in new[] { seeded.FridayId, seeded.SaturdayId })
            {
                booking.Nights.Add(new HostedEventBookingNight
                {
                    Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                    HostedEventNightId = nightId, HostedEventLayoutUnitId = seeded.BlueRoomId,
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
            // Said rather than left to the column default: the diary tests above assert Central
            // time, and a default that moved would fail them for a reason nobody could see.
            TimeZoneId = "America/Chicago",
            StartsOn = Friday, EndsOn = Saturday, LifecycleState = HostedEventLifecycleState.Published, DayPassCapacity = 10,
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
        // The event's plan, offering the one room. Without this the booking nights would point at
        // a unit that does not exist and the letter would say "just for the day".
        var blueUnit = new HostedEventLayoutUnit
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, PlaceRoomId = blue.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventLayoutUnits.Add(blueUnit);
        await db.SaveChangesAsync();

        return new Seeded(friday.Id, saturday.Id, blueUnit.Id);
    }
}
