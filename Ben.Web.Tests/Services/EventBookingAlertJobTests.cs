using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The letters about bookings, sent by the jobs against a real database (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>The timing is proved in <c>EventAlertBatchingTests</c>.</b> What these prove is
/// everything around it: that the right people are written to and nobody else, that a person's
/// choice is honoured, that a letter which failed to go is tried again rather than marked as sent,
/// and that the digest keeps quiet when there is nothing to say.</para>
///
/// <para><b>Never a name, never an address.</b> The letter says what and when; who is on the board,
/// behind the permission the board checks. One test reads the letter for the guest's name.</para>
/// </remarks>
public sealed class EventBookingAlertJobTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid StewardId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid OtherGuestId = Guid.NewGuid();

    private static readonly DateTime Now = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private sealed record Mail(List<EmailMessage> Sent, Mock<IEmailService> Service);

    private static Mail Mailbox(bool fails = false)
    {
        var sent = new List<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(true);

        var setup = email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()));
        if (fails)
            setup.ThrowsAsync(new InvalidOperationException("The mail server is having a bad day."));
        else
            setup.Callback<EmailMessage, CancellationToken>((m, _) => sent.Add(m)).Returns(Task.CompletedTask);

        return new Mail(sent, email);
    }

    /// <summary>
    /// The group says yes only to the owner. The steward gets in, if at all, through the event's own
    /// staff list — which is the second half of what is being tested.
    /// </summary>
    private static Ben.Data.WebApi.Services.Access.HostedEventAccess Access()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                OwnerId, It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        security.Setup(s => s.HasAccessAsync(
                It.Is<Guid>(g => g != OwnerId), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new Ben.Data.WebApi.Services.Access.HostedEventAccess(security.Object);
    }

    private static EventOrganizerMailer Mailer(Mail mail)
        => new(mail.Service.Object, Options.Create(new SiteIdentity { BaseUrl = "https://example.test" }));

    private static EventBookingAlertJob AlertJob(SqliteTestDb sqlite, Mail mail)
        => new(sqlite.Factory, Access(), Mailer(mail), NullLogger<EventBookingAlertJob>.Instance);

    private static EventBookingDigestJob DigestJob(SqliteTestDb sqlite, Mail mail)
        => new(sqlite.Factory, Access(), Mailer(mail), NullLogger<EventBookingDigestJob>.Instance);

    private static async Task<SqliteTestDb> SeedAsync(bool stewardDecides = false)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        foreach (var (id, name) in new[]
                 {
                     (OwnerId, "The Owner"), (StewardId, "A Steward"),
                     (GuestId, "Guest Surname"), (OtherGuestId, "Another Guest"),
                 })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test",
                DisplayName = name, DateCreated = Now.AddYears(-1),
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = Now.AddYears(-1), CreatedByAppUserId = OwnerId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = Now.AddYears(-1), CreatedByAppUserId = OwnerId,
        });

        foreach (var member in new[] { OwnerId, StewardId })
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = member, IsActive = true,
                Role = member == OwnerId ? OrganizationMemberRole.Owner : OrganizationMemberRole.Member,
                DateCreated = Now.AddYears(-1), CreatedByAppUserId = OwnerId,
            });
        }

        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = Now.Date.AddDays(30), EndsOn = Now.Date.AddDays(31),
            LifecycleState = HostedEventLifecycleState.Published,
            TimeZoneId = "America/Chicago",
            DateCreated = Now.AddMonths(-1), CreatedByAppUserId = OwnerId,
        });

        // Helping at the door; deciding only when the test says so.
        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, AppUserId = StewardId,
            RunsTheDoor = true, Decides = stewardDecides, DateConfirmed = Now.AddDays(-1),
            DateCreated = Now.AddDays(-1), CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task AskAsync(SqliteTestDb sqlite, Guid leadId, DateTime at, int party = 4)
    {
        await using var db = await sqlite.NewContextAsync();
        db.HostedEventBookings.Add(new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = leadId,
            PartySize = party, Kind = HostedEventBookingKind.DayPass,
            Status = HostedEventBookingStatus.Requested,
            DateCreated = at, CreatedByAppUserId = leadId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task PreferAsync(SqliteTestDb sqlite, Guid userId, EventBookingAlertMode mode)
    {
        await using var db = await sqlite.NewContextAsync();
        db.EventBookingAlertPreferences.Add(new EventBookingAlertPreference
        {
            Id = Guid.NewGuid(), AppUserId = userId, OrganizationId = OrgId, Mode = mode,
            DateCreated = Now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    private static string AddressOf(Guid id) => $"{id:N}@example.test";

    // ── the alert ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_quick_requests_are_one_letter_to_the_person_who_decides()
    {
        // The plan's "verified by", against a real database and the real job.
        await using var sqlite = await SeedAsync();
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));
        await AskAsync(sqlite, OtherGuestId, Now.AddMinutes(-1));

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now, default);

        var letter = Assert.Single(mail.Sent);
        Assert.Equal(AddressOf(OwnerId), letter.To);
        Assert.Contains("2 new bookings", letter.Subject);
    }

    [Fact]
    public async Task Running_again_straight_away_sends_nothing_more()
    {
        // The cursor is what stops a five-minute job repeating itself every five minutes.
        await using var sqlite = await SeedAsync();
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));

        var mail = Mailbox();
        var job = AlertJob(sqlite, mail);

        await job.RunAtAsync(Now, default);
        await job.RunAtAsync(Now.AddMinutes(5), default);
        await job.RunAtAsync(Now.AddMinutes(30), default);

        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task A_steward_handed_only_the_door_is_not_written_to_about_requests()
    {
        // They cannot answer them, so a letter would be a queue they are nagged about and may not
        // touch — and a copy of it in an inbox the board would never have let them read.
        await using var sqlite = await SeedAsync(stewardDecides: false);
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now, default);

        Assert.DoesNotContain(mail.Sent, m => m.To == AddressOf(StewardId));
    }

    [Fact]
    public async Task A_steward_who_decides_is_written_to_like_anybody_who_decides()
    {
        await using var sqlite = await SeedAsync(stewardDecides: true);
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now, default);

        Assert.Contains(mail.Sent, m => m.To == AddressOf(StewardId));
    }

    [Fact]
    public async Task The_letter_says_what_and_when_and_never_who()
    {
        // A letter is forwarded, printed and left open on a shared desk in a way a screen is not.
        // The guest's name belongs on the board, behind the permission the board checks.
        await using var sqlite = await SeedAsync();
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2), party: 4);

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now, default);

        var letter = Assert.Single(mail.Sent);
        Assert.Contains("A party of 4", letter.HtmlBody);
        Assert.DoesNotContain("Guest Surname", letter.HtmlBody);
        Assert.DoesNotContain(AddressOf(GuestId), letter.HtmlBody);
        Assert.Contains("/bookings", letter.HtmlBody);
    }

    // ── what somebody chose ──────────────────────────────────────────────────

    [Fact]
    public async Task Somebody_who_chose_the_digest_is_not_sent_the_alert()
    {
        await using var sqlite = await SeedAsync();
        await PreferAsync(sqlite, OwnerId, EventBookingAlertMode.DigestOnly);
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now, default);

        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Somebody_who_switched_it_off_is_sent_neither()
    {
        await using var sqlite = await SeedAsync();
        await PreferAsync(sqlite, OwnerId, EventBookingAlertMode.Off);
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now, default);
        await DigestJob(sqlite, mail).RunAtAsync(Now, default);

        Assert.Empty(mail.Sent);
    }

    // ── when the post fails ──────────────────────────────────────────────────

    [Fact]
    public async Task A_letter_that_failed_to_go_is_tried_again_rather_than_marked_as_sent()
    {
        // The silence this whole phase exists to prevent, by the most ordinary route: the mail
        // server has a bad afternoon and the job decides everybody was told.
        await using var sqlite = await SeedAsync();
        await AskAsync(sqlite, GuestId, Now.AddMinutes(-2));

        await AlertJob(sqlite, Mailbox(fails: true)).RunAtAsync(Now, default);

        var mail = Mailbox();
        await AlertJob(sqlite, mail).RunAtAsync(Now.AddMinutes(5), default);

        Assert.Single(mail.Sent);
    }

    // ── the digest ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_quiet_event_sends_no_digest()
    {
        // "Nothing new" every morning is the letter that trains somebody to stop opening them.
        await using var sqlite = await SeedAsync();

        var mail = Mailbox();
        await DigestJob(sqlite, mail).RunAtAsync(Now, default);

        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task A_waiting_request_is_in_the_digest_once_a_day_and_not_every_pass()
    {
        await using var sqlite = await SeedAsync();
        await AskAsync(sqlite, GuestId, Now.AddDays(-3));

        var mail = Mailbox();
        var job = DigestJob(sqlite, mail);

        await job.RunAtAsync(Now, default);
        await job.RunAtAsync(Now.AddHours(1), default);
        await job.RunAtAsync(Now.AddHours(20), default);
        await job.RunAtAsync(Now.AddDays(1), default);

        Assert.Equal(2, mail.Sent.Count);
        Assert.Contains("Waiting on an answer", mail.Sent[0].HtmlBody);
        Assert.Contains("3 days", mail.Sent[0].HtmlBody);
    }
}
