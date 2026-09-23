using System.Security.Claims;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Web.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The nine hosted-event endpoints no test reached before the pre-merge audit (item 235 phase 17a, finding A1).
/// </summary>
/// <remarks>
/// Each test pins the behaviour that matters for its endpoint rather than its happy path alone: who may call it,
/// what it refuses in words, and the one rule that would hurt somebody if it broke.
/// </remarks>
public sealed class HostedEventUncoveredEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid OtherEventId = Guid.NewGuid();
    private static readonly Guid NightId = Guid.NewGuid();
    private static readonly Guid SeatId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid OtherGuestId = Guid.NewGuid();

    // ── the world ────────────────────────────────────────────────────────────

    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, first, last) in new[] { (HostId, "Hana", "Host"), (MemberId, "Mo", "Member"), (GuestId, "Grace", "Guest"), (OtherGuestId, "=HYPERLINK(\"x\")", "Other") })
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{first.Length}{id:N}@example.test", UserName = $"{id:N}@example.test",
                FirstName = first, LastName = last, DisplayName = $"{first} {last}", PhoneNumber = "615 555 0100", DateCreated = now,
            });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Organizations.Add(new Organization { Id = OtherOrgId, Name = "Elsewhere", UrlName = "elsewhere", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });

        foreach (var (id, name, org) in new[] { (EventId, "Seance Weekend", OrgId), (OtherEventId, "Another Weekend", OrgId) })
            db.HostedEvents.Add(new HostedEvent
            {
                Id = id, OrganizationId = org, PlaceId = PlaceId, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'),
                StartsOn = now.Date.AddDays(20), EndsOn = now.Date.AddDays(20), LifecycleState = HostedEventLifecycleState.Published,
                LayoutKind = HostedEventLayoutKind.Seats, BookingMode = HostedEventBookingMode.Pick, TimeZoneId = "America/Chicago",
                ProgrammePublishedUtc = id == EventId ? now : null,
                DateCreated = now, CreatedByAppUserId = HostId,
            });

        db.HostedEventNights.Add(new HostedEventNight { Id = NightId, HostedEventId = EventId, Date = now.Date.AddDays(20), DateCreated = now, CreatedByAppUserId = HostId });
        db.HostedEventLayoutUnits.Add(new HostedEventLayoutUnit { Id = SeatId, HostedEventId = EventId, Label = "A1", Capacity = 1, DateCreated = now, CreatedByAppUserId = HostId });

        db.HostedEventSessions.Add(new HostedEventSession
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Title = "Seance in the parlour", StartsAtUtc = now.Date.AddDays(20).AddHours(3),
            EndsAtUtc = now.Date.AddDays(20).AddHours(4), LocationText = "The parlour", DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventSessions.Add(new HostedEventSession
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Title = "Called off", StartsAtUtc = now.Date.AddDays(20).AddHours(5),
            EndsAtUtc = now.Date.AddDays(20).AddHours(6), CalledOffUtc = now, DateCreated = now, CreatedByAppUserId = HostId,
        });

        db.UserPhoneTypes.Add(new UserPhoneType { Id = Guid.NewGuid(), Name = "Mobile", DateCreated = now, CreatedByAppUserId = HostId });
        await db.SaveChangesAsync();
        db.UserPhones.Add(new UserPhone
        {
            Id = Guid.NewGuid(), AppUserId = GuestId, UserPhoneTypeId = db.UserPhoneTypes.Local.First().Id, PhoneNumber = "615 555 0199",
            IsPrimary = true, ValidationToken = "", DateCreated = now, CreatedByAppUserId = GuestId,
        });
        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<Guid> BookAsync(SqliteTestDb sqlite, Guid lead, HostedEventBookingStatus status, Guid eventId,
        DateTime? holdExpires = null, string? dietary = null)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, LeadAppUserId = lead, PartySize = 2, Kind = HostedEventBookingKind.DayPass,
            Status = status, HoldExpiresUtc = holdExpires, ContactPhone = "+1 615 555 0142",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = lead,
        };
        if (eventId == EventId)
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, HostedEventNightId = NightId, HostedEventLayoutUnitId = SeatId,
                DateCreated = DateTime.UtcNow,
            });
        if (dietary is not null)
            booking.Guests.Add(new HostedEventBookingGuest { Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, DisplayName = "Grace", DietaryNotes = dietary, DateCreated = DateTime.UtcNow });
        db.HostedEventBookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    /// <summary>The group's permissions: the host may do anything; the plain member may not read bookings.</summary>
    private static Mock<IOrganizationSecurityService> Security()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _) => user == HostId);
        return security;
    }

    private static ControllerContext As(Guid who) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, who.ToString())], "Bearer")),
        },
    };

    private static EventGuestMailer Mailer()
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        return new EventGuestMailer(email.Object, Options.Create(new Ben.Data.Common.SiteIdentity { Name = "IsHaunted", BaseUrl = "https://test.local" }),
            NullLogger<EventGuestMailer>.Instance);
    }

    private static HostedEventBookingController Board(SqliteTestDb sqlite, Guid who)
    {
        var security = Security();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        return new HostedEventBookingController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventCalendarSync(), new HostedEventAccess(security.Object), Mailer(), email.Object,
            Options.Create(new Ben.Data.Common.SiteIdentity { Name = "IsHaunted" }), NullLogger<HostedEventBookingController>.Instance, new ForwardingOutboxQueue(email.Object))
        { ControllerContext = As(who) };
    }

    private static HostedEventAfterController After(SqliteTestDb sqlite, Guid who)
    {
        var security = Security();
        return new HostedEventAfterController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object), new HostedEventCalendarSync(), new Mock<IMediaIngestService>().Object,
            new Mock<IFileStorageService>().Object, NullLogger<HostedEventAfterController>.Instance)
        { ControllerContext = As(who) };
    }

    // ── the board ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_board_is_the_hosts_and_a_plain_member_is_refused_it()
    {
        await using var sqlite = await SeedAsync();
        await BookAsync(sqlite, GuestId, HostedEventBookingStatus.Confirmed, EventId);

        Assert.IsType<ForbidResult>((await Board(sqlite, MemberId).GetBoard(OrgId, EventId, default)).Result);

        var board = Assert.IsType<HostedEventBookingBoardRecord>(Assert.IsType<OkObjectResult>(
            (await Board(sqlite, HostId).GetBoard(OrgId, EventId, default)).Result).Value);
        Assert.Contains(board.Bookings, b => b.PartySize == 2);
        Assert.IsType<NotFoundResult>((await Board(sqlite, HostId).GetBoard(OtherOrgId, EventId, default)).Result);
    }

    [Fact]
    public async Task Confirming_a_request_puts_the_party_where_the_host_chose_and_issues_the_pass()
    {
        // The commonest decision a host makes: somebody asked with no preference, and the host places them. The new
        // night row once went in as an update of a row that did not exist, and every such confirmation failed.
        await using var sqlite = await SeedAsync();
        Guid bookingId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId, PartySize = 1, Kind = HostedEventBookingKind.Overnight,
                Status = HostedEventBookingStatus.Requested, DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
            };
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, HostedEventNightId = NightId, DateCreated = DateTime.UtcNow,
            });
            db.HostedEventBookings.Add(booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var confirmed = await Board(sqlite, HostId).Confirm(OrgId, EventId, bookingId,
            new ConfirmHostedEventBookingRequest([new HostedEventBookingNightChoice(NightId, SeatId)], "See you at seven."), default);

        Assert.IsType<OkObjectResult>(confirmed.Result);
        await using var check = await sqlite.NewContextAsync();
        var saved = await check.HostedEventBookings.Include(b => b.Nights).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(HostedEventBookingStatus.Confirmed, saved.Status);
        Assert.Equal(SeatId, Assert.Single(saved.Nights).HostedEventLayoutUnitId);
        Assert.True(await check.HostedEventPasses.AnyAsync(p => p.HostedEventBookingId == bookingId && p.RevokedUtc == null));
    }

    [Fact]
    public async Task A_guest_changing_their_nights_and_guests_keeps_the_new_ones()
    {
        // The same trap as confirming, from the guest's side: the booking already exists, so the rows written for it
        // must be added, not taken for existing rows.
        await using var sqlite = await SeedAsync();
        await BookAsync(sqlite, GuestId, HostedEventBookingStatus.Requested, EventId, dietary: "No nuts");
        var guest = new PublicHostedEventBookingController(sqlite.Factory, new HostedEventCalendarSync()) { ControllerContext = As(GuestId) };

        var changed = await guest.UpdateMyBooking(EventId, new EditHostedEventBookingRequest(
            Nights: [new HostedEventBookingNightChoice(NightId)],
            Guests: [new HostedEventBookingGuestInput("Grace", null, "Vegetarian"), new HostedEventBookingGuestInput("Sam", null, null)]), default);

        Assert.IsType<OkObjectResult>(changed.Result);
        await using var db = await sqlite.NewContextAsync();
        var saved = await db.HostedEventBookings.Include(b => b.Nights).Include(b => b.Guests).SingleAsync(b => b.LeadAppUserId == GuestId && b.HostedEventId == EventId);
        Assert.Null(Assert.Single(saved.Nights).HostedEventLayoutUnitId);
        Assert.Equal(["Grace", "Sam"], saved.Guests.OrderBy(g => g.SortOrder).Select(g => g.DisplayName));
    }

    [Fact]
    public async Task Releasing_lapsed_holds_releases_only_the_ones_that_lapsed()
    {
        await using var sqlite = await SeedAsync();
        var lapsed = await BookAsync(sqlite, GuestId, HostedEventBookingStatus.Held, EventId, holdExpires: DateTime.UtcNow.AddMinutes(-5));
        var live = await BookAsync(sqlite, OtherGuestId, HostedEventBookingStatus.Held, OtherEventId, holdExpires: DateTime.UtcNow.AddHours(5));

        Assert.IsType<ForbidResult>((await Board(sqlite, MemberId).ReleaseLapsedHolds(OrgId, EventId, default)).Result);
        Assert.IsType<OkObjectResult>((await Board(sqlite, HostId).ReleaseLapsedHolds(OrgId, EventId, default)).Result);

        await using var db = await sqlite.NewContextAsync();
        var gone = await db.HostedEventBookings.Include(b => b.Nights).SingleAsync(b => b.Id == lapsed);
        Assert.Equal(HostedEventBookingStatus.Expired, gone.Status);
        Assert.All(gone.Nights, n => Assert.NotNull(n.ReleasedUtc));
        Assert.Equal(HostedEventBookingStatus.Held, (await db.HostedEventBookings.SingleAsync(b => b.Id == live)).Status);
    }

    [Fact]
    public async Task Reissuing_a_pass_withdraws_the_old_code_and_refuses_a_booking_not_yet_confirmed()
    {
        await using var sqlite = await SeedAsync();
        var confirmed = await BookAsync(sqlite, GuestId, HostedEventBookingStatus.Confirmed, EventId);
        var asked = await BookAsync(sqlite, OtherGuestId, HostedEventBookingStatus.Requested, EventId);
        Assert.IsType<OkObjectResult>((await Board(sqlite, HostId).IssuePass(OrgId, EventId, confirmed, default)).Result);
        string firstToken;
        await using (var db = await sqlite.NewContextAsync())
            firstToken = (await db.HostedEventPasses.SingleAsync(p => p.HostedEventBookingId == confirmed)).Token;

        var fresh = Assert.IsType<HostedEventPassRecord>(Assert.IsType<OkObjectResult>(
            (await Board(sqlite, HostId).ReissuePass(OrgId, EventId, confirmed, new RevokeHostedEventPassRequest("Lost phone"), default)).Result).Value);
        Assert.NotEqual(firstToken, fresh.Token);

        await using (var db = await sqlite.NewContextAsync())
        {
            var passes = await db.HostedEventPasses.Where(p => p.HostedEventBookingId == confirmed).ToListAsync();
            Assert.Equal(2, passes.Count);
            Assert.NotNull(passes.Single(p => p.Token == firstToken).RevokedUtc);
            Assert.Null(passes.Single(p => p.Token == fresh.Token).RevokedUtc);
        }

        var refused = Assert.IsType<BadRequestObjectResult>((await Board(sqlite, HostId).ReissuePass(OrgId, EventId, asked, null, default)).Result);
        Assert.Contains("Confirm the booking first", refused.Value as string);
    }

    // ── after the event ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_spreadsheet_is_this_events_bookings_with_formulas_defused_and_is_refused_to_a_plain_member()
    {
        await using var sqlite = await SeedAsync();
        await BookAsync(sqlite, GuestId, HostedEventBookingStatus.Confirmed, EventId, dietary: "no nuts");
        await BookAsync(sqlite, OtherGuestId, HostedEventBookingStatus.Requested, EventId);
        await BookAsync(sqlite, GuestId, HostedEventBookingStatus.Confirmed, OtherEventId);

        Assert.IsType<ForbidResult>(await After(sqlite, MemberId).ExportBookings(OrgId, EventId, default));

        var file = Assert.IsType<FileContentResult>(await After(sqlite, HostId).ExportBookings(OrgId, EventId, default));
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("seance-weekend-bookings.csv", file.FileDownloadName);
        Assert.True(file.FileContents.Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()), "Excel needs the BOM to read accents.");

        var lines = Encoding.UTF8.GetString(file.FileContents[3..]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Status,First name,Last name", lines[0]);
        Assert.Equal(3, lines.Length); // header and this event's two bookings, not the other event's
        Assert.Contains(lines, l => l.Contains("Grace: no nuts"));
        // A name a spreadsheet would run as a formula is written as text.
        Assert.Contains(lines, l => l.Contains("\"'=HYPERLINK(\"\"x\"\")\""));
        Assert.DoesNotContain(lines, l => l.Contains(",=HYPERLINK"));
    }

    [Fact]
    public async Task Keeping_files_asks_for_at_least_one_and_at_most_a_hundred_and_ignores_anything_not_offered()
    {
        await using var sqlite = await SeedAsync();

        var none = Assert.IsType<BadRequestObjectResult>(await After(sqlite, HostId).KeepZip(OrgId, EventId, "", default));
        Assert.Contains("at least one", none.Value as string);

        var tooMany = string.Join(",", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));
        var capped = Assert.IsType<BadRequestObjectResult>(await After(sqlite, HostId).KeepZip(OrgId, EventId, tooMany, default));
        Assert.Contains("up to 100", capped.Value as string);

        Assert.IsType<NotFoundResult>(await After(sqlite, HostId).KeepZip(OrgId, EventId, Guid.NewGuid().ToString(), default));
        Assert.IsType<ForbidResult>(await After(sqlite, MemberId).KeepZip(OrgId, EventId, Guid.NewGuid().ToString(), default));
    }

    // ── staff, the venue, the guest ──────────────────────────────────────────

    [Fact]
    public async Task Adding_a_helper_by_address_is_an_invitation_that_grants_nothing_until_accepted()
    {
        await using var sqlite = await SeedAsync();
        HostedEventStaffController Staff(Guid who)
        {
            var security = Security();
            return new HostedEventStaffController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
                new HostedEventAccess(security.Object), Mailer()) { ControllerContext = As(who) };
        }

        Assert.IsType<ForbidResult>((await Staff(MemberId).Save(OrgId, EventId, new SaveHostedEventStaffRequest(Email: "door@example.test", RunsTheDoor: true), default)).Result);
        Assert.Contains("look right", Assert.IsType<BadRequestObjectResult>(
            (await Staff(HostId).Save(OrgId, EventId, new SaveHostedEventStaffRequest(Email: "not-an-address", RunsTheDoor: true), default)).Result).Value as string);
        Assert.Contains("Say what they may do", Assert.IsType<BadRequestObjectResult>(
            (await Staff(HostId).Save(OrgId, EventId, new SaveHostedEventStaffRequest(Email: "door@example.test"), default)).Result).Value as string);

        Assert.IsType<OkObjectResult>((await Staff(HostId).Save(OrgId, EventId,
            new SaveHostedEventStaffRequest(Email: " Door@Example.test ", RoleLabel: "Front door", RunsTheDoor: true), default)).Result);

        await using var db = await sqlite.NewContextAsync();
        var row = await db.HostedEventStaff.SingleAsync();
        Assert.Equal("door@example.test", row.Email);
        Assert.Null(row.AppUserId);
        Assert.Null(row.DateConfirmed);
        Assert.False(string.IsNullOrEmpty(row.Token));
        Assert.True(row.RunsTheDoor);
    }

    [Fact]
    public async Task A_venue_page_can_be_saved_privately_but_not_published_before_the_venue_is_confirmed()
    {
        await using var sqlite = await SeedAsync();
        VenueProfileController Venue(Guid who) => new(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, Security().Object) { ControllerContext = As(who) };

        Assert.IsType<ForbidResult>((await Venue(MemberId).Save(OrgId, new SaveVenueProfileRequest(PlaceId, "History", null, 20, false), default)).Result);
        Assert.Contains("sensible number", Assert.IsType<BadRequestObjectResult>(
            (await Venue(HostId).Save(OrgId, new SaveVenueProfileRequest(PlaceId, null, null, 0, false), default)).Result).Value as string);

        Assert.IsType<OkObjectResult>((await Venue(HostId).Save(OrgId, new SaveVenueProfileRequest(PlaceId, "  Built in 1890.  ", null, 20, false), default)).Result);
        var publish = Assert.IsType<ConflictObjectResult>((await Venue(HostId).Save(OrgId, new SaveVenueProfileRequest(PlaceId, "Built in 1890.", null, 20, true), default)).Result);
        Assert.Contains("confirmed as the venue", publish.Value as string);

        await using var db = await sqlite.NewContextAsync();
        var profile = await db.OrganizationVenueProfiles.SingleAsync();
        Assert.Equal("Built in 1890.", profile.History);
        Assert.False(profile.IsPublished);
    }

    [Fact]
    public async Task A_guests_contact_details_prefill_with_their_listed_phone_before_the_account_one()
    {
        await using var sqlite = await SeedAsync();
        var controller = new PublicHostedEventBookingController(sqlite.Factory, new HostedEventCalendarSync()) { ControllerContext = As(GuestId) };

        var contact = Assert.IsType<BookingContactRecord>(Assert.IsType<OkObjectResult>((await controller.GetMyContact(default)).Result).Value);
        Assert.Equal("Grace", contact.FirstName);
        Assert.Equal("Guest", contact.LastName);
        Assert.Equal("615 555 0199", contact.Phone);

        var stranger = new PublicHostedEventBookingController(sqlite.Factory, new HostedEventCalendarSync())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedResult>((await stranger.GetMyContact(default)).Result);
    }

    [Fact]
    public async Task The_whole_programme_as_a_calendar_file_leaves_out_called_off_sessions_and_unpublished_programmes()
    {
        await using var sqlite = await SeedAsync();
        var controller = new PublicHostedEventProgrammeController(sqlite.Factory)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var file = Assert.IsType<FileContentResult>(await controller.ProgrammeCalendar(EventId, default));
        Assert.Equal("text/calendar", file.ContentType.Split(';')[0]);
        var ics = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Seance in the parlour", ics);
        Assert.DoesNotContain("Called off", ics);

        Assert.IsType<NotFoundResult>(await controller.ProgrammeCalendar(OtherEventId, default));
    }
}
