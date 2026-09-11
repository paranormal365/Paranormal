using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Asking for a seat on a tour date, and the business deciding (item 234, Ben 2026-09-10).
/// </summary>
/// <remarks>
/// <para>Ben: <i>"They would not be confirmed until the tour guide or manager approves them meaning
/// they have settled how money will be or has been exchanged."</i> <b>This site never takes the
/// money.</b> Everything here is the two of them agreeing, and no state in it is a payment record.
/// </para>
///
/// <para>The properties these hold: a tour date takes a REQUEST that holds no place, an ordinary
/// event is completely unchanged, an approval is refused in words that name the places left, and
/// the tour's own welcome mail waits for the approval rather than going out on the request — a
/// guest who acted on "here is where to stand" before anybody agreed would turn up to a walk with
/// no place reserved for them.</para>
/// </remarks>
public sealed class TourSeatFlowTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ControllerContext As(Guid userId)
        => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = userId == Guid.Empty
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };

    private static PublicEventController PublicAs(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), Support.SilentTourMail.Instance)
        { ControllerContext = As(userId) };

    /// <summary>The org-side controller, with a mapper that carries the seat fields through.</summary>
    private static OrgCalendarEventController BusinessAs(IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<OrgCalendarEventAttendeeRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrgCalendarEventAttendee a
                ? new OrgCalendarEventAttendeeRecord
                {
                    Id = a.Id, AppUserId = a.AppUserId, RsvpStatus = a.RsvpStatus,
                    SeatStatus = a.SeatStatus, Seats = a.Seats,
                    SeatDecidedUtc = a.SeatDecidedUtc,
                    GuestAcknowledgedUtc = a.GuestAcknowledgedUtc,
                }
                : new OrgCalendarEventAttendeeRecord());

        var email = new Mock<Ben.Data.Common.Interfaces.IEmailService>();
        email.SetupGet(x => x.IsConfigured).Returns(false);

        return new OrgCalendarEventController(
            f, mapper.Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f),
            email.Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer(),
            Support.SilentTourMail.Instance)
        { ControllerContext = As(userId) };
    }

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid OwnerId,
        Guid TourDateId, Guid PlainDateId);

    /// <summary>
    /// A tour business with one walk on the calendar, and one ordinary evening beside it.
    /// </summary>
    /// <remarks>
    /// The ordinary evening is not decoration: the rule must not turn a group's open night into a
    /// thing that needs approving, and the only way to know is to run one through the same doors.
    /// </remarks>
    private static async Task<World> SeedAsync(int? capacity = 10)
    {
        var factory = Factory();
        await using var db = factory.CreateDbContext();

        var owner = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = "printers-alley-walks",
            Kind = OrganizationKind.GhostWalkingTour, RunsPublicTours = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = owner,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });

        var tourId = Guid.NewGuid();
        db.Tours.Add(new Tour
        {
            Id = tourId, OrganizationId = orgId, Name = "Printers Alley Ghost Walk",
            UrlName = "printers-alley-ghost-walk", TimeZoneId = "America/Chicago",
            DurationMinutes = 90, DefaultCapacity = 20, IsBookable = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });

        var tourDate = Guid.NewGuid();
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = tourDate, OrganizationId = orgId, TourId = tourId,
            Title = "Saturday walk", IsPublic = true,
            StartDateTime = DateTime.UtcNow.AddDays(7),
            EndDateTime = DateTime.UtcNow.AddDays(7).AddMinutes(90),
            AttendeeCapacity = capacity,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });

        var plainDate = Guid.NewGuid();
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = plainDate, OrganizationId = orgId,
            Title = "Open evening", IsPublic = true,
            StartDateTime = DateTime.UtcNow.AddDays(7),
            EndDateTime = DateTime.UtcNow.AddDays(7).AddHours(2),
            AttendeeCapacity = capacity,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
        });

        await db.SaveChangesAsync();
        return new World(factory, orgId, owner, tourDate, plainDate);
    }

    private static async Task<Guid> GuestAsync(World world, string handle)
    {
        await using var db = world.Factory.CreateDbContext();
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id, UserName = $"{handle}@test.com", Email = $"{handle}@test.com",
            NormalizedEmail = $"{handle}@TEST.COM", NormalizedUserName = $"{handle}@TEST.COM",
            DisplayName = handle, Handle = handle, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<OrgCalendarEventAttendee?> SeatOfAsync(World world, Guid guestId, Guid eventId)
    {
        await using var db = world.Factory.CreateDbContext();
        return await db.OrgCalendarEventAttendees.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AppUserId == guestId && a.OrgCalendarEventId == eventId);
    }

    // ── Asking ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_up_for_a_walk_asks_rather_than_takes_a_place()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");

        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default, seats: 3);

        var seat = await SeatOfAsync(world, guest, world.TourDateId);
        Assert.NotNull(seat);
        Assert.Equal(TourSeatStatus.Requested, seat!.SeatStatus);
        // Invited, not Accepted — which is what keeps the place unheld everywhere else in the
        // codebase without one of those places having to learn what a tour is.
        Assert.Equal(RsvpStatus.Invited, seat.RsvpStatus);
        Assert.Equal(3, seat.Seats);
    }

    [Fact]
    public async Task An_ordinary_evening_is_completely_unchanged()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "marcus");

        await PublicAs(world.Factory, guest).Rsvp(world.PlainDateId, default, seats: 4);

        var seat = await SeatOfAsync(world, guest, world.PlainDateId);
        Assert.NotNull(seat);
        Assert.Null(seat!.SeatStatus);              // no seat machinery at all
        Assert.Equal(RsvpStatus.Accepted, seat.RsvpStatus);
        Assert.Equal(1, seat.Seats);                // and one person is one place
    }

    [Fact]
    public async Task A_full_walk_still_takes_requests()
    {
        // Ben's model is approval, so the overflow is a waiting list the business works through
        // rather than a door that shuts. A full ORDINARY event still refuses, as it always did.
        var world = await SeedAsync(capacity: 1);
        var first = await GuestAsync(world, "first");
        var second = await GuestAsync(world, "second");

        await PublicAs(world.Factory, first).Rsvp(world.TourDateId, default);
        await using (var db = world.Factory.CreateDbContext())
        {
            var seat = await db.OrgCalendarEventAttendees.FirstAsync(a => a.AppUserId == first);
            seat.RsvpStatus = RsvpStatus.Accepted;      // approved, so the one place is gone
            seat.SeatStatus = TourSeatStatus.Reserved;
            await db.SaveChangesAsync();
        }

        var result = await PublicAs(world.Factory, second).Rsvp(world.TourDateId, default);
        Assert.IsNotType<ConflictObjectResult>(result.Result);
        Assert.Equal(TourSeatStatus.Requested, (await SeatOfAsync(world, second, world.TourDateId))!.SeatStatus);
    }

    [Fact]
    public async Task A_full_ordinary_event_still_refuses()
    {
        var world = await SeedAsync(capacity: 1);
        var first = await GuestAsync(world, "first");
        var second = await GuestAsync(world, "second");

        await PublicAs(world.Factory, first).Rsvp(world.PlainDateId, default);
        var result = await PublicAs(world.Factory, second).Rsvp(world.PlainDateId, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("This event is full.", conflict.Value);
    }

    [Fact]
    public async Task Asking_twice_is_one_request()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        var page = PublicAs(world.Factory, guest);

        await page.Rsvp(world.TourDateId, default, seats: 2);
        await page.Rsvp(world.TourDateId, default, seats: 5);

        await using var db = world.Factory.CreateDbContext();
        var seats = await db.OrgCalendarEventAttendees
            .Where(a => a.OrgCalendarEventId == world.TourDateId).ToListAsync();
        var only = Assert.Single(seats);
        // The first number stands: the second press is the same person asking again, not them
        // revising a party size upward after the business has started looking at it.
        Assert.Equal(2, only.Seats);
    }

    // ── Deciding ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approving_holds_the_places()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default, seats: 3);

        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;
        var result = await BusinessAs(world.Factory, world.OwnerId)
            .ApproveSeat(world.OrgId, world.TourDateId, seatId, default);

        Assert.IsType<OkObjectResult>(result.Result);
        var seat = await SeatOfAsync(world, guest, world.TourDateId);
        Assert.Equal(TourSeatStatus.Reserved, seat!.SeatStatus);
        Assert.Equal(RsvpStatus.Accepted, seat.RsvpStatus);
        Assert.Equal(world.OwnerId, seat.SeatDecidedByAppUserId);
        Assert.NotNull(seat.SeatDecidedUtc);
    }

    [Fact]
    public async Task An_approval_that_would_overfill_is_refused_in_words()
    {
        var world = await SeedAsync(capacity: 4);
        var party = await GuestAsync(world, "party");
        await PublicAs(world.Factory, party).Rsvp(world.TourDateId, default, seats: 5);

        var seatId = (await SeatOfAsync(world, party, world.TourDateId))!.Id;
        var result = await BusinessAs(world.Factory, world.OwnerId)
            .ApproveSeat(world.OrgId, world.TourDateId, seatId, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("4 places left", (string)conflict.Value!);
        // And nothing moved: the request is still waiting on somebody.
        Assert.Equal(TourSeatStatus.Requested, (await SeatOfAsync(world, party, world.TourDateId))!.SeatStatus);
    }

    [Fact]
    public async Task Turning_a_seat_down_is_recorded_rather_than_deleted()
    {
        // A guest who is not coming has to be able to SEE that they are not coming. A row that
        // vanishes reads to them as a request that was never received.
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default);

        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;
        await BusinessAs(world.Factory, world.OwnerId)
            .TurnDownSeat(world.OrgId, world.TourDateId, seatId, default);

        var seat = await SeatOfAsync(world, guest, world.TourDateId);
        Assert.NotNull(seat);
        Assert.Equal(TourSeatStatus.TurnedDown, seat!.SeatStatus);
        Assert.Equal(RsvpStatus.Declined, seat.RsvpStatus);
    }

    [Fact]
    public async Task Nobody_outside_the_business_decides_a_seat()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default);
        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;

        var stranger = BusinessAs(world.Factory, guest);   // the guest, approving their own seat
        Assert.IsType<ForbidResult>(
            (await stranger.ApproveSeat(world.OrgId, world.TourDateId, seatId, default)).Result);
        Assert.IsType<ForbidResult>(
            (await stranger.TurnDownSeat(world.OrgId, world.TourDateId, seatId, default)).Result);
    }

    // ── Acknowledging ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_guest_can_say_back_that_they_know()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default);
        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;
        await BusinessAs(world.Factory, world.OwnerId)
            .ApproveSeat(world.OrgId, world.TourDateId, seatId, default);

        var result = await PublicAs(world.Factory, guest).AcknowledgeSeat(world.TourDateId, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull((await SeatOfAsync(world, guest, world.TourDateId))!.GuestAcknowledgedUtc);
    }

    [Fact]
    public async Task There_is_nothing_to_acknowledge_until_somebody_approves_it()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default);

        var result = await PublicAs(world.Factory, guest).AcknowledgeSeat(world.TourDateId, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("The tour hasn't answered this one yet.", conflict.Value);
    }

    [Fact]
    public async Task A_refusal_can_be_acknowledged_too()
    {
        // Otherwise it counts on the guest's bell for ever: the acknowledgement is the only thing
        // that clears that bucket, and reserved-only left a turned-down seat with no way out.
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default);
        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;
        await BusinessAs(world.Factory, world.OwnerId)
            .TurnDownSeat(world.OrgId, world.TourDateId, seatId, default);

        var result = await PublicAs(world.Factory, guest).AcknowledgeSeat(world.TourDateId, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull((await SeatOfAsync(world, guest, world.TourDateId))!.GuestAcknowledgedUtc);
    }

    // ── What the page is told ────────────────────────────────────────────────

    [Fact]
    public async Task A_waiting_guest_is_told_it_is_with_the_tour_rather_than_offered_the_button()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default, seats: 2);

        var read = await PublicAs(world.Factory, guest).GetEvent(world.TourDateId, default);
        var page = (PublicEventRecord)Assert.IsType<OkObjectResult>(read.Result).Value!;

        Assert.False(page.Flags.CanRsvp);
        Assert.Equal("Your seat is with the tour — they'll confirm it.", page.Flags.RsvpBlockedReason);
        Assert.Equal(TourSeatStatus.Requested, page.MySeat!.Status);
        Assert.Equal(2, page.MySeat.Seats);
        // Nothing is held yet, so the walk still reads as empty.
        Assert.Equal(0, page.AttendingCount);
    }

    [Fact]
    public async Task A_turned_down_guest_is_told_so_rather_than_left_guessing()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default);
        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;
        await BusinessAs(world.Factory, world.OwnerId)
            .TurnDownSeat(world.OrgId, world.TourDateId, seatId, default);

        var read = await PublicAs(world.Factory, guest).GetEvent(world.TourDateId, default);
        var page = (PublicEventRecord)Assert.IsType<OkObjectResult>(read.Result).Value!;

        Assert.Equal(TourSeatStatus.TurnedDown, page.MySeat!.Status);
        Assert.False(page.Flags.CanRsvp);
        Assert.Equal("The tour couldn't take this booking.", page.Flags.RsvpBlockedReason);
    }

    [Fact]
    public async Task An_approved_party_counts_as_its_places()
    {
        var world = await SeedAsync();
        var guest = await GuestAsync(world, "sarah");
        await PublicAs(world.Factory, guest).Rsvp(world.TourDateId, default, seats: 4);
        var seatId = (await SeatOfAsync(world, guest, world.TourDateId))!.Id;
        await BusinessAs(world.Factory, world.OwnerId)
            .ApproveSeat(world.OrgId, world.TourDateId, seatId, default);

        var read = await PublicAs(world.Factory, Guid.NewGuid()).GetEvent(world.TourDateId, default);
        var page = (PublicEventRecord)Assert.IsType<OkObjectResult>(read.Result).Value!;

        // Four places gone off a walk of ten, from ONE sign-up.
        Assert.Equal(4, page.AttendingCount);
    }
}
