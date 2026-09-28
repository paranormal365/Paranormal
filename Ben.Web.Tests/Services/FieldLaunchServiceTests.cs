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

    [Fact]
    public async Task TheGroupsOwnPrivateNightIsOnTheLeadsList()
    {
        // Ben: "or a specific event" — not only a tour date or a public night.
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            var night = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.TourDate);
            (night.TourId, night.IsPublic) = (null, false);
            await db.SaveChangesAsync();
        }

        Assert.Equal([w.TourDate], (await w.Service().LaunchableAsync(w.Guide, default)).Select(l => l.Id));
    }

    // ── Joining by the lead's code (the QR) ──────────────────────────────────

    private static async Task<(World W, FieldLaunchService Service, Ben.Service.Models.FieldLaunches.FieldLaunchRecord Launch)>
        LaunchedPrivateTourAsync()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30), tourPublic: false);
        var service = w.Service();
        var launch = Launched(await service.LaunchAsync(w.Guide, false, FieldLaunchTarget.CalendarEvent, w.TourDate, default)).Outcome.Launch;
        return (w, service, launch);
    }

    [Fact]
    public async Task OnlyTheLeadIsGivenTheCode()
    {
        var (w, service, launch) = await LaunchedPrivateTourAsync();

        Assert.NotNull(launch.JoinToken);                                             // the lead's own answer
        Assert.NotNull((await service.ReadAsync(launch.Id, w.Owner, false, default))!.JoinToken);   // a manager
        Assert.Null((await service.ReadAsync(launch.Id, w.Guest, false, default))!.JoinToken);      // a guest
        Assert.Null(Assert.Single(await service.MineAsync(w.Guest, default)).JoinToken);
    }

    [Fact]
    public async Task SomebodyWhoScansIsToldWhereTheyStand()
    {
        var (w, service, launch) = await LaunchedPrivateTourAsync();
        var token = launch.JoinToken!;

        Assert.Equal("in", (await service.StandingAsync(token, w.Guest, false, default))!.Standing);
        Assert.NotNull((await service.StandingAsync(token, w.Guest, false, default))!.Launch);
        Assert.Equal("ask", (await service.StandingAsync(token, w.Stranger, false, default))!.Standing);
        Assert.Null((await service.StandingAsync(token, w.Stranger, false, default))!.Launch);
        Assert.Equal("sign-in", (await service.StandingAsync(token, Guid.Empty, false, default))!.Standing);
        Assert.Null(await service.StandingAsync("not-the-code", w.Stranger, false, default));
    }

    [Fact]
    public async Task AskingTellsTheLeadAndWaitsForThem()
    {
        var (w, service, launch) = await LaunchedPrivateTourAsync();
        w.Sender.Sent.Clear();

        Assert.Equal("pending", (await service.AskAsync(launch.JoinToken!, w.Stranger, default))!.Standing);
        Assert.Equal("pending", (await service.AskAsync(launch.JoinToken!, w.Stranger, default))!.Standing);   // asking twice is once

        var told = Assert.Single(w.Sender.Sent);
        Assert.Equal(w.TokenOf(w.Guide), told.Token);
        Assert.Equal("Stan wants to join", told.Message.Title);
        Assert.Equal(FieldLaunchService.RequestsLink(launch.Id), told.Message.Data["link"]);

        Assert.Null(await service.RequestsAsync(launch.Id, w.Guest, false, default));   // not the guests' business
        var asked = Assert.Single((await service.RequestsAsync(launch.Id, w.Guide, false, default))!);
        Assert.Equal(("Stan", "pending"), (asked.DisplayName, asked.Status));
        Assert.Empty(await service.MineAsync(w.Stranger, default));                      // not in yet
    }

    [Fact]
    public async Task ALeadsYesReservesATourSeatAndLetsThemIn()
    {
        var (w, service, launch) = await LaunchedPrivateTourAsync();
        await service.AskAsync(launch.JoinToken!, w.Stranger, default);
        var request = Assert.Single((await service.RequestsAsync(launch.Id, w.Guide, false, default))!);
        w.Sender.Sent.Clear();

        Assert.IsType<FieldLaunchService.DecideResult.Forbidden>(await service.DecideAsync(launch.Id, request.Id, w.Guest, false, approve: true, default));
        var decided = Assert.IsType<FieldLaunchService.DecideResult.Decided>(
            await service.DecideAsync(launch.Id, request.Id, w.Guide, false, approve: true, default));
        Assert.Equal("approved", decided.Request.Status);

        await using (var db = await w.Db.CreateDbContextAsync())
        {
            var seat = await db.OrgCalendarEventAttendees.SingleAsync(a => a.OrgCalendarEventId == w.TourDate && a.AppUserId == w.Stranger);
            Assert.Equal((RsvpStatus.Accepted, TourSeatStatus.Reserved), (seat.RsvpStatus, seat.SeatStatus));
        }
        Assert.Equal("in", (await service.StandingAsync(launch.JoinToken!, w.Stranger, false, default))!.Standing);
        Assert.Single(await service.MineAsync(w.Stranger, default));
        var told = Assert.Single(w.Sender.Sent);
        Assert.Equal((w.TokenOf(w.Stranger), "You're in"), (told.Token, told.Message.Title));
        Assert.Equal(launch.AppLink, told.Message.Data["link"]);
    }


    [Fact]
    public async Task ALeadsNoLeavesThemOut()
    {
        var (w, service, launch) = await LaunchedPrivateTourAsync();
        await service.AskAsync(launch.JoinToken!, w.Stranger, default);
        var request = Assert.Single((await service.RequestsAsync(launch.Id, w.Guide, false, default))!);

        await service.DecideAsync(launch.Id, request.Id, w.Guide, false, approve: false, default);

        Assert.Equal("declined", (await service.StandingAsync(launch.JoinToken!, w.Stranger, false, default))!.Standing);
        Assert.Equal("declined", (await service.AskAsync(launch.JoinToken!, w.Stranger, default))!.Standing);   // a no stands
        Assert.Empty(await service.MineAsync(w.Stranger, default));
        await using var db = await w.Db.CreateDbContextAsync();
        Assert.False(await db.OrgCalendarEventAttendees.AnyAsync(a => a.AppUserId == w.Stranger));
    }

    [Fact]
    public async Task AYesOnAnInvestigationIsAGuestPassAndKeepsTheGuidesCode()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        var sheet = Guid.NewGuid();
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            db.InvestigationJoinCodes.Add(new InvestigationJoinCode
            {
                Id = sheet, InvestigationId = w.Investigation, OrganizationId = w.Org, Token = "printed-sheet",
                TypedCode = "ABC123", ExpiresUtc = Tonight.AddHours(8), DateCreated = Tonight, CreatedByAppUserId = w.Lead,
            });
            await db.SaveChangesAsync();
        }
        var service = w.Service();
        var launch = Launched(await service.LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default)).Outcome.Launch;
        await service.AskAsync(launch.JoinToken!, w.Stranger, default);
        var request = Assert.Single((await service.RequestsAsync(launch.Id, w.Lead, false, default))!);

        Assert.IsType<FieldLaunchService.DecideResult.Decided>(
            await service.DecideAsync(launch.Id, request.Id, w.Lead, false, approve: true, default));

        await using var check = await w.Db.CreateDbContextAsync();
        var pass = await check.InvestigationGuestPasses.SingleAsync(p => p.AppUserId == w.Stranger);
        Assert.Equal(sheet, pass.InvestigationJoinCodeId);                                      // minted on the guide's own code
        Assert.Null((await check.InvestigationJoinCodes.SingleAsync(c => c.Id == sheet)).RevokedUtc);   // which still works
        Assert.False(await check.InvestigationAttendees.AnyAsync(a => a.AppUserId == w.Stranger));      // a guest, not a member
    }

    [Fact]
    public async Task ASomebodysHomeCannotBeJoinedByCode()
    {
        var w = new World(Tonight);
        await w.SeedAsync(starts: Tonight.AddMinutes(30));
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            var home = Guid.NewGuid();
            db.Places.Add(new Place { Id = home, Name = "12 Oak Lane", Kind = PlaceKind.PrivateResidence, DateCreated = Tonight, CreatedByAppUserId = w.Owner });
            (await db.Investigations.SingleAsync(i => i.Id == w.Investigation)).PlaceId = home;
            await db.SaveChangesAsync();
        }
        var launch = Launched(await w.Service().LaunchAsync(w.Lead, false, FieldLaunchTarget.Investigation, w.Investigation, default)).Outcome.Launch;

        Assert.Null(launch.JoinToken);   // even to the lead: there is no code to show
    }
}
