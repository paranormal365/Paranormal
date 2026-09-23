using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A published template's tokens are filled in on the letter that actually goes out.
/// </summary>
/// <remarks>
/// <para><b>What production would have done.</b> On 2026-09-23 seven templates were published on
/// IsHauntedDb for letters whose senders handed a template nothing — and a table token with no row
/// renders as an empty string. None had been sent yet. These drive each of those letters through
/// its real sender and the real outbox, with a template of the same shape published, and read what
/// was queued: every name, date and link has to be there.</para>
///
/// <para>The template is the author's; the outbox applies it at queue time, exactly as it does on
/// the live site (<see cref="TestOutbox"/>).</para>
/// </remarks>
public sealed class APublishedTemplateFillsInTests
{
    private static readonly Guid HostId   = Guid.Parse("7e1a0000-0000-0000-0000-000000000001");
    private static readonly Guid GuestId  = Guid.Parse("7e1a0000-0000-0000-0000-000000000002");
    private static readonly Guid ClientId = Guid.Parse("7e1a0000-0000-0000-0000-000000000003");
    private static readonly Guid OrgId    = Guid.Parse("7e1a0000-0000-0000-0000-000000000004");
    private static readonly Guid PlaceId  = Guid.Parse("7e1a0000-0000-0000-0000-000000000005");
    private static readonly Guid EventId  = Guid.Parse("7e1a0000-0000-0000-0000-000000000006");
    private static readonly Guid CaseId   = Guid.Parse("7e1a0000-0000-0000-0000-000000000007");

    private static readonly IOptions<SiteIdentity> Site =
        Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });

    // ── the organizer's "bookings have arrived" ──────────────────────────────

    [Fact]
    public async Task Bookings_arrived_names_the_organizer_the_group_and_the_events_own_day()
    {
        await using var sqlite = await SeedAsync();
        await PublishAsync(sqlite, MailKinds.BookingsArrived,
            "<p>Hello {AppUsers.DisplayName}. A booking for {Organizations.Name}, starting {HostedEvents.StartsOn}.</p>");

        await using (var db = await sqlite.NewContextAsync())
        {
            var ev = await db.HostedEvents.Include(e => e.Organization).SingleAsync(e => e.Id == EventId);
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, HostedEvent = ev, LeadAppUserId = GuestId,
                PartySize = 2, Kind = HostedEventBookingKind.DayPass, Status = HostedEventBookingStatus.Requested,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
            };

            Assert.True(await new EventOrganizerMailer(TestOutbox.Real(sqlite.Factory), Site).SendArrivalsAsync(
                new EventBookingRecipients.Recipient(HostId, "host@example.test", "Hana Host", EventBookingAlertMode.AsItHappens),
                ev, [booking], summary: false, default));
        }

        var body = await OnlyLetterAsync(sqlite, MailKinds.BookingsArrived);
        Assert.Contains("Hello Hana Host.", body);
        Assert.Contains("A booking for The Thomas House", body);
        // StartsOn is a calendar date: it used to print the evening before, in Chicago.
        Assert.Contains("starting October 30, 2026.", body);
    }

    // ── the organizer's "your event was removed" (key guest-removed, but it goes to the organizers) ──

    [Fact]
    public async Task Guest_removed_names_the_organizer_the_event_the_group_and_when()
    {
        await using var sqlite = await SeedAsync();
        await PublishAsync(sqlite, MailKinds.GuestRemoved,
            "<p>Hello {AppUsers.DisplayName}. {HostedEvents.Name}, from {Organizations.Name}, was removed on {HostedEvents.CancelledAtUtc}.</p>");

        await using (var db = await sqlite.NewContextAsync())
        {
            var ev = await db.HostedEvents.Include(e => e.Organization).SingleAsync(e => e.Id == EventId);
            ev.CancelledAtUtc = new DateTime(2026, 10, 1, 15, 30, 0);

            var mailer = new EventGuestMailer(TestOutbox.Real(sqlite.Factory), Site,
                NullLogger<EventGuestMailer>.Instance, TestOutbox.Real(sqlite.Factory));
            Assert.Equal(1, await mailer.SendRemovedAsync(ev, ev.Organization.Name, creditReturned: false,
                [(GuestId, "grace@example.test", "Grace Organizer")], default));
        }

        var body = await OnlyLetterAsync(sqlite, MailKinds.GuestRemoved);
        Assert.Contains("Hello Grace Organizer.", body);
        Assert.Contains("Halloween Lock-In, from The Thomas House, was removed on October 1, 2026 10:30 AM.", body);
    }

    /// <summary>
    /// The appeal and the returned credit are what only the sender knows, and the built-in letter
    /// carries both — a template that could not would be a worse letter than the one it replaces.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Guest_removed_hands_over_the_appeal_and_whether_the_credit_came_back(bool creditReturned)
    {
        await using var sqlite = await SeedAsync();
        await PublishAsync(sqlite, MailKinds.GuestRemoved,
            "<p>Removed.</p><p>[{CreditNote}]</p>{AppealButton}<p>{AppealUrl}</p>");

        await using (var db = await sqlite.NewContextAsync())
        {
            var ev = await db.HostedEvents.Include(e => e.Organization).SingleAsync(e => e.Id == EventId);
            var mailer = new EventGuestMailer(TestOutbox.Real(sqlite.Factory), Site,
                NullLogger<EventGuestMailer>.Instance, TestOutbox.Real(sqlite.Factory));
            Assert.Equal(1, await mailer.SendRemovedAsync(ev, ev.Organization.Name, creditReturned,
                [(GuestId, "grace@example.test", "Grace Organizer")], default));
        }

        var body = await OnlyLetterAsync(sqlite, MailKinds.GuestRemoved);
        var appeal = $"/organizations/{OrgId}/events/{EventId}#event-removed";
        Assert.Contains(appeal + "\"", body);                 // the button's href
        Assert.Contains(appeal + "</p>", body);               // the bare link
        Assert.Contains(">Appeal this decision<", body);
        if (creditReturned)
            Assert.Contains("[The event credit spent on it has been returned, and can be used for another event.]", body);
        else
            Assert.Contains("[]", body);
    }

    // ── a client's case letters ──────────────────────────────────────────────

    [Fact]
    public async Task A_case_status_letter_names_the_client_the_case_and_the_group()
    {
        await using var sqlite = await SeedAsync();
        await PublishAsync(sqlite, MailKinds.CaseStatusChanged,
            "<p>Hello {AppUsers.DisplayName}. {Cases.Title}, with {Organizations.Name}, has moved on.</p>");

        await using (var db = await sqlite.NewContextAsync())
        {
            var c = await db.Cases.SingleAsync(x => x.Id == CaseId);
            c.Status = CaseStatus.Active;
            await ClientMailer(sqlite).CaseStatusChangedAsync(db, c, CaseStatus.Accepted, default);
        }

        var body = await OnlyLetterAsync(sqlite, MailKinds.CaseStatusChanged);
        Assert.Contains("Hello Carol Client. Belmont Boulevard house, with The Thomas House, has moved on.", body);
    }

    [Fact]
    public async Task A_visit_letter_says_when_on_the_sites_own_clock()
    {
        await using var sqlite = await SeedAsync();
        await PublishAsync(sqlite, MailKinds.VisitScheduled,
            "<p>Hello {AppUsers.DisplayName}. {Organizations.Name} will be there {Investigations.ScheduledDateTime}.</p>");

        await using (var db = await sqlite.NewContextAsync())
        {
            var c = await db.Cases.SingleAsync(x => x.Id == CaseId);
            var visit = new Investigation
            {
                Id = Guid.NewGuid(), CaseId = CaseId,
                // A UTC instant — 1am on the 5th in UTC is 8pm on the 4th in Chicago.
                ScheduledDateTime = new DateTime(2026, 10, 5, 1, 0, 0),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            };
            await ClientMailer(sqlite).VisitScheduledAsync(db, c, visit, default);
        }

        var body = await OnlyLetterAsync(sqlite, MailKinds.VisitScheduled);
        Assert.Contains("Hello Carol Client. The Thomas House will be there October 4, 2026 8:00 PM.", body);
    }

    // ── a request made under somebody's address ──────────────────────────────

    /// <summary>
    /// With the sign-up warning's template published — no link in it, as on production — the
    /// request letter still carries the one link that claims the request.
    /// </summary>
    /// <remarks>
    /// Both letters used to share one kind, so that template replaced this letter too and the claim
    /// link vanished. It now has a kind of its own, whose link a template is required to carry.
    /// </remarks>
    [Fact]
    public async Task A_request_under_your_address_keeps_its_claim_link_whatever_the_warning_says()
    {
        await using var sqlite = await SeedAsync();
        await PublishAsync(sqlite, MailKinds.SomebodyUsedYourAddress,
            "<p>Hello {AppUsers.DisplayName}. Somebody used your address on {SiteName}.</p>");

        const string claim = "https://test.local/requests/adopt/0f1e2d3c";
        await using (var db = await sqlite.NewContextAsync())
        {
            var holder = await db.AppUsers.SingleAsync(u => u.Id == ClientId);
            var accounts = new AccountCreationService(
                userManager: null!, handles: null!, new Mock<IConfirmationMailer>().Object,
                TestOutbox.Real(sqlite.Factory), Site, new ConfigurationBuilder().Build(),
                NullLogger<AccountCreationService>.Instance);

            await accounts.TellHolderAboutPendingRequestAsync(holder, "13 Journey Lane", claim, default);
        }

        // Whatever kind it was filed under: the old shared kind is exactly the failure.
        var body = await OnlyLetterAsync(sqlite, kind: null);
        Assert.Contains(claim, body);
        Assert.Contains("13 Journey Lane", body);
        Assert.DoesNotContain("Somebody used your address", body);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static ClientStatusMailer ClientMailer(SqliteTestDb sqlite)
        => new(TestOutbox.Real(sqlite.Factory), Site, NullLogger<ClientStatusMailer>.Instance);

    /// <summary>Publishes a template as the editor would: written, then made live.</summary>
    private static async Task PublishAsync(SqliteTestDb sqlite, MailKindInfo kind, string body)
    {
        await using var db = await sqlite.NewContextAsync();
        db.EmailTemplates.Add(new EmailTemplate
        {
            Id = Guid.NewGuid(), Kind = kind.Key,
            Subject = "A letter from {SiteName}", BodyHtml = body, PublishedUtc = DateTime.UtcNow,
            DraftSubject = "A letter from {SiteName}", DraftBodyHtml = body, DraftSavedUtc = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The one letter of this kind that was queued, as it was written.</summary>
    private static async Task<string> OnlyLetterAsync(SqliteTestDb sqlite, MailKindInfo? kind)
    {
        await using var db = await sqlite.NewContextAsync();
        var letter = Assert.Single(await db.OutboxEmails.Where(o => kind == null || o.Kind == kind.Key).ToListAsync());
        return letter.HtmlBody ?? "";
    }

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name, email) in new[]
                 {
                     (HostId, "Hana Host", "host@example.test"),
                     (GuestId, "Grace Guest", "grace@example.test"),
                     (ClientId, "Carol Client", "carol@example.test"),
                 })
            db.Users.Add(new AppUser
            {
                Id = id, DisplayName = name, Email = email, UserName = email, EmailConfirmed = true, DateCreated = now,
            });

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house-templates",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in-templates",
            StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            LifecycleState = HostedEventLifecycleState.Published, DayPassCapacity = 10,
            TimeZoneId = "America/Chicago", DateCreated = now, CreatedByAppUserId = HostId,
        });

        var request = new ClientRequest
        {
            Id = Guid.NewGuid(), AppUserId = ClientId, City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", StreetAddress1 = "1 Main", Description = "Footsteps upstairs.",
            Status = ClientRequestStatus.Assigned, DateCreated = now, CreatedByAppUserId = ClientId,
        };
        db.ClientRequests.Add(request);
        db.Cases.Add(new Case
        {
            Id = CaseId, OrganizationId = OrgId, ClientRequestId = request.Id,
            Title = "Belmont Boulevard house", CaseYear = now.Year, OrgCaseNumber = 1, Status = CaseStatus.Accepted,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCaseOpened = now, DateCreated = now, CreatedByAppUserId = ClientId,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }
}
