using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// What Home counts down to: the next things the signed-in person is going to (10/16/2026).
/// </summary>
/// <remarks>
/// One read gathers four sources, and each keeps the rule of the screen that owns it — /my-events
/// for hosted bookings, the RSVP and the tour seat for calendar events, the member desk for
/// investigations. These pin what is in, what is out, and the words each one is labeled with.
/// </remarks>
public sealed class MyNextEventsControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();
    private const string Chicago = "America/Chicago";

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MyNextEventsController Build(IDbContextFactory<BenDataContext> f, Guid? user = null)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, (user ?? Me).ToString())], "Bearer")),
                },
            },
        };

    private static async Task<MyNextEventsResponse> GetAsync(IDbContextFactory<BenDataContext> f)
    {
        var result = await Build(f).Get(default);
        return Assert.IsType<MyNextEventsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private sealed class World
    {
        public required IDbContextFactory<BenDataContext> Factory { get; init; }
        public required BenDataContext Db { get; init; }
        public Guid PlaceId { get; init; }
    }

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        var db = await factory.CreateDbContextAsync();
        foreach (var (id, name) in new[] { (Me, "Me"), (Host, "Host") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
        db.Organizations.Add(new Organization { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = OtherOrgId, Name = "Elsewhere", UrlName = "elsewhere", DateCreated = DateTime.UtcNow });
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
        });
        await db.SaveChangesAsync();
        return new World { Factory = factory, Db = db, PlaceId = placeId };
    }

    private static Guid AddCalendarEvent(World w, string title, DateTime start, bool isPublic = true,
        Guid? org = null, RsvpStatus? myRsvp = RsvpStatus.Accepted, TourSeatStatus? seat = null, Guid? hostedEventId = null, Guid? tourId = null)
    {
        var id = Guid.NewGuid();
        w.Db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = id, OrganizationId = org ?? OrgId, Title = title, UrlName = isPublic ? $"slug-{id:N}" : null,
            StartDateTime = start, EndDateTime = start.AddHours(2), IsPublic = isPublic, PlaceId = w.PlaceId,
            TimeZoneId = Chicago, HostedEventId = hostedEventId, TourId = tourId,
            DateCreated = DateTime.UtcNow.AddDays(-10), CreatedByAppUserId = Host,
        });
        if (myRsvp is { } rsvp)
            w.Db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = id, AppUserId = Me, RsvpStatus = rsvp, SeatStatus = seat,
                Seats = 1, DateCreated = DateTime.UtcNow.AddDays(-2), CreatedByAppUserId = Me,
            });
        return id;
    }

    private static Guid AddHostedBooking(World w, string name, int daysAhead, HostedEventBookingStatus status,
        HostedEventLifecycleState state = HostedEventLifecycleState.Published)
    {
        var eventId = Guid.NewGuid();
        var day = DateTime.UtcNow.Date.AddDays(daysAhead);
        w.Db.HostedEvents.Add(new HostedEvent
        {
            Id = eventId, OrganizationId = OrgId, PlaceId = w.PlaceId, Name = name, UrlName = $"hosted-{eventId:N}",
            TimeZoneId = Chicago, StartsOn = day, EndsOn = day, LifecycleState = state,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
        });
        var nightId = Guid.NewGuid();
        w.Db.HostedEventNights.Add(new HostedEventNight
        {
            Id = nightId, HostedEventId = eventId, Date = day, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
        });
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, LeadAppUserId = Me, PartySize = 2,
            Kind = HostedEventBookingKind.Overnight, Status = status,
            DateCreated = DateTime.UtcNow.AddDays(-3), CreatedByAppUserId = Me,
        };
        booking.Nights.Add(new HostedEventBookingNight
        {
            Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, HostedEventNightId = nightId, DateCreated = DateTime.UtcNow,
        });
        w.Db.HostedEventBookings.Add(booking);
        return eventId;
    }

    private static void AddInvestigation(World w, string title, DateTime start, RsvpStatus rsvp,
        InvestigationStatus status = InvestigationStatus.Scheduled)
    {
        var id = Guid.NewGuid();
        w.Db.Investigations.Add(new Investigation
        {
            Id = id, OrganizationId = OrgId, Title = title, UrlName = $"inv-{id:N}", ScheduledDateTime = start,
            Status = status, DateCreated = DateTime.UtcNow,
        });
        w.Db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = id, AppUserId = Me, Rsvp = rsvp, DateCreated = DateTime.UtcNow,
        });
    }

    // ── What counts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Nothing_coming_up_is_an_empty_answer()
    {
        var w = await SeedAsync();
        var answer = await GetAsync(w.Factory);
        Assert.Empty(answer.Items);
        Assert.Equal(0, answer.Total);
    }

    [Fact]
    public async Task All_four_kinds_are_gathered_soonest_first()
    {
        var w = await SeedAsync();
        var now = DateTime.UtcNow;
        AddCalendarEvent(w, "Ghost Walk", now.AddDays(3));
        AddHostedBooking(w, "Seance Weekend", 6, HostedEventBookingStatus.Confirmed);
        AddInvestigation(w, "The Old Mill", now.AddDays(1), RsvpStatus.Accepted);
        var tourId = Guid.NewGuid();
        w.Db.Tours.Add(new Tour { Id = tourId, OrganizationId = OrgId, Name = "Printers Alley", UrlName = "printers-alley", TimeZoneId = Chicago, DateCreated = now, CreatedByAppUserId = Host });
        AddCalendarEvent(w, "Printers Alley Walk", now.AddDays(2), myRsvp: RsvpStatus.Accepted, seat: TourSeatStatus.Reserved, tourId: tourId);
        await w.Db.SaveChangesAsync();

        var answer = await GetAsync(w.Factory);

        Assert.Equal(["The Old Mill", "Printers Alley Walk", "Ghost Walk", "Seance Weekend"], answer.Items.Select(i => i.Title));
        Assert.Equal(
            [MyNextEventKind.Investigation, MyNextEventKind.TourDate, MyNextEventKind.Event, MyNextEventKind.HostedEvent],
            answer.Items.Select(i => i.Kind));
        Assert.Equal(4, answer.Total);
    }

    [Fact]
    public async Task A_confirmed_booking_opens_the_pass_and_says_youre_coming()
    {
        var w = await SeedAsync();
        var eventId = AddHostedBooking(w, "Seance Weekend", 4, HostedEventBookingStatus.Confirmed);
        await w.Db.SaveChangesAsync();

        var item = Assert.Single((await GetAsync(w.Factory)).Items);
        Assert.Equal($"/my-events/{eventId}/pass", item.Href);
        Assert.Equal(MyNextEventStanding.Going, item.Standing);
        Assert.Equal(Chicago, item.TimeZoneId);
    }

    /// <summary>
    /// The night's start is the event's, in its own zone: doors at seven Central unless a night says
    /// otherwise — the same window the event's calendar row carries.
    /// </summary>
    [Fact]
    public async Task A_booking_starts_at_seven_on_its_nights_clock()
    {
        var w = await SeedAsync();
        AddHostedBooking(w, "Seance Weekend", 4, HostedEventBookingStatus.Confirmed);
        await w.Db.SaveChangesAsync();

        var item = Assert.Single((await GetAsync(w.Factory)).Items);
        var local = Ben.Web.Services.EventClock.At(item.StartsAtUtc, Chicago);
        Assert.Equal(new TimeSpan(19, 0, 0), local.TimeOfDay);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(4), local.Date);
    }

    [Fact]
    public async Task Asked_for_and_held_bookings_count_and_say_so_and_open_the_event_page()
    {
        var w = await SeedAsync();
        AddHostedBooking(w, "Asked", 4, HostedEventBookingStatus.Requested);
        AddHostedBooking(w, "Held", 5, HostedEventBookingStatus.Held);
        AddHostedBooking(w, "Turned down", 6, HostedEventBookingStatus.TurnedDown);
        AddHostedBooking(w, "Released", 7, HostedEventBookingStatus.Cancelled);
        await w.Db.SaveChangesAsync();

        var items = (await GetAsync(w.Factory)).Items;
        Assert.Equal(["Asked", "Held"], items.Select(i => i.Title));
        Assert.Equal([MyNextEventStanding.AskedFor, MyNextEventStanding.HeldForYou], items.Select(i => i.Standing));
        Assert.All(items, i => Assert.StartsWith("/o/ghost-squad/events/", i.Href));
    }

    [Fact]
    public async Task A_called_off_event_is_not_counted_down_to()
    {
        var w = await SeedAsync();
        AddHostedBooking(w, "Cancelled", 4, HostedEventBookingStatus.Confirmed, HostedEventLifecycleState.Cancelled);
        AddHostedBooking(w, "Venue withdrew", 5, HostedEventBookingStatus.Confirmed, HostedEventLifecycleState.VenueWithdrawn);
        AddInvestigation(w, "Called off", DateTime.UtcNow.AddDays(2), RsvpStatus.Accepted, InvestigationStatus.Cancelled);
        await w.Db.SaveChangesAsync();

        Assert.Empty((await GetAsync(w.Factory)).Items);
    }

    /// <summary>
    /// A confirmed booking also puts its owner on the event's calendar row as Accepted (that row is a
    /// reflection of the bookings). Counted once, as the booking, so the card opens the pass.
    /// </summary>
    [Fact]
    public async Task A_hosted_events_calendar_row_is_not_counted_twice()
    {
        var w = await SeedAsync();
        var hosted = AddHostedBooking(w, "Seance Weekend", 4, HostedEventBookingStatus.Confirmed);
        AddCalendarEvent(w, "Seance Weekend", DateTime.UtcNow.AddDays(4), hostedEventId: hosted);
        await w.Db.SaveChangesAsync();

        var item = Assert.Single((await GetAsync(w.Factory)).Items);
        Assert.Equal(MyNextEventKind.HostedEvent, item.Kind);
    }

    [Fact]
    public async Task An_invitation_is_not_going_but_an_asked_for_tour_seat_is()
    {
        var w = await SeedAsync();
        AddCalendarEvent(w, "Only invited", DateTime.UtcNow.AddDays(2), myRsvp: RsvpStatus.Invited);
        AddCalendarEvent(w, "Declined", DateTime.UtcNow.AddDays(2), myRsvp: RsvpStatus.Declined);
        AddCalendarEvent(w, "Seat asked for", DateTime.UtcNow.AddDays(3), myRsvp: RsvpStatus.Invited, seat: TourSeatStatus.Requested);
        AddCalendarEvent(w, "Seat turned down", DateTime.UtcNow.AddDays(3), myRsvp: RsvpStatus.Invited, seat: TourSeatStatus.TurnedDown);
        await w.Db.SaveChangesAsync();

        var item = Assert.Single((await GetAsync(w.Factory)).Items);
        Assert.Equal("Seat asked for", item.Title);
        Assert.Equal(MyNextEventStanding.AskedFor, item.Standing);
    }

    [Fact]
    public async Task One_already_over_is_gone_and_one_under_way_stays()
    {
        var w = await SeedAsync();
        AddCalendarEvent(w, "Last night", DateTime.UtcNow.AddDays(-1));
        AddCalendarEvent(w, "Under way", DateTime.UtcNow.AddMinutes(-30));
        await w.Db.SaveChangesAsync();

        Assert.Equal("Under way", Assert.Single((await GetAsync(w.Factory)).Items).Title);
    }

    // ── Where a tap goes, and never widening ────────────────────────────────

    [Fact]
    public async Task A_public_event_opens_its_page_and_a_group_only_one_opens_the_calendar_for_a_member()
    {
        var w = await SeedAsync();
        w.Db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = Me, Role = OrganizationMemberRole.Member,
            IsActive = true, DateCreated = DateTime.UtcNow,
        });
        var publicId = AddCalendarEvent(w, "Public walk", DateTime.UtcNow.AddDays(2));
        AddCalendarEvent(w, "Team night", DateTime.UtcNow.AddDays(3), isPublic: false);
        await w.Db.SaveChangesAsync();

        var items = (await GetAsync(w.Factory)).Items;
        Assert.Equal($"/o/ghost-squad/events/slug-{publicId:N}", items.Single(i => i.Title == "Public walk").Href);
        Assert.Equal($"/organizations/{OrgId}/calendar", items.Single(i => i.Title == "Team night").Href);
    }

    /// <summary>
    /// Somebody on another group's private night who is not in that group has no page to open, so it
    /// is not shown: a card must go somewhere, and this one would answer 404.
    /// </summary>
    [Fact]
    public async Task A_group_only_event_of_a_group_you_are_not_in_is_not_shown()
    {
        var w = await SeedAsync();
        AddCalendarEvent(w, "Their team night", DateTime.UtcNow.AddDays(3), isPublic: false, org: OtherOrgId);
        await w.Db.SaveChangesAsync();

        Assert.Empty((await GetAsync(w.Factory)).Items);
    }

    [Fact]
    public async Task Another_persons_sign_ups_are_not_mine()
    {
        var w = await SeedAsync();
        AddCalendarEvent(w, "Ghost Walk", DateTime.UtcNow.AddDays(3));
        AddHostedBooking(w, "Seance Weekend", 4, HostedEventBookingStatus.Confirmed);
        await w.Db.SaveChangesAsync();

        var result = await Build(w.Factory, Host).Get(default);
        var answer = Assert.IsType<MyNextEventsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(answer.Items);
    }

    [Fact]
    public async Task An_investigation_not_yet_answered_is_labeled_invited_and_a_declined_one_is_left_out()
    {
        var w = await SeedAsync();
        AddInvestigation(w, "Not answered", DateTime.UtcNow.AddDays(2), RsvpStatus.Invited);
        AddInvestigation(w, "Said no", DateTime.UtcNow.AddDays(2), RsvpStatus.Declined);
        await w.Db.SaveChangesAsync();

        var item = Assert.Single((await GetAsync(w.Factory)).Items);
        Assert.Equal(("Not answered", MyNextEventStanding.Invited), (item.Title, item.Standing));
        Assert.Equal(item.StartsAtUtc + MyNextEventsController.InvestigationWithoutEnd, item.EndsAtUtc);
    }

    [Fact]
    public async Task The_total_counts_beyond_the_handful_returned()
    {
        var w = await SeedAsync();
        for (var i = 1; i <= MyNextEventsController.Cap + 3; i++)
            AddCalendarEvent(w, $"Walk {i:00}", DateTime.UtcNow.AddDays(i));
        await w.Db.SaveChangesAsync();

        var answer = await GetAsync(w.Factory);
        Assert.Equal(MyNextEventsController.Cap, answer.Items.Count);
        Assert.Equal(MyNextEventsController.Cap + 3, answer.Total);
    }
}
