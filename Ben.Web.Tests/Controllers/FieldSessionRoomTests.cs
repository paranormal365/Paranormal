using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Putting a field session in a room of its place (Ben, 11/21/2026): "I recorded on the Paranormal Platform and
/// want to attach it to my room at the site." Which rooms are offered, picking and typing, the place's page, and
/// what happens to a session's room when the room is renamed or removed, or the session moves place. On SQLite,
/// because a renamed room renames its sessions in one statement.
/// </summary>
public sealed class FieldSessionRoomTests
{
    private sealed record World(
        SqliteTestDb Sqlite, Guid Me, Guid Stranger, Guid PlaceId, Guid OtherPlaceId, Guid MyGroup, Guid SessionId,
        Guid PublicRoom, Guid MyPrivateRoom, Guid TheirPrivateRoom, Guid RetiredRoom) : IAsyncDisposable
    {
        public IDbContextFactory<BenDataContext> F => Sqlite.Factory;
        public ValueTask DisposeAsync() => Sqlite.DisposeAsync();
    }

    private static T As<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return controller;
    }

    private static FieldSessionRoomController Rooms(World w, Guid userId)
        => As(new FieldSessionRoomController(w.F, new OrganizationSecurityService(w.F)), userId);

    private static async Task<World> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        Guid Person(string name)
        {
            var id = Guid.NewGuid();
            db.Users.Add(new AppUser
            {
                Id = id, UserName = $"{id:N}@t.com", NormalizedUserName = $"{id:N}@T.COM", Email = $"{id:N}@t.com",
                DisplayName = name, EmailConfirmed = true, DateCreated = now,
            });
            return id;
        }
        var me = Person("Ben Clark");
        var stranger = Person("A Stranger");

        Guid Group(string name, Guid owner)
        {
            var id = Guid.NewGuid();
            db.Organizations.Add(new Organization
            {
                Id = id, Name = name, UrlName = $"g{id:N}", Kind = OrganizationKind.InvestigationGroup,
                DateCreated = now, CreatedByAppUserId = owner,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = id, AppUserId = owner, Role = OrganizationMemberRole.Owner,
                IsActive = true, DateCreated = now, CreatedByAppUserId = owner,
            });
            return id;
        }
        var mine = Group("Vanderbilt Paranormal", me);
        var theirs = Group("Another Group", stranger);

        var placeId = Guid.NewGuid();
        var otherPlaceId = Guid.NewGuid();
        db.Places.Add(new Place { Id = placeId, Name = "Vanderbilt", City = "Nashville", State = "TN", Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = me });
        db.Places.Add(new Place { Id = otherPlaceId, Name = "Belmont Mansion", City = "Nashville", State = "TN", Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = me });

        Guid Room(Guid org, Guid place, string name, bool isPublic, bool active = true)
        {
            var id = Guid.NewGuid();
            db.PlaceRooms.Add(new PlaceRoom
            {
                Id = id, OrganizationId = org, PlaceId = place, Name = name, IsPublic = isPublic, IsActive = active,
                DateCreated = now, CreatedByAppUserId = me,
            });
            return id;
        }
        var publicRoom = Room(theirs, placeId, "Kirkland Hall", isPublic: true);
        var myPrivate = Room(mine, placeId, "Room 217", isPublic: false);
        var theirPrivate = Room(theirs, placeId, "The Basement", isPublic: false);
        var retired = Room(theirs, placeId, "Old Gym", isPublic: true, active: false);
        Room(theirs, otherPlaceId, "Ballroom", isPublic: true);

        var fileTypeId = Guid.NewGuid();
        db.UploadFileTypes.Add(new UploadFileType { Id = fileTypeId, Name = "Field session", DateCreated = now, CreatedByAppUserId = me });
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = fileTypeId, FileName = "data.json", StoredFileName = "x.json", ContentType = "application/json",
            AppUserId = me, DateCreated = now, CreatedByAppUserId = me,
        });
        var sessionId = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = me, DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = fileId,
            DeviceModel = "iPhone17,2", LocationLabel = "Vanderbilt", StartedAt = now.AddHours(-2), EndedAt = now.AddHours(-1),
            ReadingCount = 500, MarkerCount = 2, PlaceId = placeId, PublishedAtUtc = now,
            MediaReviewState = FeedMediaReviewState.Approved, DateCreated = now, CreatedByAppUserId = me,
        });
        await db.SaveChangesAsync();
        return new World(sqlite, me, stranger, placeId, otherPlaceId, mine, sessionId, publicRoom, myPrivate, theirPrivate, retired);
    }

    private static SessionRoomChoices Ok(ActionResult<SessionRoomChoices> result)
        => (SessionRoomChoices)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private static async Task<FieldSessionUpload> SessionAsync(World w)
    {
        await using var db = await w.Sqlite.NewContextAsync();
        return await db.FieldSessionUploads.AsNoTracking().SingleAsync(s => s.Id == w.SessionId);
    }

    [Fact]
    public async Task The_place_s_public_rooms_and_my_own_group_s_rooms_are_offered_and_nothing_else()
    {
        await using var w = await SeedAsync();

        var choices = Ok(await Rooms(w, w.Me).Get(w.SessionId, default));

        Assert.Equal("Vanderbilt", choices.PlaceName);
        Assert.Equal(
            new[] { w.PublicRoom, w.MyPrivateRoom }.OrderBy(x => x),
            choices.Rooms.Select(r => r.Id).OrderBy(x => x));
        Assert.Equal("Vanderbilt Paranormal", choices.Rooms.Single(r => r.Id == w.MyPrivateRoom).GroupName);
    }

    [Fact]
    public async Task Picking_a_room_puts_the_session_in_it_and_the_place_s_page_says_so()
    {
        await using var w = await SeedAsync();

        var saved = Ok(await Rooms(w, w.Me).Set(w.SessionId, new SetSessionRoomRequest(w.MyPrivateRoom, null), default));

        Assert.Equal(w.MyPrivateRoom, saved.PlaceRoomId);
        Assert.Equal("Room 217", saved.RoomName);
        var page = (PublicPlaceResponse)Assert.IsType<OkObjectResult>(
            (await new PublicPlaceController(w.F).GetById(w.PlaceId, default)).Result).Value!;
        var row = Assert.Single(page.Sessions!);
        Assert.Equal("Room 217", row.Room);
        Assert.Equal(w.MyPrivateRoom, row.PlaceRoomId);
    }

    [Fact]
    public async Task A_room_that_is_not_offered_is_refused()
    {
        await using var w = await SeedAsync();

        foreach (var room in new[] { w.TheirPrivateRoom, w.RetiredRoom })
        {
            var result = await Rooms(w, w.Me).Set(w.SessionId, new SetSessionRoomRequest(room, null), default);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
        Assert.Null((await SessionAsync(w)).PlaceRoomId);
    }

    [Fact]
    public async Task A_typed_name_is_the_room_of_that_name_when_there_is_one_and_kept_as_typed_when_not()
    {
        await using var w = await SeedAsync();
        var rooms = Rooms(w, w.Me);

        var matched = Ok(await rooms.Set(w.SessionId, new SetSessionRoomRequest(null, "  kirkland hall "), default));
        Assert.Equal(w.PublicRoom, matched.PlaceRoomId);
        Assert.Equal("Kirkland Hall", matched.RoomName);

        var typed = Ok(await rooms.Set(w.SessionId, new SetSessionRoomRequest(null, "Third-floor lounge"), default));
        Assert.Null(typed.PlaceRoomId);
        Assert.Equal("Third-floor lounge", typed.RoomName);

        var cleared = Ok(await rooms.Set(w.SessionId, new SetSessionRoomRequest(null, null), default));
        Assert.Null(cleared.PlaceRoomId);
        Assert.Null(cleared.RoomName);
    }

    [Fact]
    public async Task A_name_longer_than_a_room_s_is_refused()
    {
        await using var w = await SeedAsync();

        var result = await Rooms(w, w.Me).Set(
            w.SessionId, new SetSessionRoomRequest(null, new string('x', FieldSessionRoomController.MaxRoomNameLength + 1)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Nobody_sees_or_sets_the_room_of_somebody_else_s_session()
    {
        await using var w = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Rooms(w, w.Stranger).Get(w.SessionId, default)).Result);
        Assert.IsType<NotFoundResult>((await Rooms(w, w.Stranger)
            .Set(w.SessionId, new SetSessionRoomRequest(w.PublicRoom, null), default)).Result);
        Assert.Null((await SessionAsync(w)).PlaceRoomId);
    }

    [Fact]
    public async Task Renaming_a_room_renames_the_sessions_in_it()
    {
        await using var w = await SeedAsync();
        Ok(await Rooms(w, w.Me).Set(w.SessionId, new SetSessionRoomRequest(w.MyPrivateRoom, null), default));

        var rooms = As(new PlaceRoomController(w.F, new OrganizationSecurityService(w.F)), w.Me);
        Assert.IsType<OkObjectResult>((await rooms.Update(w.MyGroup, w.PlaceId, w.MyPrivateRoom,
            new SavePlaceRoomRequest("Room 217 (the haunted one)", null, null, IsPublic: false), default)).Result);

        Assert.Equal("Room 217 (the haunted one)", (await SessionAsync(w)).RoomName);
    }

    [Fact]
    public async Task Removing_a_room_a_session_is_in_retires_it_instead()
    {
        await using var w = await SeedAsync();
        Ok(await Rooms(w, w.Me).Set(w.SessionId, new SetSessionRoomRequest(w.MyPrivateRoom, null), default));

        var rooms = As(new PlaceRoomController(w.F, new OrganizationSecurityService(w.F)), w.Me);
        Assert.IsType<NoContentResult>(await rooms.Delete(w.MyGroup, w.PlaceId, w.MyPrivateRoom, default));

        await using var db = await w.Sqlite.NewContextAsync();
        var room = await db.PlaceRooms.AsNoTracking().SingleAsync(r => r.Id == w.MyPrivateRoom);
        Assert.False(room.IsActive);
        var session = await SessionAsync(w);
        Assert.Equal(w.MyPrivateRoom, session.PlaceRoomId);
        Assert.Equal("Room 217", session.RoomName);
    }

    [Fact]
    public async Task Publishing_to_another_place_lets_go_of_the_room_but_keeps_its_name()
    {
        await using var w = await SeedAsync();
        Ok(await Rooms(w, w.Me).Set(w.SessionId, new SetSessionRoomRequest(w.PublicRoom, null), default));

        var publisher = As(new FieldSessionPublishController(
            w.F, new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(), NullLogger<FieldSessionPublishController>.Instance,
            TestBundles.Store(new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object),
            new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object), w.Me);
        Assert.IsType<NoContentResult>(await publisher.Publish(
            w.SessionId, new FieldSessionPublishController.PublishRequest(w.OtherPlaceId), default));

        var session = await SessionAsync(w);
        Assert.Equal(w.OtherPlaceId, session.PlaceId);
        Assert.Null(session.PlaceRoomId);
        Assert.Equal("Kirkland Hall", session.RoomName);
    }
}
