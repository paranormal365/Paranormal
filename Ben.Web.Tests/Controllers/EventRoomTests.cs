using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Entities;
using Ben.Service.Models.Feed;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// An event's room: private to the people at the event, and what is posted stays the poster's
/// (item 235 phase 11).
/// </summary>
/// <remarks>
/// Ben, 2026-09-13: "Uploads during an event belong to the uploader but can be sent to and shared with
/// event organizer and venue." The ownership tests are that sentence, row by row.
/// </remarks>
public sealed class EventRoomTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid VenueOrgId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();
    private static readonly Guid OtherGuest = Guid.NewGuid();
    private static readonly Guid Waiting = Guid.NewGuid();

    private static readonly string MediaRoot = Path.Combine(Path.GetTempPath(), "ben-room-tests", Guid.NewGuid().ToString("N"));

    private static ClaimsPrincipal Who(Guid? id) => id is Guid g
        ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, g.ToString())], "Bearer"))
        : new ClaimsPrincipal(new ClaimsIdentity());

    private static PublicHostedEventRoomController Room(SqliteTestDb sqlite, Guid who)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(Host, OrgId, It.IsAny<OrganizationSecurityTable>(),
            It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        return new PublicHostedEventRoomController(sqlite.Factory, new HostedEventAccess(security.Object),
            TestMedia.StorageOnDisk(MediaRoot), TestMedia.IngestToDisk(MediaRoot), new ManualReviewScreener())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Who(who) } },
        };
    }

    private static IFormFile Photo()
    {
        using var bitmap = new SkiaSharp.SKBitmap(2, 2);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        var bytes = data.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "media", "the-stairs.jpg")
        {
            Headers = new HeaderDictionary(), ContentType = "image/jpeg",
        };
    }

    private static async Task<SqliteTestDb> SeedAsync(bool closed = false)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (Host, "Mrs Cole"), (Guest, "Ann"), (OtherGuest, "Bob"), (Waiting, "Dan") })
            db.Users.Add(new AppUser { Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}", DisplayName = name, DateCreated = now });

        db.Organizations.AddRange(
            new Organization { Id = OrgId, Name = "Nashville Paranormal", UrlName = "np", DateCreated = now, CreatedByAppUserId = Host },
            new Organization { Id = VenueOrgId, Name = "The Thomas House", UrlName = "th", DateCreated = now, CreatedByAppUserId = Host });
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place { Id = placeId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = Host });

        var grantId = Guid.NewGuid();
        db.OrganizationVenueGrants.Add(new OrganizationVenueGrant
        {
            Id = grantId, VenueOrganizationId = VenueOrgId, GranteeOrganizationId = OrgId, PlaceId = placeId,
            ValidFrom = now.Date, ValidTo = now.Date.AddDays(2), DateCreated = now, CreatedByAppUserId = Host,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = placeId, Name = "Lock-In", UrlName = "lock-in",
            StartsOn = now.Date, EndsOn = now.Date.AddDays(1), LifecycleState = HostedEventLifecycleState.Live,
            VenueGrantId = grantId, RoomClosedUtc = closed ? now.AddHours(-1) : null,
            DateCreated = now, CreatedByAppUserId = Host,
        });
        db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = EventId, Date = now.Date, DateCreated = now, CreatedByAppUserId = Host });

        foreach (var (lead, status) in new[] { (Guest, HostedEventBookingStatus.Confirmed), (OtherGuest, HostedEventBookingStatus.Confirmed), (Waiting, HostedEventBookingStatus.Requested) })
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = lead, PartySize = 1,
                Kind = HostedEventBookingKind.DayPass, Status = status, DateCreated = now, CreatedByAppUserId = lead,
            });

        // The feed type the room's photos use, and the feed switched on for the leak test.
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.FeedMediaFileTypeId, Name = "Feed Media",
            AllowAllExtensions = true, IsActive = true, DateCreated = now, CreatedByAppUserId = Host,
        });
        db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = SiteSettingKeys.FeaturePublicFeed, Value = "true", DateCreated = now, CreatedByAppUserId = Host });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static EventRoomRecord Ok(ActionResult<EventRoomRecord> result)
        => Assert.IsType<EventRoomRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Only_the_people_at_the_event_are_in_the_room()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<OkObjectResult>((await Room(sqlite, Guest).Get(EventId, null, default)).Result);
        Assert.IsType<OkObjectResult>((await Room(sqlite, Host).Get(EventId, null, default)).Result);
        Assert.IsType<NotFoundResult>((await Room(sqlite, Waiting).Get(EventId, null, default)).Result);
        Assert.IsType<NotFoundResult>((await Room(sqlite, Guid.NewGuid()).Get(EventId, null, default)).Result);
    }

    [Fact]
    public async Task A_photo_is_the_posters_own_file_and_goes_to_nobody_unless_they_send_it()
    {
        await using var sqlite = await SeedAsync();

        Ok(await Room(sqlite, Guest).Post(EventId, "The stairs at midnight", Photo(), sendToHosts: false, default, agreeToShow: true));

        await using var db = await sqlite.NewContextAsync();
        var message = await db.OrgMessages.SingleAsync(m => m.HostedEventId == EventId);
        var file = await db.UploadFiles.SingleAsync(f => f.Id == message.MediaUploadFileId);

        Assert.Equal(Guest, file.AppUserId);
        Assert.Null(file.OwnerOrganizationId);
        Assert.Null(message.OrganizationId);
        Assert.Empty(await db.UploadFileOrganizationShares.ToListAsync());
    }

    [Fact]
    public async Task Sending_it_on_shares_it_with_the_organizer_and_the_venue_and_it_stays_theirs()
    {
        await using var sqlite = await SeedAsync();
        var room = Ok(await Room(sqlite, Guest).Post(EventId, null, Photo(), sendToHosts: true, default, agreeToShow: true));
        Assert.Contains("still yours", room.Note);

        await using var db = await sqlite.NewContextAsync();
        var file = await db.UploadFiles.SingleAsync();
        Assert.Equal(Guest, file.AppUserId);

        var shares = await db.UploadFileOrganizationShares.Where(s => s.UploadFileId == file.Id && s.IsActive).ToListAsync();
        Assert.Equal(new[] { OrgId, VenueOrgId }.Order(), shares.Select(s => s.OrganizationId).Order());
        Assert.All(shares, s => Assert.Equal(Guest, s.SharedByAppUserId));
    }

    [Fact]
    public async Task Only_the_poster_can_send_their_photo_on()
    {
        await using var sqlite = await SeedAsync();
        var posted = Ok(await Room(sqlite, Guest).Post(EventId, null, Photo(), sendToHosts: false, default, agreeToShow: true));
        var messageId = posted.Messages.Single().Id;

        Assert.IsType<ConflictObjectResult>((await Room(sqlite, OtherGuest).SendToHosts(EventId, messageId, default)).Result);
        Assert.IsType<ConflictObjectResult>((await Room(sqlite, Host).SendToHosts(EventId, messageId, default)).Result);

        var sent = Ok(await Room(sqlite, Guest).SendToHosts(EventId, messageId, default));
        Assert.True(sent.Messages.Single().SentToHosts);
    }

    [Fact]
    public async Task Taking_a_post_down_leaves_the_file_in_the_posters_library()
    {
        await using var sqlite = await SeedAsync();
        var posted = Ok(await Room(sqlite, Guest).Post(EventId, "oops", Photo(), sendToHosts: false, default, agreeToShow: true));

        Ok(await Room(sqlite, Guest).Remove(EventId, posted.Messages.Single().Id, default));

        await using var db = await sqlite.NewContextAsync();
        Assert.Empty(await db.OrgMessages.Where(m => m.HostedEventId == EventId).ToListAsync());
        Assert.Single(await db.UploadFiles.Where(f => f.AppUserId == Guest).ToListAsync());
    }

    [Fact]
    public async Task A_closed_room_can_be_read_but_not_added_to()
    {
        await using var sqlite = await SeedAsync(closed: true);

        var room = Ok(await Room(sqlite, Guest).Get(EventId, null, default));
        Assert.False(room.CanPost);

        var refused = await Room(sqlite, Guest).Post(EventId, "Still here?", null, false, default);
        Assert.IsType<ConflictObjectResult>(refused.Result);
    }

    [Fact]
    public async Task A_hidden_post_is_gone_for_other_guests_and_still_there_for_the_host()
    {
        await using var sqlite = await SeedAsync();
        var posted = Ok(await Room(sqlite, Guest).Post(EventId, "Something regrettable", null, false, default));
        var messageId = posted.Messages.Single().Id;

        Assert.IsType<ForbidResult>((await Room(sqlite, OtherGuest).Hide(EventId, messageId, default)).Result);
        Ok(await Room(sqlite, Host).Hide(EventId, messageId, default));

        Assert.Empty(Ok(await Room(sqlite, OtherGuest).Get(EventId, null, default)).Messages);
        Assert.True(Ok(await Room(sqlite, Host).Get(EventId, null, default)).Messages.Single().IsHidden);
    }

    [Fact]
    public async Task Nothing_said_in_a_room_ever_reaches_the_public_feed()
    {
        await using var sqlite = await SeedAsync();
        Ok(await Room(sqlite, Guest).Post(EventId, "Room only", Photo(), sendToHosts: false, default, agreeToShow: true));

        var feed = new FeedController(sqlite.Factory, TestMedia.StorageOnDisk(MediaRoot), TestMedia.IngestToDisk(MediaRoot),
            new ManualReviewScreener(), new FeedLearningService(TestMedia.StorageOnDisk(MediaRoot), NullLogger<FeedLearningService>.Instance),
            NullLogger<FeedController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Who(Guest) } },
        };

        var page = (FeedPageRecord)Assert.IsType<OkObjectResult>((await feed.GetFeed(null, null, null, default)).Result).Value!;
        Assert.DoesNotContain(page.Posts, p => p.Body == "Room only");
    }

    [Fact]
    public async Task When_photos_are_kept_to_the_team_a_guest_can_write_but_not_add_one()
    {
        await using var sqlite = await SeedAsync();

        Assert.IsType<ForbidResult>((await Room(sqlite, Guest).Settings(EventId, new(EventPhotoPosting.TeamOnly), default)).Result);
        var room = Ok(await Room(sqlite, Host).Settings(EventId, new(EventPhotoPosting.TeamOnly), default));
        Assert.Equal(EventPhotoPosting.TeamOnly, room.PhotoPosting);

        var photo = await Room(sqlite, Guest).Post(EventId, "Look!", Photo(), false, default, agreeToShow: true);
        Assert.Contains("kept photos to the event's own team", Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(photo.Result).Value));

        Assert.IsType<OkObjectResult>((await Room(sqlite, Guest).Post(EventId, "Words are fine", null, false, default)).Result);
        Assert.IsType<OkObjectResult>((await Room(sqlite, Host).Post(EventId, "The team's photo", Photo(), false, default, agreeToShow: true)).Result);
        Assert.False(Ok(await Room(sqlite, Guest).Get(EventId, null, default)).CanAddPhotos);
    }

    [Fact]
    public async Task The_wall_shows_the_rooms_photos_and_not_a_hidden_one()
    {
        await using var sqlite = await SeedAsync();
        Ok(await Room(sqlite, Guest).Post(EventId, "Keep", Photo(), false, default, agreeToShow: true));
        var hideMe = Ok(await Room(sqlite, OtherGuest).Post(EventId, "Hide", Photo(), false, default, agreeToShow: true)).Messages.First(m => m.Body == "Hide").Id;
        Ok(await Room(sqlite, Guest).Post(EventId, "No photo here", null, false, default));
        Ok(await Room(sqlite, Host).Hide(EventId, hideMe, default));

        var wall = Assert.IsType<EventWallRecord>(Assert.IsType<OkObjectResult>((await Room(sqlite, Host).Photos(EventId, default)).Result).Value);
        Assert.Equal(["Keep"], wall.Photos.Select(p => p.Caption));
    }

    [Fact]
    public async Task The_wall_is_behind_the_organizers_and_the_venues_accounts_and_not_a_guests()
    {
        // Ben, 2026-09-13: some of the people in the photos do not want their images in public, and the
        // wall is what gets put on a screen.
        await using var sqlite = await SeedAsync();
        var venueStaff = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.Users.Add(new AppUser { Id = venueStaff, Email = "porter@example.test", UserName = "porter", DateCreated = DateTime.UtcNow });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = VenueOrgId, AppUserId = venueStaff, IsActive = true,
                Role = OrganizationMemberRole.Member, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
            });
            await db.SaveChangesAsync();
        }
        Ok(await Room(sqlite, Guest).Post(EventId, "Seen by the right people", Photo(), false, default, agreeToShow: true));

        Assert.IsType<OkObjectResult>((await Room(sqlite, Host).Photos(EventId, default)).Result);
        Assert.IsType<OkObjectResult>((await Room(sqlite, venueStaff).Photos(EventId, default)).Result);

        Assert.IsType<NotFoundResult>((await Room(sqlite, Guest).Photos(EventId, default)).Result);
        Assert.IsType<NotFoundResult>((await Room(sqlite, Waiting).Photos(EventId, default)).Result);
        Assert.IsType<NotFoundResult>((await Room(sqlite, Guid.NewGuid()).Photos(EventId, default)).Result);

        Assert.False(Ok(await Room(sqlite, Guest).Get(EventId, null, default)).CanSeeWall);
        Assert.True(Ok(await Room(sqlite, Host).Get(EventId, null, default)).CanSeeWall);
    }

    [Fact]
    public async Task A_photo_that_would_take_the_event_past_its_space_is_refused_in_words()
    {
        // Ben, 2026-09-13: a maximum storage size for an event. Set tiny here so one photo fills it.
        await using var sqlite = await SeedAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = SiteSettingKeys.EventStorageMegabytes, Value = "1", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host });
            db.UploadFiles.Add(new UploadFile
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.FeedMediaFileTypeId, AppUserId = Host,
                FileName = "big.jpg", StoredFileName = "big.jpg", ContentType = "image/jpeg", FileSize = 1024 * 1024,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
            });
            await db.SaveChangesAsync();
            var bigId = await db.UploadFiles.Where(f => f.FileName == "big.jpg").Select(f => f.Id).SingleAsync();
            db.OrgMessages.Add(new OrgMessage
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, AuthorAppUserId = Host, ChannelType = OrgMessageChannel.EventRoom,
                Body = "", MediaUploadFileId = bigId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
            });
            await db.SaveChangesAsync();
        }

        var refused = await Room(sqlite, Guest).Post(EventId, "One more", Photo(), false, default, agreeToShow: true);
        Assert.Contains("used its 1 MB", Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(refused.Result).Value));

        // Words still fit: text takes no space.
        Assert.IsType<OkObjectResult>((await Room(sqlite, Guest).Post(EventId, "Words only", null, false, default)).Result);
    }

    [Fact]
    public async Task A_guests_first_photo_waits_for_their_agreement_to_it_being_shown_and_the_team_is_not_asked()
    {
        await using var sqlite = await SeedAsync();

        Assert.True(Ok(await Room(sqlite, Guest).Get(EventId, null, default)).NeedsPhotoConsent);
        Assert.False(Ok(await Room(sqlite, Host).Get(EventId, null, default)).NeedsPhotoConsent);

        var refused = await Room(sqlite, Guest).Post(EventId, "First", Photo(), false, default);
        Assert.Contains("photo wall or slideshow", Assert.IsType<string>(Assert.IsType<ConflictObjectResult>(refused.Result).Value));

        Ok(await Room(sqlite, Guest).Post(EventId, "First", Photo(), false, default, agreeToShow: true));
        // Asked once: the second photo goes without the tick.
        Ok(await Room(sqlite, Guest).Post(EventId, "Second", Photo(), false, default));
        Ok(await Room(sqlite, Host).Post(EventId, "The team's", Photo(), false, default));

        await using var db = await sqlite.NewContextAsync();
        var consent = await db.EventPhotoConsents.SingleAsync();
        Assert.Equal(Guest, consent.AppUserId);
        Assert.Contains("photo wall", consent.Wording);
    }
}
