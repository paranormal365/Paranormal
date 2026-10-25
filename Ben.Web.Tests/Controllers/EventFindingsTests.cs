using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// An event's findings (Ben, 10/25/2026): the people who were there's until the organizer publishes them, and on
/// the place's page only what the organizer chose, a week after telling the guest, and never against the guest's no.
/// </summary>
public sealed class EventFindingsTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid HostedId, Guid CalendarId, Guid PlaceId,
        Guid MemberId, Guid GuestId, Guid StrangerId, Guid SubmissionId, Guid FileId);

    private static async Task<World> SeedAsync(PlaceKind kind = PlaceKind.PublicLocation, bool published = false)
    {
        var f = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        Guid org = Guid.NewGuid(), hosted = Guid.NewGuid(), cal = Guid.NewGuid(), place = Guid.NewGuid(),
             member = Guid.NewGuid(), guest = Guid.NewGuid(), stranger = Guid.NewGuid(), sub = Guid.NewGuid(), file = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        foreach (var (id, name) in new[] { (member, "m"), (guest, "g"), (stranger, "s") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{name}@t.com", Email = $"{name}@t.com", DisplayName = name, DateCreated = now });
        db.Organizations.Add(new Organization { Id = org, Name = "Org", UrlName = "org", DateCreated = now, CreatedByAppUserId = member });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = org, AppUserId = member, Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = now, CreatedByAppUserId = member,
        });
        db.Places.Add(new Place { Id = place, Name = "The Old Jail", Kind = kind, DateCreated = now, CreatedByAppUserId = member });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = hosted, OrganizationId = org, PlaceId = place, Name = "Jail Night", UrlName = "jail-night",
            StartsOn = now.Date.AddDays(-3), EndsOn = now.Date.AddDays(-2), LifecycleState = HostedEventLifecycleState.Ended,
            TimeZoneId = "America/Chicago", FindingsPublishedUtc = published ? now.AddDays(-2) : null,
            DateCreated = now, CreatedByAppUserId = member,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = cal, OrganizationId = org, HostedEventId = hosted, PlaceId = place, Title = "Jail Night", IsPublic = true,
            StartDateTime = now.AddDays(-3), EndDateTime = now.AddDays(-2), DateCreated = now, CreatedByAppUserId = member,
        });
        db.HostedEventBookings.Add(new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = hosted, LeadAppUserId = guest, PartySize = 1, Kind = HostedEventBookingKind.DayPass,
            Status = HostedEventBookingStatus.Confirmed, DateCreated = now, CreatedByAppUserId = guest,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = file, UploadFileTypeId = Guid.NewGuid(), AppUserId = guest, FileName = "door.jpg", StoredFileName = "x",
            ContentType = "image/jpeg", FileSize = 3, FileData = [1, 2, 3], IsPublic = false, DateCreated = now, CreatedByAppUserId = guest,
        });
        db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
        {
            Id = sub, OrgCalendarEventId = cal, SubmittedByAppUserId = guest, UploadFileId = file,
            Status = EvidenceSubmissionStatus.Accepted, DateCreated = now, CreatedByAppUserId = guest,
        });
        await db.SaveChangesAsync();
        return new World(f, org, hosted, cal, place, member, guest, stranger, sub, file);
    }

    private static async Task<bool> MaySee(World w, Guid who)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await EventFindings.MaySeeAsync(db, w.CalendarId, who, default);
    }

    private static async Task<bool> OnPlace(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await ArchiveEvidencePublication.MayServeAsync(db, w.SubmissionId, default);
    }

    private static async Task ChangeAsync(World w, Action<BenDataContext, HostedEvent, EventEvidenceSubmission> change)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var ev = await db.HostedEvents.SingleAsync(h => h.Id == w.HostedId);
        var sub = await db.EventEvidenceSubmissions.SingleAsync(s => s.Id == w.SubmissionId);
        change(db, ev, sub);
        await db.SaveChangesAsync();
    }

    // ── who may see them ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_events_findings_go_only_to_the_people_who_were_there()
    {
        var w = await SeedAsync();

        Assert.False(await MaySee(w, Guid.Empty));
        Assert.False(await MaySee(w, w.StrangerId));
        Assert.True(await MaySee(w, w.GuestId));
        Assert.True(await MaySee(w, w.MemberId));
    }

    [Fact]
    public async Task Once_the_organizer_publishes_anybody_may_see_them()
    {
        var w = await SeedAsync(published: true);

        Assert.True(await MaySee(w, Guid.Empty));
        Assert.True(await MaySee(w, w.StrangerId));
    }

    [Fact]
    public async Task A_guest_whose_booking_was_turned_down_was_not_there()
    {
        var w = await SeedAsync();
        await ChangeAsync(w, (db, _, _) =>
            db.HostedEventBookings.Single(b => b.LeadAppUserId == w.GuestId).Status = HostedEventBookingStatus.TurnedDown);

        Assert.False(await MaySee(w, w.GuestId));
    }

    /// <summary>Archiving takes the calendar row off the public site; the findings stay as they were (10/25/2026).</summary>
    private static Task ArchiveAsync(World w, HostedEventLifecycleState state = HostedEventLifecycleState.Archived)
        => ChangeAsync(w, (db, ev, _) =>
        {
            ev.LifecycleState = state;
            db.OrgCalendarEvents.Single(c => c.Id == w.CalendarId).IsPublic = false;
        });

    [Fact]
    public async Task Archiving_keeps_published_findings_open_to_anybody()
    {
        var w = await SeedAsync(published: true);
        await ArchiveAsync(w);

        Assert.True(await MaySee(w, Guid.Empty));
        Assert.True(await MaySee(w, w.GuestId));
    }

    [Fact]
    public async Task Archiving_keeps_private_findings_with_the_people_who_were_there()
    {
        var w = await SeedAsync(published: false);
        await ArchiveAsync(w);

        Assert.False(await MaySee(w, w.StrangerId));
        Assert.True(await MaySee(w, w.GuestId));
    }

    [Fact]
    public async Task A_called_off_events_findings_go_only_to_the_people_who_were_there()
    {
        var w = await SeedAsync(published: true);
        await ArchiveAsync(w, HostedEventLifecycleState.Cancelled);

        Assert.False(await MaySee(w, Guid.Empty));
        Assert.True(await MaySee(w, w.GuestId));
    }

    [Fact]
    public async Task An_event_removed_from_the_site_shows_its_findings_to_nobody()
    {
        var w = await SeedAsync(published: true);
        await ArchiveAsync(w, HostedEventLifecycleState.Removed);

        Assert.False(await MaySee(w, Guid.Empty));
        Assert.False(await MaySee(w, w.GuestId));
    }

    // ── publishing ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publishing_opens_the_accepted_files_and_unpublishing_closes_them()
    {
        var w = await SeedAsync();

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var ev = await db.HostedEvents.SingleAsync();
            await EventFindings.SetPublishedAsync(db, ev, true, w.MemberId, DateTime.UtcNow, default);
            await db.SaveChangesAsync();
            Assert.True((await db.UploadFiles.SingleAsync(f => f.Id == w.FileId)).IsPublic);
        }
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var ev = await db.HostedEvents.SingleAsync();
            ev.FindingsOnPlace = FindingsOnPlace.AllPublic;
            await EventFindings.SetPublishedAsync(db, ev, false, w.MemberId, DateTime.UtcNow, default);
            await db.SaveChangesAsync();
            Assert.False((await db.UploadFiles.SingleAsync(f => f.Id == w.FileId)).IsPublic);
            Assert.Equal(FindingsOnPlace.None, ev.FindingsOnPlace);
            Assert.Null(ev.FindingsPublishedUtc);
        }
    }

    [Fact]
    public async Task Unpublishing_leaves_a_guests_own_place_contribution_public()
    {
        var w = await SeedAsync(published: true);
        await ChangeAsync(w, (db, _, sub) =>
        {
            sub.PublishedToPlaceAtUtc = DateTime.UtcNow;
            db.UploadFiles.Single(f => f.Id == w.FileId).IsPublic = true;
        });

        await using var db = await w.F.CreateDbContextAsync();
        var ev = await db.HostedEvents.SingleAsync();
        await EventFindings.SetPublishedAsync(db, ev, false, w.MemberId, DateTime.UtcNow, default);
        await db.SaveChangesAsync();
        Assert.True((await db.UploadFiles.SingleAsync(f => f.Id == w.FileId)).IsPublic);
    }

    [Fact]
    public void Publishing_is_refused_at_a_private_home_when_called_off_and_before_the_start()
    {
        var now = DateTime.UtcNow;
        var ev = new HostedEvent { LifecycleState = HostedEventLifecycleState.Ended };

        Assert.NotNull(EventFindings.WhyNotPublish(ev, PlaceKind.PrivateResidence, now.AddDays(-1), now));
        Assert.NotNull(EventFindings.WhyNotPublish(ev, PlaceKind.PublicLocation, now.AddHours(1), now));
        Assert.Null(EventFindings.WhyNotPublish(ev, PlaceKind.PublicLocation, now.AddHours(-1), now));

        ev.LifecycleState = HostedEventStates.CalledOff.First();
        Assert.NotNull(EventFindings.WhyNotPublish(ev, PlaceKind.PublicLocation, now.AddDays(-1), now));
    }

    // ── the organizer's controls ─────────────────────────────────────────────

    private static HostedEventFindingsController Organizer(World w, Guid who, IAuditLogService audit)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) => user == w.MemberId);
        var services = new ServiceCollection().AddSingleton(audit).BuildServiceProvider();
        return new HostedEventFindingsController(w.F, new Mock<AutoMapper.IMapper>().Object, security.Object, new HostedEventAccess(security.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, who.ToString())], "Bearer")),
                },
            },
        };
    }

    /// <summary>10/25/2026: publishing and choosing for the place's page leave an audit record of who and what.</summary>
    [Fact]
    public async Task Publishing_and_choosing_a_piece_are_audited_and_the_guest_is_told()
    {
        var w = await SeedAsync();
        var audit = new Mock<IAuditLogService>();

        var published = await Organizer(w, w.MemberId, audit.Object).Publish(w.OrgId, w.HostedId, "publish", default);
        var record = Assert.IsType<EventFindingsRecord>(Assert.IsType<OkObjectResult>(published.Result).Value);
        Assert.NotNull(record.PublishedUtc);
        audit.Verify(a => a.LogUpdateAsync(nameof(HostedEvent), w.HostedId, It.IsAny<object>(), It.IsAny<object>(), w.MemberId, It.IsAny<string>()), Times.Once);

        var chosen = await Organizer(w, w.MemberId, audit.Object).ChoosePiece(w.OrgId, w.HostedId, "evidence", w.SubmissionId, true,
            new PlatformMessageService(w.F), default);
        Assert.IsType<OkObjectResult>(chosen.Result);
        audit.Verify(a => a.LogUpdateAsync(nameof(EventEvidenceSubmission), w.SubmissionId, It.IsAny<object>(), It.IsAny<object>(), w.MemberId, It.IsAny<string>()), Times.Once);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.GuestId));
        Assert.Equal(FindingsOnPlace.Chosen, (await db.HostedEvents.SingleAsync()).FindingsOnPlace);
    }

    [Fact]
    public async Task Somebody_who_cant_edit_the_event_can_neither_publish_nor_choose()
    {
        var w = await SeedAsync();
        var audit = new Mock<IAuditLogService>().Object;

        Assert.IsType<ForbidResult>((await Organizer(w, w.GuestId, audit).Publish(w.OrgId, w.HostedId, "publish", default)).Result);
        Assert.IsType<ForbidResult>((await Organizer(w, w.StrangerId, audit).ChoosePiece(w.OrgId, w.HostedId, "evidence", w.SubmissionId, true,
            new PlatformMessageService(w.F), default)).Result);
    }

    [Fact]
    public async Task The_place_page_needs_public_findings_first()
    {
        var w = await SeedAsync();
        var refused = await Organizer(w, w.MemberId, new Mock<IAuditLogService>().Object)
            .SetPlace(w.OrgId, w.HostedId, FindingsOnPlace.AllPublic, new PlatformMessageService(w.F), default);

        Assert.StartsWith("Make the findings public first", (string)Assert.IsType<BadRequestObjectResult>(refused.Result).Value!);
    }

    // ── the place's page ──────────────────────────────────────────────────────

    private static Task ChooseAsync(World w, int daysAgo, FindingsOnPlace mode = FindingsOnPlace.Chosen)
        => ChangeAsync(w, (_, ev, sub) =>
        {
            ev.FindingsOnPlace = mode;
            sub.OnPlaceChosenUtc = DateTime.UtcNow.AddDays(-daysAgo);
        });

    [Fact]
    public async Task A_chosen_piece_shows_on_the_place_only_after_the_guests_week()
    {
        var w = await SeedAsync(published: true);

        await ChooseAsync(w, daysAgo: 6);
        Assert.False(await OnPlace(w));

        await ChooseAsync(w, daysAgo: 8);
        Assert.True(await OnPlace(w));
    }

    [Fact]
    public async Task The_guests_no_wins_even_after_it_shows()
    {
        var w = await SeedAsync(published: true);
        await ChooseAsync(w, daysAgo: 8);
        Assert.True(await OnPlace(w));

        await ChangeAsync(w, (_, _, sub) => sub.PlaceOptOutUtc = DateTime.UtcNow);
        Assert.False(await OnPlace(w));
    }

    [Fact]
    public async Task A_guests_standing_no_on_their_booking_keeps_every_piece_off()
    {
        var w = await SeedAsync(published: true);
        await ChooseAsync(w, daysAgo: 8, FindingsOnPlace.AllPublic);
        await ChangeAsync(w, (db, _, _) =>
            db.HostedEventBookings.Single(b => b.LeadAppUserId == w.GuestId).FindingsOffPlaceUtc = DateTime.UtcNow);

        Assert.False(await OnPlace(w));
    }

    [Fact]
    public async Task Nothing_shows_on_the_place_while_the_findings_are_private()
    {
        var w = await SeedAsync(published: false);
        await ChooseAsync(w, daysAgo: 8);

        Assert.False(await OnPlace(w));
    }

    [Fact]
    public async Task Nothing_shows_when_the_organizer_turns_the_place_page_off()
    {
        var w = await SeedAsync(published: true);
        await ChooseAsync(w, daysAgo: 8, FindingsOnPlace.None);

        Assert.False(await OnPlace(w));
    }

    [Fact]
    public async Task Nothing_from_a_private_home_ever_shows_on_its_page()
    {
        var w = await SeedAsync(PlaceKind.PrivateResidence, published: true);
        await ChooseAsync(w, daysAgo: 8);

        Assert.False(await OnPlace(w));
    }
}
