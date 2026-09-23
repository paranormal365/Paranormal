using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
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

namespace Ben.Web.Tests.Services;

/// <summary>
/// The door on the phone: which doors a person has, and an arrival kept while there was no signal (item 235 phase 14c).
/// </summary>
public sealed class HostedEventDoorDutiesTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid VenueOrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();

    private static readonly Guid DoorMemberId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();
    private static readonly Guid StewardId = Guid.NewGuid();
    private static readonly Guid UnacceptedId = Guid.NewGuid();
    private static readonly Guid CookId = Guid.NewGuid();
    private static readonly Guid PorterId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();

    private static readonly Guid TonightEventId = Guid.NewGuid();
    private static readonly Guid VenueEventId = Guid.NewGuid();
    private static readonly Guid LastMonthEventId = Guid.NewGuid();
    private static readonly Guid DraftEventId = Guid.NewGuid();
    private static readonly Guid TonightNightId = Guid.NewGuid();

    /// <summary>The group's permission, as the roles page would grant it: the door for two people only.</summary>
    private static Mock<IOrganizationSecurityService> Security()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid org, OrganizationSecurityTable table, OrganizationSecurityAction _, CancellationToken _) =>
                table == OrganizationSecurityTable.EventCheckIn
                && ((user == DoorMemberId && org == OrgId) || (user == PorterId && org == VenueOrgId)));
        return security;
    }

    private static ControllerContext As(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
        },
    };

    private static MyHostedEventDutiesController Duties(SqliteTestDb sqlite, Guid userId)
        => new(sqlite.Factory, new HostedEventAccess(Security().Object)) { ControllerContext = As(userId) };

    private static HostedEventDoorController Door(SqliteTestDb sqlite)
    {
        var security = Security();
        return new HostedEventDoorController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object)) { ControllerContext = As(DoorMemberId) };
    }

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;
        var today = TimeZoneInfo.ConvertTimeFromUtc(now, HostedEventCalendarSync.ZoneOf("America/Chicago")).Date;

        foreach (var id in new[] { HostId, DoorMemberId, PlainMemberId, StewardId, UnacceptedId, CookId, PorterId, GuestId })
            db.Users.Add(new AppUser { Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test", DisplayName = $"{id:N}"[..6], DateCreated = now });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "Nashville Paranormal", UrlName = "nashville", DateCreated = now, CreatedByAppUserId = HostId });
        db.Organizations.Add(new Organization { Id = VenueOrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });

        foreach (var (user, org) in new[] { (DoorMemberId, OrgId), (PlainMemberId, OrgId), (PorterId, VenueOrgId) })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org, AppUserId = user, IsActive = true, DateCreated = now, CreatedByAppUserId = HostId,
            });

        var grantId = Guid.NewGuid();
        db.OrganizationVenueGrants.Add(new OrganizationVenueGrant
        {
            Id = grantId, VenueOrganizationId = VenueOrgId, GranteeOrganizationId = OrgId, PlaceId = PlaceId,
            ValidFrom = today, ValidTo = today.AddDays(40), AllowStaff = true, DateCreated = now, CreatedByAppUserId = HostId,
        });

        void Event(Guid id, string name, DateTime starts, HostedEventLifecycleState state, Guid? grant = null)
            => db.HostedEvents.Add(new HostedEvent
            {
                Id = id, OrganizationId = OrgId, PlaceId = PlaceId, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'),
                StartsOn = starts, EndsOn = starts, LifecycleState = state, TimeZoneId = "America/Chicago", VenueGrantId = grant,
                DateCreated = now, CreatedByAppUserId = HostId,
            });

        Event(TonightEventId, "Tonight", today, HostedEventLifecycleState.Live);
        Event(VenueEventId, "At the venue", today.AddDays(30), HostedEventLifecycleState.Published, grantId);
        Event(LastMonthEventId, "Last month", today.AddDays(-30), HostedEventLifecycleState.Ended);
        Event(DraftEventId, "Not yet", today.AddDays(10), HostedEventLifecycleState.Draft);

        db.HostedEventNights.Add(new HostedEventNight { Id = TonightNightId, HostedEventId = TonightEventId, Date = today, DateCreated = now, CreatedByAppUserId = HostId });

        void Staff(Guid user, Guid eventId, bool door, bool accepted, string label)
            => db.HostedEventStaff.Add(new HostedEventStaff
            {
                Id = Guid.NewGuid(), HostedEventId = eventId, AppUserId = user, RoleLabel = label, RunsTheDoor = door,
                SeesMenus = !door, DateConfirmed = accepted ? now : null, DateCreated = now, CreatedByAppUserId = HostId,
            });

        Staff(StewardId, TonightEventId, door: true, accepted: true, "Front door");
        Staff(StewardId, LastMonthEventId, door: true, accepted: true, "Front door");
        Staff(UnacceptedId, TonightEventId, door: true, accepted: false, "Front door");
        Staff(CookId, TonightEventId, door: false, accepted: true, "Kitchen");

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<IReadOnlyList<MyHostedEventDutyRecord>> DutiesOf(SqliteTestDb sqlite, Guid userId)
        => Assert.IsAssignableFrom<IReadOnlyList<MyHostedEventDutyRecord>>(
            Assert.IsType<OkObjectResult>((await Duties(sqlite, userId).Get(default)).Result).Value);

    // ── which doors ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_member_with_the_door_gets_every_door_still_to_run_and_never_a_draft_or_last_months()
    {
        await using var sqlite = await SeedAsync();
        var duties = await DutiesOf(sqlite, DoorMemberId);

        Assert.Equal([TonightEventId, VenueEventId], duties.Select(d => d.HostedEventId));
        Assert.True(duties[0].Tonight);
        Assert.False(duties[1].Tonight);
        Assert.Equal(OrgId, duties[0].OrganizationId);
    }

    [Fact]
    public async Task A_member_without_the_doors_permission_has_no_doors()
    {
        await using var sqlite = await SeedAsync();
        Assert.Empty(await DutiesOf(sqlite, PlainMemberId));
    }

    [Fact]
    public async Task A_helper_who_accepted_the_door_gets_that_door_with_their_part()
    {
        await using var sqlite = await SeedAsync();
        var duty = Assert.Single(await DutiesOf(sqlite, StewardId));
        Assert.Equal(TonightEventId, duty.HostedEventId);
        Assert.Equal("Front door", duty.RoleLabel);
    }

    [Fact]
    public async Task An_unanswered_invitation_or_a_helper_in_the_kitchen_has_no_door()
    {
        await using var sqlite = await SeedAsync();
        Assert.Empty(await DutiesOf(sqlite, UnacceptedId));
        Assert.Empty(await DutiesOf(sqlite, CookId));
    }

    [Fact]
    public async Task The_venues_porter_gets_the_door_only_where_the_venue_lent_its_staff()
    {
        await using var sqlite = await SeedAsync();
        var duty = Assert.Single(await DutiesOf(sqlite, PorterId));
        Assert.Equal(VenueEventId, duty.HostedEventId);

        await using (var db = await sqlite.NewContextAsync())
        {
            var grant = await db.OrganizationVenueGrants.SingleAsync();
            grant.RevokedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        Assert.Empty(await DutiesOf(sqlite, PorterId));
    }

    // ── an arrival kept offline ──────────────────────────────────────────────

    private static async Task<Guid> ConfirmedPartyAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = TonightEventId, LeadAppUserId = GuestId, PartySize = 3,
            Kind = HostedEventBookingKind.DayPass, DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
        };
        db.HostedEventBookings.Add(booking);
        BookingTransitions.Confirm(booking, HostId, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task<DateTime> ArrivedAsync(SqliteTestDb sqlite, Guid bookingId)
    {
        await using var db = await sqlite.NewContextAsync();
        return (await db.HostedEventCheckIns.SingleAsync(c => c.HostedEventBookingId == bookingId)).ArrivedUtc;
    }

    [Fact]
    public async Task An_arrival_sent_late_keeps_the_time_they_came_in()
    {
        await using var sqlite = await SeedAsync();
        var booking = await ConfirmedPartyAsync(sqlite);
        var cameIn = DateTime.UtcNow.AddMinutes(-50);

        Assert.IsType<OkObjectResult>((await Door(sqlite).Arrive(OrgId, TonightEventId,
            new HostedEventDoorMoveRequest(booking, TonightNightId, ArrivedUtc: cameIn), default)).Result);

        Assert.Equal(cameIn, await ArrivedAsync(sqlite, booking), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_phone_with_a_wrong_clock_cannot_post_date_or_back_date_an_arrival()
    {
        await using var sqlite = await SeedAsync();
        var booking = await ConfirmedPartyAsync(sqlite);

        await Door(sqlite).Arrive(OrgId, TonightEventId,
            new HostedEventDoorMoveRequest(booking, TonightNightId, ArrivedUtc: DateTime.UtcNow.AddHours(5)), default);
        Assert.Equal(DateTime.UtcNow, await ArrivedAsync(sqlite, booking), TimeSpan.FromSeconds(30));

        Assert.Equal(DateTime.UtcNow, DoorClock.ArrivedAt(DateTime.UtcNow.AddDays(-9), DateTime.UtcNow, DateTime.UtcNow.Date),
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task When_a_kept_arrival_is_earlier_than_the_one_recorded_since_the_earlier_one_wins()
    {
        await using var sqlite = await SeedAsync();
        var booking = await ConfirmedPartyAsync(sqlite);

        await Door(sqlite).Arrive(OrgId, TonightEventId, new HostedEventDoorMoveRequest(booking, TonightNightId), default);
        var cameIn = DateTime.UtcNow.AddMinutes(-40);
        await Door(sqlite).Arrive(OrgId, TonightEventId,
            new HostedEventDoorMoveRequest(booking, TonightNightId, ArrivedUtc: cameIn), default);

        Assert.Equal(cameIn, await ArrivedAsync(sqlite, booking), TimeSpan.FromSeconds(1));

        // And a later one never moves it forward.
        await Door(sqlite).Arrive(OrgId, TonightEventId,
            new HostedEventDoorMoveRequest(booking, TonightNightId, ArrivedUtc: DateTime.UtcNow.AddMinutes(-5)), default);
        Assert.Equal(cameIn, await ArrivedAsync(sqlite, booking), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_scan_sent_late_keeps_the_time_on_the_nights_arrival_and_on_the_pass()
    {
        await using var sqlite = await SeedAsync();
        var booking = await ConfirmedPartyAsync(sqlite);
        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventPasses.Add(new HostedEventPass
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking, Token = "kept-offline-token-0042",
                IssuedUtc = DateTime.UtcNow.AddDays(-3), DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
            });
            await db.SaveChangesAsync();
        }

        var security = Security();
        var email = new Mock<Ben.Data.Common.Interfaces.IEmailService>().Object;
        var site = Options.Create(new Ben.Data.Common.SiteIdentity { Name = "Test", BaseUrl = "https://test.local" });
        var controller = new HostedEventBookingController(
            sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object, new HostedEventCalendarSync(),
            new HostedEventAccess(security.Object), new EventGuestMailer(email, site, NullLogger<EventGuestMailer>.Instance),
            email, site, NullLogger<HostedEventBookingController>.Instance, new ForwardingOutboxQueue(email)) { ControllerContext = As(DoorMemberId) };

        var cameIn = DateTime.UtcNow.AddMinutes(-75);
        var result = Assert.IsType<HostedEventScanResult>(Assert.IsType<OkObjectResult>((await controller.Scan(OrgId, TonightEventId,
            new ScanHostedEventPassRequest("kept-offline-token-0042", HostedEventNightId: TonightNightId, ArrivedUtc: cameIn), default)).Result).Value);
        Assert.True(result.Admitted);

        Assert.Equal(cameIn, await ArrivedAsync(sqlite, booking), TimeSpan.FromSeconds(1));
        await using var check = await sqlite.NewContextAsync();
        Assert.Equal(cameIn, (await check.HostedEventPasses.SingleAsync()).CheckedInUtc!.Value, TimeSpan.FromSeconds(1));
    }
}
