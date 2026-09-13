using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The programme: first come, a queue after that, and everybody told when it changes (item 235 phase 10).
/// </summary>
public sealed class HostedEventSessionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Cat = Guid.NewGuid();
    private static readonly Guid Dan = Guid.NewGuid();   // asked, not yet confirmed
    private static readonly Guid Eve = Guid.NewGuid();

    private static readonly DateTime Saturday = DateTime.UtcNow.Date.AddDays(30);

    private sealed record Mail(List<EmailMessage> Sent, IEmailService Service);

    private static Mail Mailbox()
    {
        var sent = new List<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent.Add(m)).Returns(Task.CompletedTask);
        return new Mail(sent, email.Object);
    }

    private static EventGuestMailer Mailer(Mail mail)
        => new(mail.Service, Options.Create(new SiteIdentity()), NullLogger<EventGuestMailer>.Instance);

    private static T As<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return controller;
    }

    private static HostedEventSessionController HostSide(SqliteTestDb sqlite)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(Host, OrgId, It.IsAny<OrganizationSecurityTable>(),
            It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return As(new HostedEventSessionController(sqlite.Factory, null!, security.Object, new HostedEventAccess(security.Object)), Host);
    }

    private static PublicHostedEventProgrammeController Guest(SqliteTestDb sqlite, Guid who)
        => As(new PublicHostedEventProgrammeController(sqlite.Factory), who);

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (Host, "Mrs Cole"), (Ann, "Ann"), (Bob, "Bob"), (Cat, "Cat"), (Dan, "Dan"), (Eve, "Eve") })
            db.Users.Add(new AppUser { Id = id, Email = $"{name.ToLowerInvariant()}@example.test", UserName = $"{id:N}", DisplayName = name, DateCreated = now });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = Host });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = Host });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Halloween Lock-In", UrlName = "lock-in",
            StartsOn = Saturday, EndsOn = Saturday, TimeZoneId = "America/Chicago",
            LifecycleState = HostedEventLifecycleState.Published, DateCreated = now, CreatedByAppUserId = Host,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = EventId, Date = Saturday, DateCreated = now, CreatedByAppUserId = Host });

        foreach (var (lead, status, party) in new[]
                 {
                     (Ann, HostedEventBookingStatus.Confirmed, 2), (Bob, HostedEventBookingStatus.Confirmed, 1),
                     (Cat, HostedEventBookingStatus.Confirmed, 1), (Dan, HostedEventBookingStatus.Requested, 1),
                     (Eve, HostedEventBookingStatus.Confirmed, 1),
                 })
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = lead, PartySize = party,
                Kind = HostedEventBookingKind.DayPass, Status = status, DateCreated = now, CreatedByAppUserId = lead,
            });

        await db.SaveChangesAsync();
        return sqlite;
    }

    /// <summary>The Ovilus class, 9 to 10 PM at the venue, holding <paramref name="capacity"/>, programme published.</summary>
    private static async Task<Guid> OvilusAsync(SqliteTestDb sqlite, Mail mail, int? capacity = 2)
    {
        var result = await HostSide(sqlite).Create(OrgId, EventId, new SaveHostedEventSessionRequest(
            "Operating the Ovilus", null, Saturday, new TimeSpan(21, 0, 0), new TimeSpan(22, 0, 0),
            null, "The parlour", "Ben", capacity, RequiresSignUp: true), Mailer(mail), default);
        var programme = Assert.IsType<HostedEventProgrammeRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.IsType<OkObjectResult>((await HostSide(sqlite).Publish(OrgId, EventId, default)).Result);
        return programme.Sessions.Single().Id;
    }

    private static async Task<PublicSessionRecord> SignUpAsync(SqliteTestDb sqlite, Guid who, Guid sessionId, int people = 1)
    {
        var result = await Guest(sqlite, who).SignUp(EventId, sessionId, new SessionSignUpRequest(people), default);
        var programme = Assert.IsType<PublicProgrammeRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return programme.Sessions.Single(s => s.Id == sessionId);
    }

    [Fact]
    public async Task When_it_is_full_the_next_guest_is_queued_with_their_place_in_the_queue()
    {
        await using var sqlite = await SeedAsync();
        var ovilus = await OvilusAsync(sqlite, Mailbox(), capacity: 2);

        Assert.False((await SignUpAsync(sqlite, Bob, ovilus)).Mine!.Waiting);
        Assert.False((await SignUpAsync(sqlite, Cat, ovilus)).Mine!.Waiting);

        var ann = await SignUpAsync(sqlite, Ann, ovilus);
        Assert.True(ann.Mine!.Waiting);
        Assert.Equal(1, ann.Mine.Position);
        Assert.Equal(2, ann.PlacesTaken);
    }

    [Fact]
    public async Task Somebody_leaving_moves_the_queue_up_and_tells_them()
    {
        await using var sqlite = await SeedAsync();
        var mail = Mailbox();
        var ovilus = await OvilusAsync(sqlite, mail, capacity: 2);
        await SignUpAsync(sqlite, Bob, ovilus);
        await SignUpAsync(sqlite, Cat, ovilus);
        await SignUpAsync(sqlite, Ann, ovilus);

        var left = await Guest(sqlite, Bob).Leave(EventId, ovilus, Mailer(mail), default);
        Assert.IsType<OkObjectResult>(left.Result);

        var annNow = (await Guest(sqlite, Ann).Get(EventId, default)).Result as OkObjectResult;
        var session = Assert.IsType<PublicProgrammeRecord>(annNow!.Value).Sessions.Single();
        Assert.False(session.Mine!.Waiting);
        Assert.Equal(2, session.PlacesTaken);

        var letter = Assert.Single(mail.Sent);
        Assert.Equal("ann@example.test", letter.To);
        Assert.Contains("You're in", letter.Subject);
    }

    [Fact]
    public async Task A_party_at_the_head_of_the_queue_is_not_jumped_by_a_single_person_behind_them()
    {
        await using var sqlite = await SeedAsync();
        var ovilus = await OvilusAsync(sqlite, Mailbox(), capacity: 2);
        await SignUpAsync(sqlite, Bob, ovilus);
        await SignUpAsync(sqlite, Cat, ovilus);
        Assert.True((await SignUpAsync(sqlite, Ann, ovilus, people: 2)).Mine!.Waiting);
        Assert.Equal(2, (await SignUpAsync(sqlite, Eve, ovilus)).Mine!.Position);

        // Bob leaves: one place free. Ann's party of two waits for two, and Eve — one person, behind
        // her — is not let past into the single place.
        await Guest(sqlite, Bob).Leave(EventId, ovilus, Mailer(Mailbox()), default);
        var eve = Assert.IsType<PublicProgrammeRecord>(((OkObjectResult)(await Guest(sqlite, Eve).Get(EventId, default)).Result!).Value).Sessions.Single();
        Assert.True(eve.Mine!.Waiting);
        Assert.Equal(1, eve.PlacesTaken);
    }

    [Fact]
    public async Task A_guest_still_waiting_on_the_venue_cannot_sign_up_and_is_told_why()
    {
        await using var sqlite = await SeedAsync();
        var ovilus = await OvilusAsync(sqlite, Mailbox());

        var result = await Guest(sqlite, Dan).SignUp(EventId, ovilus, new SessionSignUpRequest(1), default);
        var refused = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("once the venue confirms", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task A_session_outside_the_event_is_refused_in_words()
    {
        await using var sqlite = await SeedAsync();
        var result = await HostSide(sqlite).Create(OrgId, EventId, new SaveHostedEventSessionRequest(
            "Breakfast", null, Saturday.AddDays(5), new TimeSpan(8, 0, 0), new TimeSpan(9, 0, 0),
            null, null, null, null, false), Mailer(Mailbox()), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("outside the event", Assert.IsType<string>(refused.Value));
    }

    [Fact]
    public async Task Two_saves_racing_for_the_last_place_cannot_both_win()
    {
        // The counter is the arbiter: whichever save reads "1 left" second finds the row changed.
        await using var sqlite = await SeedAsync();
        var ovilus = await OvilusAsync(sqlite, Mailbox(), capacity: 1);

        await using var first = await sqlite.NewContextAsync();
        await using var second = await sqlite.NewContextAsync();
        var a = await first.HostedEventSessions.SingleAsync(s => s.Id == ovilus);
        var b = await second.HostedEventSessions.SingleAsync(s => s.Id == ovilus);

        a.PlacesTaken += 1;
        await first.SaveChangesAsync();

        b.PlacesTaken += 1;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Moving_a_published_session_tells_everybody_with_a_place_and_rings_the_bell()
    {
        await using var sqlite = await SeedAsync();
        var mail = Mailbox();
        var ovilus = await OvilusAsync(sqlite, mail, capacity: 5);
        await SignUpAsync(sqlite, Bob, ovilus);

        var moved = await HostSide(sqlite).Update(OrgId, EventId, ovilus, new SaveHostedEventSessionRequest(
            "Operating the Ovilus", null, Saturday, new TimeSpan(22, 0, 0), new TimeSpan(23, 0, 0),
            null, "The parlour", "Ben", 5, true), Mailer(mail), default);
        Assert.IsType<OkObjectResult>(moved.Result);

        var letter = Assert.Single(mail.Sent);
        Assert.Equal("bob@example.test", letter.To);
        Assert.Contains("10:00 PM", letter.HtmlBody);

        var bell = As(new NotificationSummaryController(sqlite.Factory, new Mock<IOrganizationSecurityService>().Object), Bob);
        var summary = Assert.IsType<NotificationSummaryResponse>(((OkObjectResult)(await bell.GetSummary(default)).Result!).Value);
        Assert.Equal(1, summary.EventScheduleChanges?.Count);

        await Guest(sqlite, Bob).Seen(EventId, default);
        summary = Assert.IsType<NotificationSummaryResponse>(((OkObjectResult)(await bell.GetSummary(default)).Result!).Value);
        Assert.Equal(0, summary.EventScheduleChanges?.Count ?? 0);
    }

    [Fact]
    public async Task A_session_people_signed_up_for_is_cancelled_not_deleted()
    {
        await using var sqlite = await SeedAsync();
        var mail = Mailbox();
        var ovilus = await OvilusAsync(sqlite, mail, capacity: 1);
        await SignUpAsync(sqlite, Bob, ovilus);
        await SignUpAsync(sqlite, Cat, ovilus);   // waiting

        var deleted = await HostSide(sqlite).Delete(OrgId, EventId, ovilus, default);
        Assert.IsType<ConflictObjectResult>(deleted.Result);

        await HostSide(sqlite).Cancel(OrgId, EventId, ovilus, new("The Ovilus is broken."), Mailer(mail), default);
        Assert.Equal(2, mail.Sent.Count);   // the one with a place AND the one waiting
        Assert.All(mail.Sent, m => Assert.Contains("The Ovilus is broken.", m.HtmlBody));
    }

    [Fact]
    public async Task A_draft_programme_is_nobodys_but_the_hosts()
    {
        await using var sqlite = await SeedAsync();
        await HostSide(sqlite).Create(OrgId, EventId, new SaveHostedEventSessionRequest(
            "Secret séance", null, Saturday, new TimeSpan(23, 0, 0), new TimeSpan(0, 30, 0),
            null, null, null, null, false), Mailer(Mailbox()), default);

        Assert.IsType<NotFoundResult>((await Guest(sqlite, Bob).Get(EventId, default)).Result);
    }

    [Fact]
    public async Task The_calendar_file_is_at_nine_at_the_venue_whatever_the_readers_clock()
    {
        await using var sqlite = await SeedAsync();
        var ovilus = await OvilusAsync(sqlite, Mailbox());

        var file = Assert.IsType<FileContentResult>(await Guest(sqlite, Bob).SessionCalendar(EventId, ovilus, default));
        var ics = System.Text.Encoding.UTF8.GetString(file.FileContents);

        var zone = HostedEventCalendarSync.ZoneOf("America/Chicago");
        var nineUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(Saturday.AddHours(21), DateTimeKind.Unspecified), zone);
        Assert.Contains($"DTSTART:{nineUtc:yyyyMMdd'T'HHmmss'Z'}", ics);
    }
}
