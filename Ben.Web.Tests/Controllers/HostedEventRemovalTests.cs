using System.Security.Claims;
using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Web.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// IsHaunted removing a hosted event, and the organizer's appeal (item 235 phase 17b).
/// </summary>
/// <remarks>
/// The rules that would hurt somebody if they broke: the credit always comes back, the guests' passes stop working,
/// the organizer's letter never carries the reviewer's private note, a removed event has no way back except an upheld
/// appeal, and an upheld appeal brings it back as a draft rather than onto the site.
/// </remarks>
public sealed class HostedEventRemovalTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid CreditId = Guid.NewGuid();
    private static readonly Guid PassId = Guid.NewGuid();
    private static readonly Guid SuperAdminId = Guid.NewGuid();
    private static readonly Guid OrganizerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();

    private const string PrivateNote = "Reported twice for selling tickets to a trespass at the old asylum.";

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (SuperAdminId, "Sue Admin"), (OrganizerId, "Olive Organizer"), (MemberId, "Mo Member"), (GuestId, "Gus Guest") })
            db.Users.Add(new AppUser { Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test", DisplayName = name, DateCreated = now });

        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin, NormalizedName = RoleNames.SuperAdmin.ToUpperInvariant() };
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<Guid> { RoleId = role.Id, UserId = SuperAdminId });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "Night Watch", UrlName = "night-watch", DateCreated = now, CreatedByAppUserId = OrganizerId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Old Mill", DateCreated = now, CreatedByAppUserId = OrganizerId });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId, Name = "Mill Lock-In", UrlName = "mill-lock-in",
            StartsOn = now.Date.AddDays(1), EndsOn = now.Date.AddDays(1), LifecycleState = HostedEventLifecycleState.Published,
            FirstPublishedUtc = now.AddDays(-3), ContactLine = "Call us.", DateCreated = now, CreatedByAppUserId = OrganizerId,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = EventId, Date = now.Date.AddDays(1), DateCreated = now, CreatedByAppUserId = OrganizerId });
        // Spent the day before: well inside the organizer's own 48-hour window, which removal ignores.
        db.EventCredits.Add(new EventCredit
        {
            Id = CreditId, OwnerOrganizationId = OrgId, PriceAtPurchase = 99m, PurchasedUtc = now.AddMonths(-1),
            ExpiresUtc = now.AddMonths(11), SpentUtc = now.AddDays(-3), SpentOnHostedEventId = EventId,
            DateCreated = now, CreatedByAppUserId = OrganizerId,
        });
        var bookingId = Guid.NewGuid();
        db.HostedEventBookings.Add(new HostedEventBooking
        {
            Id = bookingId, HostedEventId = EventId, LeadAppUserId = GuestId, PartySize = 3,
            Kind = HostedEventBookingKind.DayPass, Status = HostedEventBookingStatus.Confirmed,
            DateCreated = now, CreatedByAppUserId = GuestId,
        });
        db.HostedEventPasses.Add(new HostedEventPass
        {
            Id = PassId, HostedEventBookingId = bookingId, Token = Guid.NewGuid().ToString("N"),
            IssuedUtc = now, DateCreated = now, CreatedByAppUserId = OrganizerId,
        });
        await db.SaveChangesAsync();
        return sqlite;
    }

    private static ControllerContext As(Guid who, bool superAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, who.ToString()) };
        if (superAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
                RequestServices = new ServiceCollection().AddSingleton<IAuditLogService, StrictAudit>().BuildServiceProvider(),
            },
        };
    }

    /// <summary>
    /// Compares the two pictures the way the real audit log does, and so refuses two different types — the mistake that
    /// made the appeal answer 500 after it had already saved, which only the e2e run caught.
    /// </summary>
    private sealed class StrictAudit : IAuditLogService
    {
        public Task LogCreateAsync(string entityType, Guid entityId, object entity, Guid userId, string source)
        {
            Ben.Data.Common.Helpers.AuditChangeTracker.ToPropertySnapshot(entity);
            return Task.CompletedTask;
        }

        public Task LogUpdateAsync(string entityType, Guid entityId, object before, object after, Guid userId, string source)
        {
            Ben.Data.Common.Helpers.AuditChangeTracker.GetChanges(before, after);
            return Task.CompletedTask;
        }

        public Task LogDeleteAsync(string entityType, Guid entityId, object entity, Guid userId, string source) => Task.CompletedTask;
    }

    private sealed class Mailbox
    {
        public List<EmailMessage> Sent { get; } = [];
        public IEmailService Service
        {
            get
            {
                var email = new Mock<IEmailService>();
                email.SetupGet(e => e.IsConfigured).Returns(true);
                email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                    .Callback((EmailMessage m, CancellationToken _) => Sent.Add(m)).Returns(Task.CompletedTask);
                return email.Object;
            }
        }
    }

    private static EventGuestMailer Mailer(IEmailService email)
        => new(email, Options.Create(new Ben.Data.Common.SiteIdentity { Name = "IsHaunted", BaseUrl = "https://test.local" }),
            NullLogger<EventGuestMailer>.Instance);

    private static AdminHostedEventController Admin(SqliteTestDb sqlite, Mailbox mail)
        => new(sqlite.Factory, new HostedEventCalendarSync(), Mailer(mail.Service), new PlatformMessageService(sqlite.Factory))
        { ControllerContext = As(SuperAdminId, superAdmin: true) };

    private static HostedEventRemovalController Organizer(SqliteTestDb sqlite, Guid who)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) => user == OrganizerId);
        return new HostedEventRemovalController(sqlite.Factory, new Mock<IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object), new PlatformMessageService(sqlite.Factory))
        { ControllerContext = As(who) };
    }

    private static HostedEventController Events(SqliteTestDb sqlite)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sanitizer = new Mock<ICmsMarkupSanitizer>();
        sanitizer.Setup(s => s.SanitizeHtml(It.IsAny<string>())).Returns<string>(h => h);
        return new HostedEventController(sqlite.Factory, new Mock<IMapper>().Object, security.Object, sanitizer.Object,
            new HostedEventCalendarSync(), new HostedEventEntitlement(new SubscriptionLimitGuard(sqlite.Factory)),
            new SiteSettingsService(sqlite.Factory))
        { ControllerContext = As(OrganizerId) };
    }

    private static async Task<AdminHostedEventsRecord> RemoveAsync(SqliteTestDb sqlite, Mailbox mail)
        => Assert.IsType<AdminHostedEventsRecord>(Assert.IsType<OkObjectResult>(
            (await Admin(sqlite, mail).Remove(EventId, new RemoveHostedEventRequest(PrivateNote), default)).Result).Value);

    // ── removing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Removing_takes_it_off_the_site_returns_the_credit_whatever_the_timing_and_stops_the_passes()
    {
        await using var sqlite = await SeedAsync();
        var mail = new Mailbox();

        var effect = Assert.IsType<AdminHostedEventRemovalEffect>(Assert.IsType<OkObjectResult>(
            (await Admin(sqlite, mail).RemovalEffect(EventId, default)).Result).Value);
        Assert.Equal(new AdminHostedEventRemovalEffect(false, true, 1, 3), effect);

        var result = await RemoveAsync(sqlite, mail);
        Assert.Contains("credit went back", result.Note);

        await using var db = await sqlite.NewContextAsync();
        var hosted = await db.HostedEvents.SingleAsync(e => e.Id == EventId);
        Assert.Equal(HostedEventLifecycleState.Removed, hosted.LifecycleState);
        Assert.False(hosted.IsOnThePublicSite);
        Assert.Null(hosted.FirstPublishedUtc);
        Assert.Null(hosted.CancelledReason);

        var credit = await db.EventCredits.SingleAsync(c => c.Id == CreditId);
        Assert.Null(credit.SpentOnHostedEventId);
        Assert.NotNull((await db.HostedEventPasses.SingleAsync(p => p.Id == PassId)).RevokedUtc);

        var removal = await db.HostedEventRemovals.SingleAsync();
        Assert.Equal((HostedEventLifecycleState.Published, PrivateNote, true, 1),
            (removal.PreviousState, removal.Note, removal.CreditReturned, removal.GuestsTold));

        Assert.IsType<ConflictObjectResult>((await Admin(sqlite, mail).Remove(EventId, new(null), default)).Result);
    }

    [Fact]
    public async Task The_organizers_letter_is_generic_offers_the_appeal_and_never_carries_the_private_note()
    {
        await using var sqlite = await SeedAsync();
        var mail = new Mailbox();

        await RemoveAsync(sqlite, mail);

        var guest = Assert.Single(mail.Sent, m => m.Subject.Contains("not going ahead"));
        var organizer = Assert.Single(mail.Sent, m => m.Subject.Contains("was removed"));
        Assert.Contains("doesn't meet our guidelines", organizer.HtmlBody);
        Assert.Contains($"/organizations/{OrgId}/events/{EventId}#event-removed", organizer.HtmlBody);
        Assert.Contains("credit spent on it has been returned", organizer.HtmlBody);
        Assert.All(mail.Sent, m => Assert.DoesNotContain("asylum", m.HtmlBody));
        Assert.DoesNotContain("guidelines", guest.HtmlBody);
    }

    [Fact]
    public async Task A_removed_event_cannot_be_restored_uncancelled_or_published_from_its_own_page()
    {
        await using var sqlite = await SeedAsync();
        await RemoveAsync(sqlite, new Mailbox());

        var events = Events(sqlite);
        Assert.IsType<BadRequestObjectResult>((await events.Restore(OrgId, EventId, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await events.Archive(OrgId, EventId, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await events.Uncancel(OrgId, EventId, default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(HostedEventLifecycleState.Removed, (await db.HostedEvents.SingleAsync(e => e.Id == EventId)).LifecycleState);
    }

    [Fact]
    public async Task Restoring_brings_back_only_an_archived_event_never_a_called_off_one()
    {
        await using var sqlite = await SeedAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.SingleAsync(e => e.Id == EventId);
            hosted.LifecycleState = HostedEventLifecycleState.Cancelled;
            await db.SaveChangesAsync();
        }

        await Events(sqlite).Restore(OrgId, EventId, default);

        await using var check = await sqlite.NewContextAsync();
        Assert.Equal(HostedEventLifecycleState.Cancelled, (await check.HostedEvents.SingleAsync(e => e.Id == EventId)).LifecycleState);
    }

    // ── appealing ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_organizer_appeals_once_and_the_site_is_told_but_a_plain_member_may_not()
    {
        await using var sqlite = await SeedAsync();
        await RemoveAsync(sqlite, new Mailbox());

        Assert.IsType<ForbidResult>((await Organizer(sqlite, MemberId).Appeal(OrgId, EventId, new("Please"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Organizer(sqlite, OrganizerId).Appeal(OrgId, EventId, new("  "), default)).Result);

        var appealed = Assert.IsType<HostedEventRemovalRecord>(Assert.IsType<OkObjectResult>(
            (await Organizer(sqlite, OrganizerId).Appeal(OrgId, EventId, new("We have written permission from the owner."), default)).Result).Value);
        Assert.Equal(HostedEventAppealState.Waiting, appealed.AppealState);

        var again = Assert.IsType<BadRequestObjectResult>((await Organizer(sqlite, OrganizerId).Appeal(OrgId, EventId, new("Again"), default)).Result);
        Assert.Contains("already appealed", (string)again.Value!);

        var read = Assert.IsType<HostedEventRemovalRecord>(Assert.IsType<OkObjectResult>((await Organizer(sqlite, OrganizerId).Get(OrgId, EventId, default)).Result).Value);
        Assert.True(read.CreditReturned);

        await using var db = await sqlite.NewContextAsync();
        Assert.Contains(await db.UserMessageTos.Select(t => t.ToAppUserId).ToListAsync(), id => id == SuperAdminId);
    }

    [Fact]
    public async Task Upholding_brings_the_event_back_as_a_draft_and_declining_needs_a_reason()
    {
        await using var sqlite = await SeedAsync();
        var mail = new Mailbox();
        await RemoveAsync(sqlite, mail);
        await Organizer(sqlite, OrganizerId).Appeal(OrgId, EventId, new("We have written permission."), default);

        Guid removalId;
        await using (var db = await sqlite.NewContextAsync())
            removalId = (await db.HostedEventRemovals.SingleAsync()).Id;

        Assert.IsType<BadRequestObjectResult>((await Admin(sqlite, mail).Decide(removalId, new(false, null), default)).Result);

        var upheld = Assert.IsType<AdminHostedEventsRecord>(Assert.IsType<OkObjectResult>(
            (await Admin(sqlite, mail).Decide(removalId, new(true, "Thanks for the letter."), default)).Result).Value);
        Assert.Contains("back as a draft", upheld.Note);
        Assert.Contains(mail.Sent, m => m.Subject.Contains("back as a draft") && m.HtmlBody.Contains("Thanks for the letter."));

        Assert.IsType<BadRequestObjectResult>((await Admin(sqlite, mail).Decide(removalId, new(false, "Changed my mind"), default)).Result);

        await using var check = await sqlite.NewContextAsync();
        var hosted = await check.HostedEvents.SingleAsync(e => e.Id == EventId);
        Assert.Equal(HostedEventLifecycleState.Draft, hosted.LifecycleState);
        Assert.Null(hosted.CancelledAtUtc);
        Assert.Equal(HostedEventAppealState.Upheld, (await check.HostedEventRemovals.SingleAsync()).AppealState);
    }

    [Fact]
    public async Task A_declined_appeal_leaves_it_removed_and_the_list_shows_the_answer()
    {
        await using var sqlite = await SeedAsync();
        var mail = new Mailbox();
        await RemoveAsync(sqlite, mail);
        await Organizer(sqlite, OrganizerId).Appeal(OrgId, EventId, new("It's fine."), default);

        var list = Assert.IsType<AdminHostedEventsRecord>(Assert.IsType<OkObjectResult>((await Admin(sqlite, mail).Get(default)).Result).Value);
        var appeal = Assert.Single(list.Appeals);
        Assert.Equal((HostedEventAppealState.Waiting, PrivateNote), (appeal.AppealState, appeal.RemovalNote));
        Assert.Equal(HostedEventAppealState.Waiting, Assert.Single(list.Events).AppealState);

        await Admin(sqlite, mail).Decide(appeal.RemovalId, new(false, "The owner says otherwise."), default);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(HostedEventLifecycleState.Removed, (await db.HostedEvents.SingleAsync(e => e.Id == EventId)).LifecycleState);
        var read = Assert.IsType<HostedEventRemovalRecord>(Assert.IsType<OkObjectResult>((await Organizer(sqlite, OrganizerId).Get(OrgId, EventId, default)).Result).Value);
        Assert.Equal((HostedEventAppealState.Declined, "The owner says otherwise."), (read.AppealState, read.DecisionNote));
    }

    [Fact]
    public async Task The_dashboard_counts_events_by_state_and_the_waiting_appeal()
    {
        await using var sqlite = await SeedAsync();
        await RemoveAsync(sqlite, new Mailbox());
        await Organizer(sqlite, OrganizerId).Appeal(OrgId, EventId, new("Please look again."), default);

        await using var db = await sqlite.NewContextAsync();
        var stats = await HostedEventOversightStats.ReadAsync(db, 30, DateTime.UtcNow, 8, default);

        Assert.Equal((0, 1, 1, 1), (stats.OnTheSite, stats.CalledOff, stats.Organizers, stats.AppealsWaiting));
        Assert.Equal("Removed", Assert.Single(stats.EventsByState).Label);
        Assert.Equal(1, stats.CreditsHeld);
        Assert.Equal(30, stats.BookingsPerDay.Count);
    }
}
