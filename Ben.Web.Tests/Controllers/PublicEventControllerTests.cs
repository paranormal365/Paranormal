using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Public events (backlog item #87) — the listings that bring strangers to the platform.
/// </summary>
/// <remarks>
/// <para>Two rules carry the weight. <b>A public event is never at a private residence</b>, because
/// a listing with a date and an address is an invitation for strangers to turn up at somebody's
/// home. And <b>the exact address is withheld at the projection</b> when an event hides it — a
/// reader who is not coming receives a payload with no field for it, never the address with a flag
/// asking the client to be discreet.</para>
///
/// <para>Both were run against code with the guard removed before being relied on.</para>
/// </remarks>
public sealed class PublicEventControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid VisitorId = Guid.NewGuid();
    private static readonly Guid OtherVisitorId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid PublicEventId, Guid HiddenLocationEventId, Guid PrivateEventId, Guid ResidenceEventId);

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PublicEventController Build(IDbContextFactory<BenDataContext> f, Guid? userId)
        => new(f, new Ben.Data.WebApi.Services.CmsMarkupSanitizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(userId is null
                        ? new ClaimsIdentity()
                        : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer")),
                }
            }
        };

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 { (MemberId, "A Member"), (VisitorId, "A Visitor"), (OtherVisitorId, "Another Visitor") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
        });

        Guid AddPlace(string name, PlaceKind kind)
        {
            var placeId = Guid.NewGuid();
            db.Places.Add(new Place
            {
                Id = placeId, Name = name, Kind = kind,
                StreetAddress1 = "12 Elm Street", City = "Nashville", State = "TN", ZipCode = "37201",
                Latitude = 36.1627m, Longitude = -86.7816m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            return placeId;
        }

        var landmarkId  = AddPlace("The Old Mill", PlaceKind.PublicLocation);
        var residenceId = AddPlace("A client's house", PlaceKind.PrivateResidence);

        Guid AddEvent(string title, string slug, bool isPublic, Guid? placeId,
                      bool hideLocation = false, int? capacity = null, Guid? caseId = null)
        {
            var eventId = Guid.NewGuid();
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = eventId, OrganizationId = OrgId, Title = title,
                UrlName = isPublic ? slug : null,
                Description = "Come along.",
                StartDateTime = DateTime.UtcNow.AddDays(7),
                EndDateTime = DateTime.UtcNow.AddDays(7).AddHours(3),
                IsPublic = isPublic, PlaceId = placeId, CaseId = caseId,
                HideExactLocation = hideLocation, AttendeeCapacity = capacity,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            return eventId;
        }

        var publicId    = AddEvent("Ghost Walk", "2026-08-24-ghost-walk", true, landmarkId);
        var hiddenId    = AddEvent("Night Vigil", "2026-08-24-night-vigil", true, landmarkId, hideLocation: true);
        var privateId   = AddEvent("Team Meeting", "team-meeting", false, landmarkId);
        var residenceId2 = AddEvent("At the house", "at-the-house", true, residenceId);

        await db.SaveChangesAsync();
        return new World(factory, publicId, hiddenId, privateId, residenceId2);
    }

    private static async Task<PublicEventRecord> GetAsync(World w, Guid eventId, Guid? viewer)
    {
        var result = await Build(w.Factory, viewer).GetEvent(eventId, default);
        return Assert.IsType<PublicEventRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ── A public event is never at somebody's home ───────────────────────────

    /// <summary>
    /// Even flagged public in storage, a residence event never reaches a visitor. The read filter
    /// restates the rule the write path enforces, so a row that became public some other way — a
    /// script, a migration, a bug — is still not served.
    /// </summary>
    [Fact]
    public async Task An_event_at_a_residence_is_never_public_however_it_got_flagged()
    {
        var w = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Build(w.Factory, null).GetEvent(w.ResidenceEventId, default)).Result);

        var list = await Build(w.Factory, null).GetUpcoming(null, 50, default);
        var items = Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(list.Result).Value);

        Assert.DoesNotContain(items, i => i.Id == w.ResidenceEventId);
    }

    [Fact]
    public async Task An_event_attached_to_a_case_is_never_public()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var ev = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.PublicEventId);
            ev.CaseId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(w.Factory, null).GetEvent(w.PublicEventId, default)).Result);
    }

    [Fact]
    public async Task A_private_event_is_not_served_to_anybody()
    {
        var w = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Build(w.Factory, null).GetEvent(w.PrivateEventId, default)).Result);
        // Not even to a member of the organization that owns it — this endpoint is the public one,
        // and members have their own calendar.
        Assert.IsType<NotFoundResult>((await Build(w.Factory, MemberId).GetEvent(w.PrivateEventId, default)).Result);
    }

    // ── The address ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_hidden_address_is_absent_from_the_payload_until_somebody_is_coming()
    {
        var w = await SeedAsync();

        var asStranger = await GetAsync(w, w.HiddenLocationEventId, VisitorId);
        Assert.Null(asStranger.Location.ExactAddress);
        Assert.True(asStranger.Location.IsExactAddressHidden);
        // The area is still shown, so somebody can decide whether it is worth going.
        Assert.Equal("Nashville", asStranger.Location.City);

        await Build(w.Factory, VisitorId).Rsvp(w.HiddenLocationEventId, default);

        var asAttendee = await GetAsync(w, w.HiddenLocationEventId, VisitorId);
        Assert.Contains("12 Elm Street", asAttendee.Location.ExactAddress);
        Assert.False(asAttendee.Location.IsExactAddressHidden);
    }

    [Fact]
    public async Task Cancelling_stops_the_address_being_served_again()
    {
        var w = await SeedAsync();
        await Build(w.Factory, VisitorId).Rsvp(w.HiddenLocationEventId, default);
        Assert.NotNull((await GetAsync(w, w.HiddenLocationEventId, VisitorId)).Location.ExactAddress);

        await Build(w.Factory, VisitorId).CancelRsvp(w.HiddenLocationEventId, default);

        Assert.Null((await GetAsync(w, w.HiddenLocationEventId, VisitorId)).Location.ExactAddress);
    }

    [Fact]
    public async Task An_event_that_does_not_hide_its_location_shows_it_to_everybody()
    {
        var w = await SeedAsync();
        var anonymous = await GetAsync(w, w.PublicEventId, viewer: null);

        Assert.Contains("12 Elm Street", anonymous.Location.ExactAddress);
        Assert.False(anonymous.Location.IsExactAddressHidden);
    }

    /// <summary>
    /// The map pin is the redacted grid point for everybody, attendee or not. One map, one pin —
    /// a coordinate that sharpened for some readers would be a way of working out who is attending.
    /// </summary>
    [Fact]
    public async Task The_map_coordinate_is_approximate_even_for_an_attendee()
    {
        var w = await SeedAsync();
        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);

        var detail = await GetAsync(w, w.PublicEventId, VisitorId);

        Assert.NotNull(detail.Location.ApproximateLatitude);
        Assert.NotEqual(36.1627m, detail.Location.ApproximateLatitude);
    }

    // ── Coming along ─────────────────────────────────────────────────────────

    [Fact]
    public async Task An_anonymous_visitor_is_told_to_sign_in_rather_than_shown_a_dead_button()
    {
        var w = await SeedAsync();
        var detail = await GetAsync(w, w.PublicEventId, viewer: null);

        Assert.False(detail.Flags.CanRsvp);
        Assert.Equal("Sign in to say you're coming.", detail.Flags.RsvpBlockedReason);
    }

    [Fact]
    public async Task Saying_you_are_coming_twice_is_one_person()
    {
        var w = await SeedAsync();

        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);
        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);

        var detail = await GetAsync(w, w.PublicEventId, VisitorId);
        Assert.Equal(1, detail.AttendingCount);
        Assert.True(detail.Flags.HasRsvpd);
    }

    [Fact]
    public async Task A_full_event_takes_no_more_people()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var ev = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.PublicEventId);
            ev.AttendeeCapacity = 1;
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default)).Result);

        var second = await Build(w.Factory, OtherVisitorId).Rsvp(w.PublicEventId, default);
        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    /// <summary>
    /// Somebody who cancelled and changed their mind is not refused by a seat they are not sitting
    /// in — the capacity check excludes their own row.
    /// </summary>
    [Fact]
    public async Task Changing_your_mind_back_does_not_count_you_twice()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var ev = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.PublicEventId);
            ev.AttendeeCapacity = 1;
            await db.SaveChangesAsync();
        }

        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);
        await Build(w.Factory, VisitorId).CancelRsvp(w.PublicEventId, default);

        Assert.IsType<OkObjectResult>((await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default)).Result);
    }

    [Fact]
    public async Task Sign_ups_close_when_they_are_told_to()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var ev = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.PublicEventId);
            ev.RsvpClosesAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        Assert.IsType<ConflictObjectResult>((await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default)).Result);
    }

    // ── What I said I would go to ────────────────────────────────────────────

    /// <summary>
    /// Saying you are coming has to leave a trace somewhere the person can find. Before this, an
    /// RSVP created an <c>OrgCalendarEventAttendee</c> while <c>/my-investigations</c> read
    /// <c>InvestigationAttendee</c>, so it vanished entirely.
    /// </summary>
    [Fact]
    public async Task An_event_you_are_going_to_appears_on_your_own_list()
    {
        var w = await SeedAsync();

        var before = await Build(w.Factory, VisitorId).GetMine(default);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(before.Result).Value));

        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);

        var after = await Build(w.Factory, VisitorId).GetMine(default);
        var mine = Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(after.Result).Value);

        var only = Assert.Single(mine);
        Assert.Equal(w.PublicEventId, only.Id);
        Assert.False(string.IsNullOrWhiteSpace(only.UrlName), "the card has nowhere to link");
    }

    [Fact]
    public async Task Somebody_elses_rsvp_is_not_on_your_list()
    {
        var w = await SeedAsync();
        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);

        var theirs = await Build(w.Factory, OtherVisitorId).GetMine(default);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(theirs.Result).Value));
    }

    [Fact]
    public async Task Cancelling_takes_it_off_your_list()
    {
        var w = await SeedAsync();
        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);
        await Build(w.Factory, VisitorId).CancelRsvp(w.PublicEventId, default);

        var mine = await Build(w.Factory, VisitorId).GetMine(default);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(mine.Result).Value));
    }

    /// <summary>
    /// Something that finished yesterday stays on the list for a while. Somebody asking "what was
    /// that place called?" the morning after has nowhere else to look.
    /// </summary>
    [Fact]
    public async Task A_recently_finished_event_is_still_listed()
    {
        var w = await SeedAsync();
        await Build(w.Factory, VisitorId).Rsvp(w.PublicEventId, default);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var ev = await db.OrgCalendarEvents.SingleAsync(e => e.Id == w.PublicEventId);
            ev.StartDateTime = DateTime.UtcNow.AddDays(-2);
            ev.EndDateTime   = DateTime.UtcNow.AddDays(-2).AddHours(3);
            await db.SaveChangesAsync();
        }

        var mine = await Build(w.Factory, VisitorId).GetMine(default);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(mine.Result).Value));
    }

    // ── The readable URL ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_event_is_reachable_by_its_slug()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, null)
            .GetEventBySlug("ghost-squad", "2026-08-24-ghost-walk", default);
        var detail = Assert.IsType<PublicEventRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(w.PublicEventId, detail.Id);
    }

    /// <summary>
    /// Every event in the list carries the slug its card links to. A list whose items cannot be
    /// opened is the shape this codebase keeps shipping — a feature that renders and goes nowhere.
    /// </summary>
    [Fact]
    public async Task Every_listed_event_can_actually_be_opened()
    {
        var w = await SeedAsync();

        var list = await Build(w.Factory, null).GetUpcoming(null, 50, default);
        var items = Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(list.Result).Value);

        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.UrlName),
                $"'{item.Title}' is listed with no slug, so its card links nowhere.");

            var opened = await Build(w.Factory, null)
                .GetEventBySlug(item.OrganizationUrlName, item.UrlName!, default);
            Assert.IsType<OkObjectResult>(opened.Result);
        }
    }

    [Fact]
    public async Task A_slug_belonging_to_another_organization_does_not_resolve()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, null)
            .GetEventBySlug("some-other-group", "2026-08-24-ghost-walk", default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── "Upcoming" has to mean upcoming ───────────────────────────────────────

    [Fact]
    public async Task The_listing_leaves_out_events_that_have_already_ended()
    {
        // GetUpcoming had no date filter at all, and sorts ascending by start — so the FIRST
        // thing a stranger saw on a group's public events page was the oldest event it had ever
        // run. Found by opening the Events screen on a phone and reading the top row.
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, Title = "Last Month's Open Night",
                UrlName = "last-months-open-night", Description = "Over and done.",
                StartDateTime = DateTime.UtcNow.AddDays(-30),
                EndDateTime = DateTime.UtcNow.AddDays(-30).AddHours(3),
                IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            await db.SaveChangesAsync();
        }

        var listed = await ListAsync(w);
        Assert.DoesNotContain(listed, e => e.Title == "Last Month's Open Night");
        Assert.Contains(listed, e => e.Title == "Ghost Walk");
    }

    [Fact]
    public async Task An_event_happening_right_now_is_still_listed()
    {
        // End, not start. Dropping something the moment it begins would hide exactly the event
        // somebody is looking up while standing outside it.
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, Title = "Happening Now",
                UrlName = "happening-now", Description = "In progress.",
                StartDateTime = DateTime.UtcNow.AddHours(-1),
                EndDateTime = DateTime.UtcNow.AddHours(2),
                IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            await db.SaveChangesAsync();
        }

        Assert.Contains(await ListAsync(w), e => e.Title == "Happening Now");
    }

    [Fact]
    public async Task A_past_events_own_page_still_resolves()
    {
        // The listing filter must NOT reach the detail lookup: a link shared to an event that
        // has since happened has to keep working, or every past share becomes a dead end.
        var w = await SeedAsync();
        var pastId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = pastId, OrganizationId = OrgId, Title = "Already Happened",
                UrlName = "already-happened", Description = "Over.",
                StartDateTime = DateTime.UtcNow.AddDays(-10),
                EndDateTime = DateTime.UtcNow.AddDays(-10).AddHours(2),
                IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(w.Factory, null).GetEvent(pastId, default);
        var record = Assert.IsType<PublicEventRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Already Happened", record.Title);
    }

    private static async Task<IReadOnlyList<PublicEventListItem>> ListAsync(World w)
    {
        var result = await Build(w.Factory, null).GetUpcoming(null, 50, default);
        return Assert.IsType<List<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
    }
}
