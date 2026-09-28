using System.Security.Claims;
using Ben.Data.WebApi.Services;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.FieldLaunches;
using Ben.Data.WebApi.Services.Push;
using Ben.Service.Models.Feed;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A lead starting everybody's Field Kit (item 252): who may press it, when, who it reaches, and
/// who can see the card it leaves in the feed.
/// </summary>
public sealed class FieldLaunchServiceTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now = now;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }

    /// <summary>Remembers who was pushed; every phone takes it.</summary>
    private sealed class FakeSender : IPushSender
    {
        public readonly List<(string Token, PushMessage Message)> Sent = new();
        public bool IsConfigured => true;
        public Task<PushOutcome> SendAsync(string token, PushEnvironment environment, PushMessage message, CancellationToken ct)
        {
            lock (Sent) Sent.Add((token, message));
            return Task.FromResult(PushOutcome.Delivered);
        }
    }

    /// <summary>A group, its people, a tour date, a private investigation and a hosted event — all tonight.</summary>
    private sealed class World
    {
        public readonly IDbContextFactory<BenDataContext> Db;
        public readonly Clock Clock;
        public readonly FakeSender Sender = new();
        public readonly Mock<IOrganizationSecurityService> Security = new();

        public readonly Guid Org = Guid.NewGuid();
        public readonly Guid Owner = Guid.NewGuid();
        public readonly Guid Guide = Guid.NewGuid();
        public readonly Guid Member = Guid.NewGuid();      // in the group, no say over anything
        public readonly Guid Guest = Guid.NewGuid();       // a seat reserved on the tour
        public readonly Guid Asked = Guid.NewGuid();       // a seat asked for, not yet given
        public readonly Guid Lead = Guid.NewGuid();        // the investigation's lead
        public readonly Guid Investigator = Guid.NewGuid();
        public readonly Guid Declined = Guid.NewGuid();
        public readonly Guid Booker = Guid.NewGuid();      // a confirmed booking at the hosted event
        public readonly Guid NamedGuest = Guid.NewGuid();  // named on that booking, with an account
        public readonly Guid Pending = Guid.NewGuid();     // a booking still asked for
        public readonly Guid DoorStaff = Guid.NewGuid();
        public readonly Guid Stranger = Guid.NewGuid();

        public readonly Guid TourDate = Guid.NewGuid();
        public readonly Guid Investigation = Guid.NewGuid();
        public readonly Guid Hosted = Guid.NewGuid();

        public readonly Guid Tour = Guid.NewGuid();

        public World(DateTime now, IDbContextFactory<BenDataContext>? db = null)
        {
            Clock = new Clock(now);
            Db = db ?? new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        }

        public FieldLaunchService Service(TimeProvider? clock = null) => new(
            Db, Security.Object, new HostedEventAccess(Security.Object, Db),
            new PushNotifier(Db, Sender, NullLogger<PushNotifier>.Instance), clock ?? Clock);

        public async Task SeedAsync(DateTime starts, bool investigationPublic = false, bool tourPublic = true)
        {
            var now = Clock.Now;
            await using var db = await Db.CreateDbContextAsync();
            foreach (var (id, name) in new[]
                     {
                         (Owner, "Olive"), (Guide, "Gideon"), (Member, "Mae"), (Guest, "Gus"), (Asked, "Ash"),
                         (Lead, "Lena"), (Investigator, "Ivy"), (Declined, "Dee"), (Booker, "Bea"), (NamedGuest, "Ned"),
                         (Pending, "Pat"), (DoorStaff, "Dora"), (Stranger, "Stan"),
                     })
            {
                db.Users.Add(new AppUser
                {
                    Id = id, UserName = $"{name}@t.dev", NormalizedUserName = $"{name}@T.DEV".ToUpperInvariant(),
                    Email = $"{name}@t.dev", DisplayName = name, Handle = name.ToLowerInvariant(), DateCreated = now,
                });
                db.PushDevices.Add(new PushDevice
                {
                    Id = Guid.NewGuid(), AppUserId = id, Token = $"{id:N}", Environment = PushEnvironment.Production,
                    DateCreated = now, LastSeenUtc = now,
                });
            }
            db.Organizations.Add(new Organization
            {
                Id = Org, Name = "Nashville Ghost Walks", UrlName = "ngw", TimeZoneId = "America/Chicago",
                DateCreated = now, CreatedByAppUserId = Owner,
            });
            foreach (var (id, role) in new[] { (Owner, OrganizationMemberRole.Owner), (Guide, OrganizationMemberRole.Member),
                                               (Member, OrganizationMemberRole.Member), (Lead, OrganizationMemberRole.Member),
                                               (Investigator, OrganizationMemberRole.Member) })
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = Org, AppUserId = id, Role = role, IsActive = true,
                    DateCreated = now, CreatedByAppUserId = Owner,
                });
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.FeaturePublicFeed, Value = "true", DateCreated = now, CreatedByAppUserId = Owner,
            });

            // The tour date, with a guide, a reserved seat and a seat only asked for.
            db.Tours.Add(new Tour
            {
                Id = Tour, OrganizationId = Org, Name = "Nashville Ghost Walk", UrlName = "ghost-walk",
                TimeZoneId = "America/Chicago", DateCreated = now, CreatedByAppUserId = Owner,
            });
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = TourDate, OrganizationId = Org, Title = "Saturday walk", Location = "Printers Alley",
                StartDateTime = starts, EndDateTime = starts.AddHours(2), IsPublic = tourPublic, TourId = Tour,
                DateCreated = now, CreatedByAppUserId = Owner,
            });
            db.OrgCalendarEventGuides.Add(new OrgCalendarEventGuide
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = TourDate, AppUserId = Guide, DateCreated = now, CreatedByAppUserId = Owner,
            });
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = TourDate, AppUserId = Guest, RsvpStatus = RsvpStatus.Accepted,
                SeatStatus = TourSeatStatus.Reserved, Seats = 2, DateCreated = now, CreatedByAppUserId = Guest,
            });
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = TourDate, AppUserId = Asked, RsvpStatus = RsvpStatus.Invited,
                SeatStatus = TourSeatStatus.Requested, Seats = 1, DateCreated = now, CreatedByAppUserId = Asked,
            });

            // A visit in a client's case: private.
            db.Investigations.Add(new Investigation
            {
                Id = Investigation, OrganizationId = Org, Title = "Belmont farmhouse, second visit", Location = "Belmont Blvd",
                ScheduledDateTime = starts, EndDateTime = starts.AddHours(4), Status = InvestigationStatus.Scheduled,
                Visibility = investigationPublic ? InvestigationVisibility.Public : InvestigationVisibility.GroupOnly,
                DateCreated = now, CreatedByAppUserId = Owner,
            });
            foreach (var (id, lead, rsvp) in new[] { (Lead, true, RsvpStatus.Accepted), (Investigator, false, RsvpStatus.Invited),
                                                     (Declined, false, RsvpStatus.Declined) })
                db.InvestigationAttendees.Add(new InvestigationAttendee
                {
                    Id = Guid.NewGuid(), InvestigationId = Investigation, AppUserId = id, IsLead = lead, Rsvp = rsvp,
                    DateCreated = now, CreatedByAppUserId = Owner,
                });

            // A hosted weekend: a confirmed booking with a named guest, one still asked for, door staff.
            var place = Guid.NewGuid();
            db.Places.Add(new Place { Id = place, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = Owner });
            db.HostedEvents.Add(new HostedEvent
            {
                Id = Hosted, OrganizationId = Org, PlaceId = place, Name = "Séance Weekend", UrlName = "seance",
                TimeZoneId = "UTC", StartsOn = starts.Date, EndsOn = starts.Date,
                LifecycleState = HostedEventLifecycleState.Live, DateCreated = now, CreatedByAppUserId = Owner,
            });
            var booking = Guid.NewGuid();
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = booking, HostedEventId = Hosted, LeadAppUserId = Booker, PartySize = 2,
                Status = HostedEventBookingStatus.Confirmed, DateCreated = now, CreatedByAppUserId = Booker,
            });
            db.HostedEventBookingGuests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking, DisplayName = "Ned", AppUserId = NamedGuest, DateCreated = now,
            });
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = Hosted, LeadAppUserId = Pending, PartySize = 1,
                Status = HostedEventBookingStatus.Requested, DateCreated = now, CreatedByAppUserId = Pending,
            });
            db.HostedEventStaff.Add(new HostedEventStaff
            {
                Id = Guid.NewGuid(), HostedEventId = Hosted, AppUserId = DoorStaff, RunsTheDoor = true,
                DateConfirmed = now, DateCreated = now, CreatedByAppUserId = Owner,
            });
            await db.SaveChangesAsync();
        }

        public HashSet<string> PushedTo() => Sender.Sent.Select(s => s.Token).ToHashSet();
        public string TokenOf(Guid id) => $"{id:N}";

        public FeedController Feed(Guid reader)
        {
            var root = Path.Combine(Path.GetTempPath(), "ben-launch-tests", Guid.NewGuid().ToString("N"));
            return new FeedController(Db, TestMedia.StorageOnDisk(root), TestMedia.IngestToDisk(root),
                new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
                new Ben.Data.WebApi.Services.Feed.FeedLearningService(TestMedia.StorageOnDisk(root),
                    NullLogger<Ben.Data.WebApi.Services.Feed.FeedLearningService>.Instance),
                NullLogger<FeedController>.Instance, Ben.Data.WebApi.Services.LinkPreviews.LinkPreviewWarmer.None)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = reader == Guid.Empty
                            ? new ClaimsPrincipal(new ClaimsIdentity())
                            : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, reader.ToString())], "Bearer")),
                    },
                },
            };
        }

        public async Task<List<FeedPostRecord>> FeedFor(Guid reader)
        {
            var result = await Feed(reader).GetFeed("all", null, null, CancellationToken.None);
            return ((FeedPageRecord)Assert.IsType<OkObjectResult>(result.Result).Value!).Posts.ToList();
        }
    }

    private static readonly DateTime Tonight = DateTime.UtcNow;

    private static LaunchOutcomeRecordOf Launched(FieldLaunchService.LaunchResult result)
        => new(Assert.IsType<FieldLaunchService.LaunchResult.Launched>(result).Outcome);

    private sealed record LaunchOutcomeRecordOf(Ben.Service.Models.FieldLaunches.LaunchOutcomeRecord Outcome);

    // ── Against a real database ──────────────────────────────────────────────

    /// <summary>
    /// The launch, written to a database that enforces its keys. The in-memory store does not, and
    /// the first launch against SQL Server was refused for a post missing its creator.
    /// </summary>
    [Fact]
    public async Task ALaunchIsWrittenToARealDatabaseAndItsCardReadsBack()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var w = new World(Tonight, sqlite.Factory);
        // The world is seeded with the keys off — a tour's start address, a place's owner and the
        // rest are not what this is about — and the keys go back on for everything the launch writes.
        await using (var db = await sqlite.NewContextAsync()) await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        await using (var db = await sqlite.NewContextAsync()) await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        var service = w.Service();

        var tour = Launched(await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default)).Outcome;
        var visit = Launched(await service.LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default)).Outcome;
        Launched(await service.LaunchAsync(w.DoorStaff, false, FieldLaunchTarget.HostedEvent, w.Hosted, default));

        Assert.Equal("Nashville Ghost Walk: Saturday walk", tour.Launch.Title);
        Assert.Single(await service.MineAsync(w.Investigator, default));
        Assert.NotNull(await service.ReadAsync(visit.Launch.Id, w.Investigator, false, default));
        // The owner: the tour date and the visit. (The hosted event is the permission service's to
        // grant, and this world's grants nothing — its door staff launched it above.)
        Assert.Equal(["event", "investigation"], (await service.LaunchableAsync(w.Owner, default)).Select(l => l.Target).Order());
    }

    // ── Who it reaches ───────────────────────────────────────────────────────

    [Fact]
    public async Task ATourDatesGuideReachesEverySeatReservedAndNobodyElse()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));

        var outcome = Launched(await w.Service().LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default)).Outcome;

        Assert.Equal([w.TokenOf(w.Guest)], w.PushedTo());
        Assert.Equal((1, 1, 1, true), (outcome.People, outcome.PeopleWithTheApp, outcome.PhonesReached, outcome.PushConfigured));
        var push = w.Sender.Sent.Single().Message;
        Assert.Equal("Nashville Ghost Walk: Saturday walk is starting", push.Title);
        Assert.Equal(FieldLaunchService.AppLink(outcome.Launch.Id), push.Data["link"]);
        Assert.Equal(w.TourDate, outcome.Launch.OrgCalendarEventId);
        Assert.Equal("Printers Alley", outcome.Launch.LocationLabel);
    }

    [Fact]
    public async Task AnInvestigationReachesItsPeopleButNotWhoeverDeclinedNorTheLeadWhoPressedIt()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));

        Launched(await w.Service().LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default));

        Assert.Equal([w.TokenOf(w.Investigator)], w.PushedTo());
    }

    [Fact]
    public async Task AnInvestigationAtAPublicEventAlsoReachesThatEventsPeople()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            (await db.Investigations.SingleAsync(i => i.Id == w.Investigation)).OrgCalendarEventId = w.TourDate;
            await db.SaveChangesAsync();
        }

        Launched(await w.Service().LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default));

        Assert.Equal([w.TokenOf(w.Investigator), w.TokenOf(w.Guest)], w.PushedTo());
    }

    [Fact]
    public async Task AHostedEventReachesConfirmedBookingsAndTheirNamedGuests()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight);

        Launched(await w.Service().LaunchAsync(w.DoorStaff, false, FieldLaunchTarget.HostedEvent, w.Hosted, default));

        Assert.Equal([w.TokenOf(w.Booker), w.TokenOf(w.NamedGuest)], w.PushedTo());
    }

    // ── Who may press it ─────────────────────────────────────────────────────

    [Fact]
    public async Task AnOrdinaryMemberMayNotLaunchAnything()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        var service = w.Service();

        Assert.IsType<FieldLaunchService.LaunchResult.Forbidden>(
            await service.LaunchAsync(w.Member, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));
        Assert.IsType<FieldLaunchService.LaunchResult.Forbidden>(
            await service.LaunchAsync(w.Investigator, false, FieldLaunchTarget.Investigation, w.Investigation, default));
        Assert.IsType<FieldLaunchService.LaunchResult.Forbidden>(
            await service.LaunchAsync(w.Guest, false, FieldLaunchTarget.HostedEvent, w.Hosted, default));
        Assert.Empty(w.Sender.Sent);
    }

    [Fact]
    public async Task SomebodyWhoMayEditTheCalendarMayLaunchATourDateTheyDoNotGuide()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        w.Security.Setup(s => s.HasAccessAsync(w.Member, w.Org, OrganizationSecurityTable.OrgCalendar,
                OrganizationSecurityAction.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Launched(await w.Service().LaunchAsync(w.Member, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));
    }

    [Fact]
    public async Task TheGroupsOwnerMayLaunchAnyOfItsInvestigations()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        Launched(await w.Service().LaunchAsync(w.Owner, false, FieldLaunchTarget.Investigation, w.Investigation, default));
    }

    // ── When ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ItOpensThreeHoursBeforeAndClosesWhenItEnds()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddHours(4));
        var service = w.Service();

        Assert.IsType<FieldLaunchService.LaunchResult.Refused>(
            await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));

        w.Clock.Now = Tonight.AddHours(1.5);   // two and a half hours before
        Launched(await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));

        w.Clock.Now = Tonight.AddHours(6.5);   // it ended at 6
        Assert.IsType<FieldLaunchService.LaunchResult.Refused>(
            await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));
    }

    [Fact]
    public async Task PressingItTwiceInAFewMinutesSendsItOnce()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight);
        var service = w.Service();
        Launched(await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));

        w.Clock.Now = Tonight.AddMinutes(3);
        Assert.IsType<FieldLaunchService.LaunchResult.Refused>(
            await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));
        Assert.Single(w.Sender.Sent);

        w.Clock.Now = Tonight.AddMinutes(11);  // a second hunt later in the night
        Launched(await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));
    }

    // ── The card ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task APrivateInvestigationsCardIsSeenOnlyByItsPeopleAndTheLead()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        var launch = Launched(await w.Service().LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default)).Outcome.Launch;

        var card = Assert.Single(await w.FeedFor(w.Investigator));
        Assert.Equal(launch.Id, card.Launch!.LaunchId);
        Assert.Equal(launch.AppLink, card.Launch.AppLink);
        Assert.Single(await w.FeedFor(w.Lead));

        Assert.Empty(await w.FeedFor(w.Stranger));
        Assert.Empty(await w.FeedFor(w.Declined));
        Assert.Empty(await w.FeedFor(Guid.Empty));

        // Nor does the post's own page open for somebody it was not sent to.
        Assert.IsType<NotFoundResult>((await w.Feed(w.Stranger).GetThread(card.Id, default)).Result);
        Assert.IsType<OkObjectResult>((await w.Feed(w.Investigator).GetThread(card.Id, default)).Result);
    }

    [Fact]
    public async Task APublicTourDatesCardIsForAnyone()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        Launched(await w.Service().LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default));

        Assert.NotNull(Assert.Single(await w.FeedFor(Guid.Empty)).Launch);
        Assert.Single(await w.FeedFor(w.Stranger));
    }

    [Fact]
    public async Task TheCardGoesSixHoursAfterTheEnd()
    {
        // Launched two days ago, for a walk that ended then: the feed (on the real clock) is past it.
        var w = new World(Tonight.AddDays(-2));
        await w.SeedAsync(starts: Tonight.AddDays(-2).AddMinutes(30));
        var launch = Launched(await w.Service().LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default)).Outcome.Launch;
        Assert.Equal(launch.EndsUtc.AddHours(6), launch.ExpiresUtc);

        Assert.Empty(await w.FeedFor(Guid.Empty));
        Assert.Empty(await w.FeedFor(w.Guest));
        Assert.Null(await w.Service(TimeProvider.System).ReadAsync(launch.Id, w.Guest, false, default));
    }

    // ── Reading a launch ─────────────────────────────────────────────────────

    [Fact]
    public async Task APrivateLaunchOpensOnlyForItsPeople()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        var service = w.Service();
        var launch = Launched(await service.LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default)).Outcome.Launch;

        Assert.NotNull(await service.ReadAsync(launch.Id, w.Investigator, false, default));
        Assert.NotNull(await service.ReadAsync(launch.Id, w.Lead, false, default));
        Assert.Null(await service.ReadAsync(launch.Id, w.Stranger, false, default));
        Assert.Null(await service.ReadAsync(launch.Id, Guid.Empty, false, default));

        Assert.Single(await service.MineAsync(w.Investigator, default));
        Assert.Empty(await service.MineAsync(w.Stranger, default));
    }

    [Fact]
    public async Task APublicLaunchOpensForAnybodyEvenWithoutAnAccount()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        var service = w.Service();
        var launch = Launched(await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default)).Outcome.Launch;

        Assert.NotNull(await service.ReadAsync(launch.Id, Guid.Empty, false, default));
    }

    // ── The lead's list ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheLeadsListHoldsWhatTheyMayLaunchNowAndNothingElse()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        var service = w.Service();

        var guide = await service.LaunchableAsync(w.Guide, default);
        var only = Assert.Single(guide);
        Assert.Equal(("event", w.TourDate, 1), (only.Target, only.Id, only.People));

        Assert.Equal(["investigation"], (await service.LaunchableAsync(w.Lead, default)).Select(l => l.Target));
        Assert.Equal(["hosted-event"], (await service.LaunchableAsync(w.DoorStaff, default)).Select(l => l.Target));
        Assert.Empty(await service.LaunchableAsync(w.Member, default));
        Assert.Empty(await service.LaunchableAsync(w.Stranger, default));
    }
}
