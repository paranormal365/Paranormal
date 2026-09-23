using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Picking seats without signing in, and the emailed link that turns the pick into a hold
/// (item 235 slice 11d).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: guests may hold seats without an account, but not in a way that lets
/// somebody block an event with holds nobody stands behind, and never without the organizer being
/// able to reach them.</para>
///
/// <para>On SQLite, because the rules that matter most here are two unique indexes — one live pick
/// per square, one live pick per address — and the in-memory provider enforces neither.</para>
/// </remarks>
public sealed class HostedEventEmailPickTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid FridayId = Guid.NewGuid();
    private static readonly Guid SeatA1Id = Guid.NewGuid();
    private static readonly Guid SeatA2Id = Guid.NewGuid();

    private const string Stranger = "stranger@example.test";

    private static async Task<SqliteTestDb> SeedAsync(int extraSeats = 0)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        db.Users.Add(new AppUser
        {
            Id = HostId, Email = "host@example.test", UserName = "host@example.test",
            NormalizedEmail = "HOST@EXAMPLE.TEST", NormalizedUserName = "HOST@EXAMPLE.TEST",
            DisplayName = "The Host", DateCreated = now,
        });
        db.Users.Add(new AppUser
        {
            Id = MemberId, Email = "member@example.test", UserName = "member@example.test",
            NormalizedEmail = "MEMBER@EXAMPLE.TEST", NormalizedUserName = "MEMBER@EXAMPLE.TEST",
            DisplayName = "A Member", FirstName = "A", LastName = "Member", PhoneNumber = "615-555-0101",
            DateCreated = now,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "An Evening of Evidence", UrlName = "an-evening-of-evidence",
            StartsOn = now.Date.AddDays(30), EndsOn = now.Date.AddDays(30),
            LifecycleState = HostedEventLifecycleState.Published,
            LayoutKind = HostedEventLayoutKind.Seats,
            BookingMode = HostedEventBookingMode.Pick,
            HoldMinutes = 2880,
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventNights.Add(new HostedEventNight
        {
            Id = FridayId, HostedEventId = EventId, Date = now.Date.AddDays(30), SortOrder = 0,
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        var seats = new List<(Guid, string)> { (SeatA1Id, "A1"), (SeatA2Id, "A2") };
        for (var i = 0; i < extraSeats; i++) seats.Add((Guid.NewGuid(), $"B{i + 1}"));

        var order = 0;
        foreach (var (id, label) in seats)
        {
            db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit
            {
                Id = id, HostedEventId = EventId, Label = label, Capacity = 1, SortOrder = order++,
                DateCreated = now, CreatedByAppUserId = HostId,
            });
        }

        await db.SaveChangesAsync();
        return sqlite;
    }

    /// <summary>
    /// The pick letter is filed as the letter it is, and hands a template its hold link.
    /// </summary>
    /// <remarks>
    /// It was filed as "choose-your-emails" — a preferences letter that does not exist — so
    /// /admin/mail showed a seat hold under the wrong name, and it handed a template nothing, so a
    /// template for it would have rendered with no way to hold the places.
    /// </remarks>
    [Fact]
    public async Task The_pick_letter_is_filed_as_holding_places_and_hands_a_template_its_link()
    {
        await using var sqlite = await SeedAsync();
        var letters = new List<Ben.Data.Common.Interfaces.EmailMessage>();
        var email = new Moq.Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(Moq.It.IsAny<Ben.Data.Common.Interfaces.EmailMessage>(), Moq.It.IsAny<CancellationToken>()))
             .Callback<Ben.Data.Common.Interfaces.EmailMessage, CancellationToken>((m, _) => letters.Add(m))
             .Returns(Task.CompletedTask);
        var mailer = new EventGuestMailer(email.Object,
            Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://test.local" }),
            NullLogger<EventGuestMailer>.Instance);

        Assert.IsType<OkObjectResult>(
            (await Anonymous(sqlite).Pick(EventId, Picking(Stranger, SeatA1Id), mailer, default)).Result);

        var letter = Assert.Single(letters);
        Assert.Equal(Ben.Data.Common.Mail.MailKinds.HoldYourPlaces.Key, letter.Kind);

        var holdUrl = letter.Payload!.Supplied!["HoldUrl"].Value;
        Assert.Contains("/event-picks/", holdUrl);
        // The same link the built-in letter carries, so a template and the letter agree.
        Assert.Contains(holdUrl, letter.HtmlBody);
        Assert.Equal(Stranger, letter.Payload.Tables!["AppUsers"]["Email"]);
    }

    /// <summary>
    /// A template for the pick letter that leaves out the hold link is refused when it is saved.
    /// </summary>
    /// <remarks>
    /// Under the old kind this was accepted: it declared no link, so there was nothing to require.
    /// </remarks>
    [Fact]
    public void A_template_for_the_pick_letter_without_the_hold_link_is_refused()
    {
        var kind = Ben.Data.Common.Mail.MailKinds.HoldYourPlaces;

        Assert.Equal(["a way to hold the places"],
            Ben.Data.Common.Mail.MailTokens.MissingRequired(
                "Hold your places", "<p>Hello {AppUsers.DisplayName}, you picked some places.</p>", kind));

        // Either spelling of the link will do — the bare URL or the ready-made button.
        Assert.Empty(Ben.Data.Common.Mail.MailTokens.MissingRequired("Hold your places", "<p>{HoldButton}</p>", kind));
        Assert.Empty(Ben.Data.Common.Mail.MailTokens.MissingRequired("Hold your places", "<a href=\"{HoldUrl}\">Hold</a>", kind));
    }

    private static EventGuestMailer NoMail()
    {
        var email = new Moq.Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        return new EventGuestMailer(email.Object, Options.Create(new Ben.Data.Common.SiteIdentity()),
            NullLogger<EventGuestMailer>.Instance);
    }

    private static PublicHostedEventEmailPickController Anonymous(SqliteTestDb sqlite)
        => new(sqlite.Factory, new HostedEventCalendarSync())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
            },
        };

    private static PublicHostedEventBookingController SignedIn(SqliteTestDb sqlite, Guid userId)
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

    private static EmailLinkAccounts Accounts(SqliteTestDb sqlite)
    {
        var store = new UserStore<AppUser, IdentityRole<Guid>, BenDataContext, Guid>(sqlite.Factory.CreateDbContext());
        var users = new UserManager<AppUser>(
            store, Options.Create(new IdentityOptions()), new PasswordHasher<AppUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
            NullLogger<UserManager<AppUser>>.Instance);
        return new EmailLinkAccounts(users, new UserHandleService(sqlite.Factory), NullLogger<EmailLinkAccounts>.Instance);
    }

    private static PickHostedEventPlacesByEmailRequest Picking(string email, params Guid[] seats)
        => new("Ada", "Lovelace", email, "(615) 555-0100",
            [.. seats.Select(s => new HostedEventBookingNightChoice(FridayId, s))], seats.Length);

    /// <summary>
    /// Picks, then hands back a token that opens the pick — written straight onto the row, since the
    /// real one only exists in the letter.
    /// </summary>
    private static async Task<string> PickAsync(SqliteTestDb sqlite, string email, params Guid[] seats)
    {
        Assert.IsType<OkObjectResult>((await Anonymous(sqlite).Pick(EventId, Picking(email, seats), NoMail(), default)).Result);

        var token = EmailPicks.NewToken();
        await using var db = await sqlite.NewContextAsync();
        var pick = await db.HostedEventEmailPicks.SingleAsync(p => p.Email == email && p.IsLive);
        pick.TokenHash = EmailPicks.Hash(token);
        await db.SaveChangesAsync();
        return token;
    }

    // ── picking ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_pick_puts_its_seats_out_of_reach_for_fifteen_minutes_and_makes_no_account()
    {
        await using var sqlite = await SeedAsync();

        var result = await Anonymous(sqlite).Pick(EventId, Picking(Stranger, SeatA1Id), NoMail(), default);
        var placed = Assert.IsType<HostedEventEmailPickPlacedRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.InRange(placed.PendingUntilUtc, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.Contains(placed.Places, p => p.EndsWith("A1"));

        await using var db = await sqlite.NewContextAsync();
        var cells = await PlanOccupancy.ReadAsync(db, EventId, default);
        var cell = cells[(FridayId, SeatA1Id)];
        Assert.True(cell.AwaitingEmail);
        Assert.Equal(HostedEventBookingStatus.Held, cell.HeldAs);

        // Nothing is a booking and nobody has an account until the link is clicked.
        Assert.Empty(await db.HostedEventBookings.ToListAsync());
        Assert.False(await db.Users.AnyAsync(u => u.Email == Stranger));
    }

    [Fact]
    public async Task A_second_stranger_is_told_the_seat_is_being_confirmed()
    {
        await using var sqlite = await SeedAsync();
        await PickAsync(sqlite, Stranger, SeatA1Id);

        var refused = Assert.IsType<ConflictObjectResult>(
            (await Anonymous(sqlite).Pick(EventId, Picking("other@example.test", SeatA1Id), NoMail(), default)).Result);

        var body = Assert.IsType<HoldRefusedRecord>(refused.Value);
        Assert.Contains(SeatA1Id, body.TakenUnitIds);
    }

    [Fact]
    public async Task The_database_refuses_two_live_picks_of_one_seat_whatever_the_code_checked()
    {
        // The arbiter between picks, proved without the door's own check in front of it.
        await using var sqlite = await SeedAsync();
        await PickAsync(sqlite, Stranger, SeatA1Id);

        await using var db = await sqlite.NewContextAsync();
        var second = new HostedEventEmailPick
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, FirstName = "B", LastName = "C",
            Email = "other@example.test", Phone = "6155550100", TokenHash = EmailPicks.Hash("x"),
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15), DateCreated = DateTime.UtcNow,
        };
        second.Places.Add(new HostedEventEmailPickPlace
        {
            Id = Guid.NewGuid(), HostedEventEmailPickId = second.Id,
            HostedEventNightId = FridayId, HostedEventLayoutUnitId = SeatA1Id,
        });
        db.HostedEventEmailPicks.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_signed_in_guest_cannot_hold_a_seat_somebody_is_confirming()
    {
        // The bookings' own arbiter cannot see a pick, so the hold door has to look.
        await using var sqlite = await SeedAsync();
        await PickAsync(sqlite, Stranger, SeatA1Id);

        var refused = Assert.IsType<ConflictObjectResult>((await SignedIn(sqlite, MemberId).HoldPlaces(
            EventId, new HoldHostedEventPlacesRequest([new(FridayId, SeatA1Id)], 1), NoMail(), default)).Result);

        Assert.Contains(SeatA1Id, Assert.IsType<HoldRefusedRecord>(refused.Value).TakenUnitIds);
    }

    [Fact]
    public async Task Picking_again_from_the_same_address_replaces_the_first_pick()
    {
        await using var sqlite = await SeedAsync();
        await PickAsync(sqlite, Stranger, SeatA1Id);

        Assert.IsType<OkObjectResult>(
            (await Anonymous(sqlite).Pick(EventId, Picking(Stranger, SeatA2Id), NoMail(), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(1, await db.HostedEventEmailPicks.CountAsync(p => p.IsLive));

        // A1 is free again for somebody else.
        Assert.IsType<OkObjectResult>((await SignedIn(sqlite, MemberId).HoldPlaces(
            EventId, new HoldHostedEventPlacesRequest([new(FridayId, SeatA1Id)], 1), NoMail(), default)).Result);
    }

    [Fact]
    public async Task A_pick_nobody_confirmed_in_time_gives_the_seat_back()
    {
        await using var sqlite = await SeedAsync();
        await PickAsync(sqlite, Stranger, SeatA1Id);

        await using (var db = await sqlite.NewContextAsync())
        {
            var pick = await db.HostedEventEmailPicks.SingleAsync();
            pick.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await SignedIn(sqlite, MemberId).HoldPlaces(
            EventId, new HoldHostedEventPlacesRequest([new(FridayId, SeatA1Id)], 1), NoMail(), default)).Result);
    }

    [Theory]
    [InlineData("", "Lovelace", "615 555 0100", "first name")]
    [InlineData("Ada", "", "615 555 0100", "last name")]
    [InlineData("Ada", "Lovelace", "", "phone number")]
    [InlineData("Ada", "Lovelace", "call me", "doesn't look right")]
    public async Task The_organizer_gets_a_name_and_a_number_or_there_is_no_pick(
        string first, string last, string phone, string said)
    {
        await using var sqlite = await SeedAsync();

        var request = new PickHostedEventPlacesByEmailRequest(first, last, Stranger, phone,
            [new(FridayId, SeatA1Id)], 1);
        var refused = Assert.IsType<BadRequestObjectResult>(
            (await Anonymous(sqlite).Pick(EventId, request, NoMail(), default)).Result);

        Assert.Contains(said, (string)refused.Value!);
    }

    [Fact]
    public async Task Unproven_picks_may_not_take_more_than_their_share_of_a_night()
    {
        // Twelve seats: the ceiling is the floor of eight. Eight strangers may pick; the ninth is told
        // to sign in or wait.
        await using var sqlite = await SeedAsync(extraSeats: 10);

        await using (var db = await sqlite.NewContextAsync())
        {
            var seats = await db.HostedEventLayoutUnits.OrderBy(u => u.SortOrder).Select(u => u.Id).ToListAsync();
            for (var i = 0; i < EmailPicks.UnprovenFloor; i++)
                Assert.IsType<OkObjectResult>((await Anonymous(sqlite).Pick(
                    EventId, Picking($"person{i}@example.test", seats[i]), NoMail(), default)).Result);

            var refused = Assert.IsType<ConflictObjectResult>((await Anonymous(sqlite).Pick(
                EventId, Picking("one-too-many@example.test", seats[^1]), NoMail(), default)).Result);
            Assert.Contains("Sign in", (string)refused.Value!);
        }

        // And a signed-in guest is not held back by it.
        await using var check = await sqlite.NewContextAsync();
        var last = await check.HostedEventLayoutUnits.OrderByDescending(u => u.SortOrder).Select(u => u.Id).FirstAsync();
        Assert.IsType<OkObjectResult>((await SignedIn(sqlite, MemberId).HoldPlaces(
            EventId, new HoldHostedEventPlacesRequest([new(FridayId, last)], 1), NoMail(), default)).Result);
    }

    // ── the link ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clicking_the_link_makes_the_account_and_a_hold_on_the_events_own_clock()
    {
        await using var sqlite = await SeedAsync();
        var token = await PickAsync(sqlite, Stranger, SeatA1Id, SeatA2Id);

        var result = await Anonymous(sqlite).Confirm(token, Accounts(sqlite), NoMail(), default);
        var record = Assert.IsType<HostedEventEmailPickRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("held", record.State);
        Assert.True(record.AccountHasNoPassword);

        await using var db = await sqlite.NewContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == Stranger);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("Ada", user.FirstName);
        // Given to the organizer for this event, not put on the account.
        Assert.Null(user.PhoneNumber);

        var booking = await db.HostedEventBookings.Include(b => b.Nights).SingleAsync();
        Assert.Equal(user.Id, booking.LeadAppUserId);
        Assert.Equal(HostedEventBookingStatus.Held, booking.Status);
        Assert.Equal("(615) 555-0100", booking.ContactPhone);
        Assert.Equal(2, booking.PartySize);
        Assert.Equal(2, booking.Nights.Count(n => n.IsHolding));
        // The event's two days start at the click, not at the pick.
        Assert.InRange(booking.HoldExpiresUtc!.Value, DateTime.UtcNow.AddDays(2).AddMinutes(-5), DateTime.UtcNow.AddDays(2).AddMinutes(5));

        var pick = await db.HostedEventEmailPicks.Include(p => p.Places).SingleAsync();
        Assert.False(pick.IsLive);
        Assert.All(pick.Places, p => Assert.False(p.IsLive));
        Assert.Equal(booking.Id, pick.HostedEventBookingId);

        // Pressing the button again is the same statement.
        Assert.IsType<OkObjectResult>((await Anonymous(sqlite).Confirm(token, Accounts(sqlite), NoMail(), default)).Result);
        await using var after = await sqlite.NewContextAsync();
        Assert.Equal(1, await after.HostedEventBookings.CountAsync());
    }

    [Fact]
    public async Task An_address_that_already_has_an_account_gets_the_hold_on_that_account()
    {
        await using var sqlite = await SeedAsync();
        var token = await PickAsync(sqlite, "member@example.test", SeatA1Id);

        Assert.IsType<OkObjectResult>((await Anonymous(sqlite).Confirm(token, Accounts(sqlite), NoMail(), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(MemberId, (await db.HostedEventBookings.SingleAsync()).LeadAppUserId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "member@example.test"));
    }

    [Fact]
    public async Task A_link_clicked_after_fifteen_minutes_holds_nothing()
    {
        await using var sqlite = await SeedAsync();
        var token = await PickAsync(sqlite, Stranger, SeatA1Id);

        await using (var db = await sqlite.NewContextAsync())
        {
            var pick = await db.HostedEventEmailPicks.SingleAsync();
            pick.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var refused = Assert.IsType<ConflictObjectResult>(
            (await Anonymous(sqlite).Confirm(token, Accounts(sqlite), NoMail(), default)).Result);
        Assert.Contains("fifteen minutes", (string)refused.Value!);

        await using var check = await sqlite.NewContextAsync();
        Assert.Empty(await check.HostedEventBookings.ToListAsync());
        Assert.False(await check.Users.AnyAsync(u => u.Email == Stranger));
    }

    [Fact]
    public async Task A_seat_the_venue_gave_away_meanwhile_is_named_when_the_link_is_clicked()
    {
        await using var sqlite = await SeedAsync();
        var token = await PickAsync(sqlite, Stranger, SeatA1Id);

        // The venue putting somebody in A1 on its own board, which does not look at picks.
        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = MemberId, PartySize = 1,
                Kind = HostedEventBookingKind.Overnight, Status = HostedEventBookingStatus.Requested,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            };
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, HostedEventNightId = FridayId,
                HostedEventLayoutUnitId = SeatA1Id, DateCreated = DateTime.UtcNow,
            });
            db.HostedEventBookings.Add(booking);
            BookingTransitions.Confirm(booking, HostId, null, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var refused = Assert.IsType<ConflictObjectResult>(
            (await Anonymous(sqlite).Confirm(token, Accounts(sqlite), NoMail(), default)).Result);
        Assert.Contains("A1", (string)refused.Value!);

        await using var check = await sqlite.NewContextAsync();
        var pick = await check.HostedEventEmailPicks.SingleAsync();
        Assert.False(pick.IsLive);
        Assert.NotNull(pick.RefusedSentence);
        Assert.Equal(1, await check.HostedEventBookings.CountAsync());
    }

    [Fact]
    public async Task The_link_lets_somebody_with_no_password_let_the_places_go()
    {
        await using var sqlite = await SeedAsync();
        var token = await PickAsync(sqlite, Stranger, SeatA1Id);
        Assert.IsType<OkObjectResult>((await Anonymous(sqlite).Confirm(token, Accounts(sqlite), NoMail(), default)).Result);

        var result = await Anonymous(sqlite).LetGo(token, default);
        var record = Assert.IsType<HostedEventEmailPickRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(HostedEventBookingStatus.Cancelled, record.Booking!.Status);

        // And the seat is free for the next person.
        Assert.IsType<OkObjectResult>((await SignedIn(sqlite, MemberId).HoldPlaces(
            EventId, new HoldHostedEventPlacesRequest([new(FridayId, SeatA1Id)], 1), NoMail(), default)).Result);
    }

    [Fact]
    public async Task A_pick_nobody_confirmed_is_deleted_a_day_later_with_the_name_and_phone_in_it()
    {
        await using var sqlite = await SeedAsync();
        await PickAsync(sqlite, Stranger, SeatA1Id);

        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        await EmailPicks.RetireLapsedAsync(db, now.AddMinutes(20), null, default);
        Assert.Equal(0, await EmailPicks.ForgetAsync(db, now.AddHours(2), default));
        Assert.Equal(1, await EmailPicks.ForgetAsync(db, now.AddDays(1).AddMinutes(1), default));
        Assert.Empty(await db.HostedEventEmailPickPlaces.ToListAsync());
    }

    // ── the three, for signed-in guests ──────────────────────────────────────

    [Fact]
    public async Task A_signed_in_guest_with_no_phone_on_their_account_is_asked_for_one()
    {
        await using var sqlite = await SeedAsync();

        var refused = Assert.IsType<BadRequestObjectResult>((await SignedIn(sqlite, HostId).HoldPlaces(
            EventId, new HoldHostedEventPlacesRequest([new(FridayId, SeatA1Id)], 1), NoMail(), default)).Result);
        Assert.Contains("first name", (string)refused.Value!);

        Assert.IsType<OkObjectResult>((await SignedIn(sqlite, HostId).HoldPlaces(
            EventId,
            new HoldHostedEventPlacesRequest([new(FridayId, SeatA1Id)], 1,
                FirstName: "Thomas", LastName: "House", Phone: "+1 615 555 0199"),
            NoMail(), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal("+1 615 555 0199", (await db.HostedEventBookings.SingleAsync()).ContactPhone);
        var host = await db.Users.SingleAsync(u => u.Id == HostId);
        Assert.Equal("Thomas", host.FirstName);
        Assert.Null(host.PhoneNumber);
    }

    [Theory]
    [InlineData("(615) 555-0100", true)]
    [InlineData("+44 20 7946 0958", true)]
    [InlineData("555-0100", true)]
    [InlineData("12345", false)]
    [InlineData("615-555-0100 ext 4", false)]
    [InlineData("1234567890123456", false)]
    public void A_phone_is_judged_by_its_digits(string phone, bool fine)
        => Assert.Equal(fine, BookingContact.LooksLikeAPhone(phone));

    [Fact]
    public void The_ceiling_is_a_quarter_of_the_plan_and_never_below_the_floor()
    {
        Assert.Equal(EmailPicks.UnprovenFloor, EmailPicks.CeilingFor(2));
        Assert.Equal(100, EmailPicks.CeilingFor(400));
    }
}
