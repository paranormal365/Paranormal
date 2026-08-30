using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Naming the rooms of a property you run (item 197).
/// </summary>
/// <remarks>
/// <para>A hotel, an inn or a dormitory is described by its rooms rather than its address, and
/// "Room 217's history" is the thing a guest reads. The phone already records which room an
/// operator says they are in and stamps it on every reading; these are where those labels get
/// something to point at.</para>
///
/// <para><b>The claim worth testing is ownership.</b> A <c>Place</c> is shared — two groups can
/// investigate the same building, and one of them did not create it — so rooms belong to the
/// ORGANIZATION that named them. A group must not be able to read or edit another's rooms in the
/// same building, and the route is what enforces it.</para>
/// </remarks>
public sealed class PlaceRoomTests
{
    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid PlaceId,
        Guid HotelId, Guid HotelOwner, Guid VisitorOrgId, Guid VisitorOwner);

    /// <summary>One building; a hotel that runs it and a group that merely visits it.</summary>
    private static async Task<World> BuildAsync()
    {
        var f = Factory();
        var placeId = Guid.NewGuid();
        var hotelId = Guid.NewGuid();
        var hotelOwner = Guid.NewGuid();
        var visitorOrgId = Guid.NewGuid();
        var visitorOwner = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();

        void Org(Guid id, string name, Guid owner)
        {
            db.Organizations.Add(new Organization
            {
                Id = id, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = id, AppUserId = owner,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
        }

        Org(hotelId, "The Hermitage Hotel", hotelOwner);
        Org(visitorOrgId, "Harpeth Paranormal", visitorOwner);

        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Hermitage Hotel", Kind = PlaceKind.PublicLocation,
            City = "Nashville", State = "TN",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = hotelOwner,
        });

        await db.SaveChangesAsync();
        return new World(f, placeId, hotelId, hotelOwner, visitorOrgId, visitorOwner);
    }

    private static PlaceRoomController Controller(World w, Guid actingUserId)
        => new(w.Factory, new OrganizationSecurityService(w.Factory))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, actingUserId.ToString())], "Bearer"))
                }
            }
        };

    private static SavePlaceRoomRequest Room(string name, bool isPublic = false)
        => new(name, "Second floor", "Guests report a knock at 3am.", isPublic);

    [Fact]
    public async Task An_owner_can_name_a_room()
    {
        var w = await BuildAsync();

        var created = await Controller(w, w.HotelOwner)
            .Create(w.HotelId, w.PlaceId, Room("Room 217"), default);

        var record = Assert.IsType<PlaceRoomRecord>(Assert.IsType<OkObjectResult>(created.Result).Value);
        Assert.Equal("Room 217", record.Name);
        Assert.Equal("Second floor", record.Floor);
        Assert.True(record.IsActive);
    }

    /// <summary>
    /// A room is not public because somebody named it.
    /// </summary>
    /// <remarks>
    /// The whole point of a haunted property is that its reports are the marketing, which makes it
    /// tempting to publish everything by default. A room is still somewhere people sleep, so
    /// publishing one stays a decision.
    /// </remarks>
    [Fact]
    public async Task A_new_room_is_private_until_somebody_says_otherwise()
    {
        var w = await BuildAsync();

        var created = await Controller(w, w.HotelOwner)
            .Create(w.HotelId, w.PlaceId, Room("The cellar"), default);

        var record = (PlaceRoomRecord)((OkObjectResult)created.Result!).Value!;
        Assert.False(record.IsPublic);
    }

    /// <summary>
    /// Two groups can describe the same building without seeing each other's rooms.
    /// </summary>
    /// <remarks>
    /// This is the reason rooms hang off the organization rather than the place. A hotel names its
    /// own rooms; a group that visits names whatever it likes; neither list leaks into the other.
    /// </remarks>
    [Fact]
    public async Task One_groups_rooms_are_invisible_to_another_in_the_same_building()
    {
        var w = await BuildAsync();

        await Controller(w, w.HotelOwner).Create(w.HotelId, w.PlaceId, Room("Room 217"), default);
        await Controller(w, w.VisitorOwner).Create(w.VisitorOrgId, w.PlaceId, Room("Upstairs corridor"), default);

        var hotelRooms = await Controller(w, w.HotelOwner).GetAll(w.HotelId, w.PlaceId, default);
        var visitorRooms = await Controller(w, w.VisitorOwner).GetAll(w.VisitorOrgId, w.PlaceId, default);

        var hotel = (IEnumerable<PlaceRoomRecord>)((OkObjectResult)hotelRooms.Result!).Value!;
        var visitor = (IEnumerable<PlaceRoomRecord>)((OkObjectResult)visitorRooms.Result!).Value!;

        Assert.Equal(["Room 217"], hotel.Select(r => r.Name));
        Assert.Equal(["Upstairs corridor"], visitor.Select(r => r.Name));
    }

    /// <summary>
    /// Guessing another group's room id does not open it.
    /// </summary>
    /// <remarks>
    /// The id is the one thing a caller supplies that the permission check does not cover, so the
    /// lookup matches on organization and place as well. Without that, a member of any group could
    /// edit a room belonging to any other simply by knowing its id.
    /// </remarks>
    [Fact]
    public async Task A_room_cannot_be_edited_through_another_groups_route()
    {
        var w = await BuildAsync();

        var created = await Controller(w, w.HotelOwner)
            .Create(w.HotelId, w.PlaceId, Room("Room 217"), default);
        var roomId = ((PlaceRoomRecord)((OkObjectResult)created.Result!).Value!).Id;

        var stolen = await Controller(w, w.VisitorOwner)
            .Update(w.VisitorOrgId, w.PlaceId, roomId, Room("Renamed by a stranger"), default);

        Assert.IsType<NotFoundResult>(stolen.Result);

        // And it really is untouched.
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal("Room 217", (await db.PlaceRooms.FindAsync(roomId))!.Name);
    }

    /// <summary>Somebody with no standing in the group cannot name rooms in it.</summary>
    [Fact]
    public async Task A_stranger_cannot_name_rooms()
    {
        var w = await BuildAsync();

        var result = await Controller(w, Guid.NewGuid())
            .Create(w.HotelId, w.PlaceId, Room("Room 217"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>
    /// The same room name twice in one building is refused with a sentence, not an index error.
    /// </summary>
    /// <remarks>
    /// It matters beyond tidiness: the Field Kit sends a room by NAME, so two rooms called
    /// "Room 217" would make an attributed reading ambiguous rather than merely duplicated.
    /// </remarks>
    [Fact]
    public async Task A_duplicate_room_name_is_refused()
    {
        var w = await BuildAsync();

        await Controller(w, w.HotelOwner).Create(w.HotelId, w.PlaceId, Room("Room 217"), default);
        var again = await Controller(w, w.HotelOwner).Create(w.HotelId, w.PlaceId, Room("Room 217"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Contains("Room 217", conflict.Value!.ToString());
    }

    /// <summary>But the SAME name is fine for a different group in the same building.</summary>
    [Fact]
    public async Task Two_groups_may_each_have_a_room_of_the_same_name()
    {
        var w = await BuildAsync();

        await Controller(w, w.HotelOwner).Create(w.HotelId, w.PlaceId, Room("Room 217"), default);
        var other = await Controller(w, w.VisitorOwner)
            .Create(w.VisitorOrgId, w.PlaceId, Room("Room 217"), default);

        Assert.IsType<OkObjectResult>(other.Result);
    }

    [Fact]
    public async Task A_room_needs_a_name()
    {
        var w = await BuildAsync();

        var result = await Controller(w, w.HotelOwner)
            .Create(w.HotelId, w.PlaceId, new SavePlaceRoomRequest("   ", null, null, false), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>Rooms come back in the order the property arranged them, not alphabetically.</summary>
    /// <remarks>Buildings are not alphabetical: 217 comes after the lobby and before the roof.</remarks>
    [Fact]
    public async Task Rooms_keep_the_order_they_were_given()
    {
        var w = await BuildAsync();
        var c = Controller(w, w.HotelOwner);

        foreach (var name in new[] { "Lobby", "Room 217", "Roof" })
            await c.Create(w.HotelId, w.PlaceId, Room(name), default);

        var listed = await Controller(w, w.HotelOwner).GetAll(w.HotelId, w.PlaceId, default);
        var rooms = (IEnumerable<PlaceRoomRecord>)((OkObjectResult)listed.Result!).Value!;

        Assert.Equal(["Lobby", "Room 217", "Roof"], rooms.Select(r => r.Name));
    }
}
