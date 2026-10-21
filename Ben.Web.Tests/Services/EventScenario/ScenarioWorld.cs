using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// Jim's overnight conference at The Factory (Ben's scenario, 10/21/2026), as a database: Jim's group, the
/// venue, one event over two nights that has just ended, its umbrella calendar row, and three parties —
/// one the door checked in, one it never saw, and one still asking.
/// </summary>
internal sealed class ScenarioWorld : IAsyncDisposable
{
    public readonly SqliteTestDb Sqlite;

    public static readonly Guid Jim = Guid.NewGuid();
    public static readonly Guid Nephew = Guid.NewGuid();     // runs the door, nothing else
    public static readonly Guid Came = Guid.NewGuid();       // confirmed, checked in
    public static readonly Guid CameGuest = Guid.NewGuid();  // named on Came's booking
    public static readonly Guid Missed = Guid.NewGuid();     // confirmed, never checked in
    public static readonly Guid Asked = Guid.NewGuid();      // still asking
    public static readonly Guid Stranger = Guid.NewGuid();

    public readonly Guid OrgId = Guid.NewGuid();
    public readonly Guid PlaceId = Guid.NewGuid();
    public readonly Guid EventId = Guid.NewGuid();
    public readonly Guid UmbrellaId = Guid.NewGuid();
    public readonly Guid FirstNightId = Guid.NewGuid();
    public readonly Guid CameBookingId = Guid.NewGuid();
    public readonly Guid MissedBookingId = Guid.NewGuid();
    public readonly Guid AskedBookingId = Guid.NewGuid();

    /// <summary>The run: two days before today, so a test means the same next year.</summary>
    public static readonly DateTime FirstDay = DateTime.UtcNow.Date.AddDays(-3);
    public static readonly DateTime LastDay = FirstDay.AddDays(1);

    /// <summary>When the last night ends on the venue's clock, in UTC.</summary>
    public static readonly DateTime EndsUtc = HostedEventCalendarSync.Window(
        new HostedEvent { TimeZoneId = "America/Chicago", StartsOn = FirstDay, EndsOn = LastDay }, []).EndUtc;

    private ScenarioWorld(SqliteTestDb sqlite) { Sqlite = sqlite; }

    public static async Task<ScenarioWorld> CreateAsync(
        HostedEventLifecycleState state = HostedEventLifecycleState.Ended, bool checkedIn = true)
    {
        var w = new ScenarioWorld(await SqliteTestDb.CreateAsync());
        await using var db = await w.Sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (Jim, "Jim"), (Nephew, "Nephew"), (Came, "Came"), (CameGuest, "Guest"),
                                           (Missed, "Missed"), (Asked, "Asked"), (Stranger, "Stranger") })
            db.Users.Add(new AppUser { Id = id, Email = $"{name.ToLowerInvariant()}@example.test", UserName = $"{name.ToLowerInvariant()}@example.test", DisplayName = name, DateCreated = now });

        // The evidence file type every submission is stored under (SQLite keeps the foreign key).
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = new Guid("20000000-0000-0000-0000-000000000001"), Name = "Evidence", IsActive = true, AllowAllExtensions = true, DateCreated = now, CreatedByAppUserId = Jim,
        });
        db.Organizations.Add(new Organization { Id = w.OrgId, Name = "Jim's Investigations", UrlName = "jims-investigations", DateCreated = now, CreatedByAppUserId = Jim });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrgId, AppUserId = Jim, Role = OrganizationMemberRole.Owner,
            IsActive = true, DateCreated = now, CreatedByAppUserId = Jim,
        });
        db.Places.Add(new Place { Id = w.PlaceId, Name = "The Factory", City = "Franklin", State = "TN", Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = Jim });

        db.HostedEvents.Add(new HostedEvent
        {
            Id = w.EventId, OrganizationId = w.OrgId, PlaceId = w.PlaceId, Name = "Overnight at The Factory",
            UrlName = "overnight-at-the-factory", TimeZoneId = "America/Chicago", StartsOn = FirstDay, EndsOn = LastDay,
            LifecycleState = state, CollectsEvidence = true, DayPassCapacity = 40, DayPassPrice = 45,
            DateCreated = now, CreatedByAppUserId = Jim,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = w.FirstNightId, HostedEventId = w.EventId, Date = FirstDay, DateCreated = now, CreatedByAppUserId = Jim });
        db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = w.EventId, Date = LastDay, SortOrder = 1, DateCreated = now, CreatedByAppUserId = Jim });

        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = w.UmbrellaId, OrganizationId = w.OrgId, HostedEventId = w.EventId, PlaceId = w.PlaceId,
            Title = "Overnight at The Factory", IsPublic = true,
            StartDateTime = FirstDay.AddHours(19), EndDateTime = EndsUtc, DateCreated = now, CreatedByAppUserId = Jim,
        });

        var came = new HostedEventBooking
        {
            Id = w.CameBookingId, HostedEventId = w.EventId, LeadAppUserId = Came, PartySize = 2,
            Kind = HostedEventBookingKind.DayPass, Status = HostedEventBookingStatus.Confirmed, DateCreated = now, CreatedByAppUserId = Came,
        };
        came.Guests.Add(new HostedEventBookingGuest { Id = Guid.NewGuid(), HostedEventBookingId = came.Id, DisplayName = "Guest", AppUserId = CameGuest, DateCreated = now });
        db.HostedEventBookings.Add(came);
        db.HostedEventBookings.Add(new HostedEventBooking
        {
            Id = w.MissedBookingId, HostedEventId = w.EventId, LeadAppUserId = Missed, PartySize = 1,
            Kind = HostedEventBookingKind.DayPass, Status = HostedEventBookingStatus.Confirmed, DateCreated = now, CreatedByAppUserId = Missed,
        });
        db.HostedEventBookings.Add(new HostedEventBooking
        {
            Id = w.AskedBookingId, HostedEventId = w.EventId, LeadAppUserId = Asked, PartySize = 1,
            Kind = HostedEventBookingKind.DayPass, Status = HostedEventBookingStatus.Requested, DateCreated = now, CreatedByAppUserId = Asked,
        });

        if (checkedIn)
            db.HostedEventCheckIns.Add(new HostedEventCheckIn
            {
                Id = Guid.NewGuid(), HostedEventBookingId = w.CameBookingId, HostedEventNightId = w.FirstNightId,
                ArrivedUtc = FirstDay.AddHours(20), RecordedByAppUserId = Nephew, DateCreated = now, CreatedByAppUserId = Nephew,
            });

        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(), HostedEventId = w.EventId, AppUserId = Nephew, RunsTheDoor = true, RoleLabel = "Door",
            DateConfirmed = now, DateCreated = now, CreatedByAppUserId = Jim,
        });

        await db.SaveChangesAsync();
        return w;
    }

    public async Task ChangeEventAsync(Action<HostedEvent> change)
    {
        await using var db = await Sqlite.NewContextAsync();
        var ev = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.HostedEvents, e => e.Id == EventId);
        change(ev);
        await db.SaveChangesAsync();
    }

    // ── the pieces a test drives ─────────────────────────────────────────────

    /// <summary>Letters, as the tests read them.</summary>
    public sealed class Letters : IEmailService
    {
        public readonly List<EmailMessage> Sent = [];
        public bool IsConfigured { get; init; } = true;

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add(new EmailMessage(to, subject, htmlBody));
            return Task.CompletedTask;
        }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    public static readonly IOptions<SiteIdentity> Site =
        Options.Create(new SiteIdentity { Name = "IsHaunted", BaseUrl = "https://example.test", ApiBaseUrl = "https://api.example.test" });

    public EventGuestMailer Mailer(Letters letters)
        => new(letters, Site, NullLogger<EventGuestMailer>.Instance, new ForwardingOutboxQueue(letters));

    /// <summary>The security service: Jim may do anything in his group, nobody else may do anything.</summary>
    public Mock<IOrganizationSecurityService> Security()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) => user == Jim);
        security.Setup(s => s.MayAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationPermissionArea>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationPermissionArea _, OrganizationSecurityAction _, CancellationToken _) => user == Jim);
        return security;
    }

    public HostedEventAccess Access() => new(Security().Object);

    public static ControllerContext SignedInAs(Guid? who) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = who is { } id
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
                : new ClaimsPrincipal(new ClaimsIdentity()),
        },
    };

    public HostedEventBookingController Board(Letters letters, Guid who)
    {
        var security = Security();
        var queue = new ForwardingOutboxQueue(letters);
        return new HostedEventBookingController(
            Sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventCalendarSync(), new HostedEventAccess(security.Object), Mailer(letters), letters, Site,
            NullLogger<HostedEventBookingController>.Instance, queue)
        { ControllerContext = SignedInAs(who) };
    }

    public ValueTask DisposeAsync() => Sqlite.DisposeAsync();
}
