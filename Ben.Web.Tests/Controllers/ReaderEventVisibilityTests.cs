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
/// "Or events available for the person logged in" (Ben, 10/16/2026): the event list and the
/// nearby search add a signed-in reader's own groups' events, and nothing else.
/// </summary>
/// <remarks>
/// <para>The rules held here, each run against code with it removed before being relied on:</para>
/// <list type="bullet">
/// <item>A non-member never gets a group-only event, signed in or not.</item>
/// <item>A private-residence or case-bound event never appears on a list or a map, even for the
/// group's own members.</item>
/// <item>A signed-out request is exactly what it was, and so is a signed-in one that did not ask
/// (the phone app sends its token to these endpoints and has no idea what a "Your group" row is).</item>
/// </list>
/// </remarks>
public sealed class ReaderEventVisibilityTests
{
    private const double Lat = 36.1627, Lon = -86.7816;

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();
    private static readonly Guid FormerMemberId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory);

    private static ControllerContext As(Guid? user) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(user is null
                ? new ClaimsIdentity()
                : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Value.ToString())], "Bearer")),
        },
    };

    private static async Task<World> SeedAsync()
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = await factory.CreateDbContextAsync();

        foreach (var id in new[] { MemberId, OutsiderId, FormerMemberId })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = "Someone" });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId, Role = OrganizationMemberRole.Member,
            IsActive = true, DateCreated = DateTime.UtcNow,
        });
        // Left the group: no longer a member for anything.
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = FormerMemberId, Role = OrganizationMemberRole.Member,
            IsActive = false, DateCreated = DateTime.UtcNow,
        });

        Guid Place(string name, PlaceKind kind)
        {
            var id = Guid.NewGuid();
            db.Places.Add(new Place
            {
                Id = id, Name = name, Kind = kind, City = "Nashville", State = "TN",
                Latitude = 36.1650m, Longitude = -86.7840m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            return id;
        }

        var landmark = Place("The Old Mill", PlaceKind.PublicLocation);
        var house = Place("A client's house", PlaceKind.PrivateResidence);

        Guid Event(string title, bool isPublic, Guid placeId, Guid? caseId = null)
        {
            var id = Guid.NewGuid();
            var start = DateTime.UtcNow.AddDays(5);
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = id, OrganizationId = OrgId, Title = title, UrlName = isPublic ? $"slug-{id:N}" : null,
                StartDateTime = start, EndDateTime = start.AddHours(2), IsPublic = isPublic,
                PlaceId = placeId, CaseId = caseId, TimeZoneId = "America/Chicago",
                DateCreated = DateTime.UtcNow.AddDays(-5), CreatedByAppUserId = MemberId,
            });
            return id;
        }

        Event("Public walk", true, landmark);
        Event("Team night", false, landmark);
        Event("At the client's house", false, house);
        Event("Public at a house", true, house);
        Event("Case visit", false, landmark, caseId: Guid.NewGuid());
        var invitedTo = Event("Open vigil", true, landmark);
        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = invitedTo, AppUserId = OutsiderId, RsvpStatus = RsvpStatus.Invited,
            Seats = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
        });

        await db.SaveChangesAsync();
        return new World(factory);
    }

    private static PublicEventController Events(World w, Guid? user)
        => new(w.Factory, new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), Support.SilentTourMail.Instance)
        { ControllerContext = As(user) };

    private static async Task<IReadOnlyList<PublicEventListItem>> ListAsync(World w, Guid? user, bool includeMine)
    {
        var result = await Events(w, user).GetUpcoming(null, 50, default, includeMine);
        return Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private static async Task<IReadOnlyList<NearbyEventResult>> NearbyAsync(World w, Guid? user, bool includeMine)
    {
        var controller = new SearchController(w.Factory) { ControllerContext = As(user) };
        var result = await controller.Nearby(Lat, Lon, 25, null, default, includeMine);
        return Assert.IsType<NearbyResults>(Assert.IsType<OkObjectResult>(result.Result).Value).Events;
    }

    private static readonly string[] PublicOnly = ["Open vigil", "Public walk"];

    // ── Signed out: exactly what it was ─────────────────────────────────────

    [Fact]
    public async Task Signed_out_the_list_is_the_public_list_whatever_is_asked()
    {
        var w = await SeedAsync();
        Assert.Equal(PublicOnly, (await ListAsync(w, null, false)).Select(e => e.Title).Order());
        Assert.Equal(PublicOnly, (await ListAsync(w, null, true)).Select(e => e.Title).Order());
        Assert.All(await ListAsync(w, null, true), e => Assert.Equal(EventReaderTie.None, e.ReaderTie));
    }

    [Fact]
    public async Task Signed_out_the_nearby_search_is_the_public_search_whatever_is_asked()
    {
        var w = await SeedAsync();
        Assert.Equal(PublicOnly, (await NearbyAsync(w, null, false)).Select(e => e.Title).Order());
        Assert.Equal(PublicOnly, (await NearbyAsync(w, null, true)).Select(e => e.Title).Order());
    }

    /// <summary>The phone app sends its token and never asks; it must get the list it was built for.</summary>
    [Fact]
    public async Task A_member_who_does_not_ask_gets_the_public_list()
    {
        var w = await SeedAsync();
        Assert.Equal(PublicOnly, (await ListAsync(w, MemberId, false)).Select(e => e.Title).Order());
        Assert.Equal(PublicOnly, (await NearbyAsync(w, MemberId, false)).Select(e => e.Title).Order());
        Assert.All(await ListAsync(w, MemberId, false), e => Assert.Equal(EventReaderTie.None, e.ReaderTie));
    }

    // ── A member who asks ───────────────────────────────────────────────────

    [Fact]
    public async Task A_member_who_asks_also_gets_their_groups_events_marked()
    {
        var w = await SeedAsync();

        var list = await ListAsync(w, MemberId, true);
        Assert.Equal(["Open vigil", "Public walk", "Team night"], list.Select(e => e.Title).Order());
        Assert.Equal(EventReaderTie.YourGroup, list.Single(e => e.Title == "Team night").ReaderTie);
        Assert.Equal(EventReaderTie.None, list.Single(e => e.Title == "Public walk").ReaderTie);

        var near = await NearbyAsync(w, MemberId, true);
        Assert.Equal(["Open vigil", "Public walk", "Team night"], near.Select(e => e.Title).Order());
        Assert.Equal(EventReaderTie.YourGroup, near.Single(e => e.Title == "Team night").ReaderTie);
    }

    // ── Never wider ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_non_member_never_gets_a_group_only_event()
    {
        var w = await SeedAsync();
        foreach (var someone in new[] { OutsiderId, FormerMemberId })
        {
            Assert.DoesNotContain(await ListAsync(w, someone, true), e => e.Title == "Team night");
            Assert.DoesNotContain(await NearbyAsync(w, someone, true), e => e.Title == "Team night");
        }
    }

    /// <summary>
    /// These lists carry a pin. A pin at somebody's house, or on a case, is the thing the public rule
    /// exists to prevent — and being in the group does not make the map a safe place for it.
    /// </summary>
    [Fact]
    public async Task A_private_residence_or_case_event_never_appears_even_for_a_member()
    {
        var w = await SeedAsync();
        string[] never = ["At the client's house", "Public at a house", "Case visit"];

        foreach (var reader in new Guid?[] { null, MemberId, OutsiderId })
        {
            var list = (await ListAsync(w, reader, true)).Select(e => e.Title);
            var near = (await NearbyAsync(w, reader, true)).Select(e => e.Title);
            Assert.Empty(list.Intersect(never));
            Assert.Empty(near.Intersect(never));
        }
    }

    [Fact]
    public async Task An_invitation_to_a_public_event_is_marked_invited()
    {
        var w = await SeedAsync();
        Assert.Equal(EventReaderTie.Invited, (await ListAsync(w, OutsiderId, true)).Single(e => e.Title == "Open vigil").ReaderTie);
        Assert.Equal(EventReaderTie.Invited, (await NearbyAsync(w, OutsiderId, true)).Single(e => e.Title == "Open vigil").ReaderTie);
    }

    [Fact]
    public async Task Every_row_carries_when_it_was_listed_for_the_countdown_bar()
    {
        var w = await SeedAsync();
        Assert.All(await ListAsync(w, null, false), e => Assert.NotNull(e.ListedUtc));
        Assert.All(await NearbyAsync(w, null, false), e =>
        {
            Assert.NotNull(e.ListedUtc);
            Assert.NotNull(e.EndDateTime);
        });
    }
}
