using System.Security.Claims;
using Ben.Data.Common.Constants;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A venue keeps pictures of itself: its own, and ones organizers offer from their events (item 235 phase 12).
/// </summary>
public sealed class VenuePhotoTests
{
    private static readonly Guid OrganizerId = Guid.NewGuid();
    private static readonly Guid VenueOrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid ProfileId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private static async Task<SqliteTestDb> SeedAsync(bool published = true)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        db.Users.Add(new AppUser { Id = HostId, Email = "h@example.test", UserName = "h@example.test", DateCreated = now });
        db.Organizations.Add(new Organization { Id = OrganizerId, Name = "Nashville Paranormal", UrlName = "nashville", DateCreated = now, CreatedByAppUserId = HostId });
        db.Organizations.Add(new Organization { Id = VenueOrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = HostId });
        db.Places.Add(new Place { Id = PlaceId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = HostId });
        db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
        {
            Id = ProfileId, OrganizationId = VenueOrgId, PlaceId = PlaceId, VerifiedUtc = now, IsPublished = published,
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrganizerId, PlaceId = PlaceId, Name = "Séance Weekend", UrlName = "seance",
            StartsOn = now.Date, EndsOn = now.Date, LifecycleState = HostedEventLifecycleState.Ended, DateCreated = now, CreatedByAppUserId = HostId,
        });
        var typeId = Guid.NewGuid();
        db.UploadFileTypes.Add(new UploadFileType { Id = typeId, Name = "Pictures", DateCreated = now, CreatedByAppUserId = HostId });
        db.UploadFiles.Add(new UploadFile
        {
            Id = FileId, UploadFileTypeId = typeId, OwnerOrganizationId = OrganizerId, FileName = "ballroom.jpg", StoredFileName = "b.jpg",
            ContentType = "image/jpeg", FileSize = 10, StoragePath = "x/b.jpg", DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.HostedEventGalleryImages.Add(new HostedEventGalleryImage
        {
            Id = ImageId, HostedEventId = EventId, UploadFileId = FileId, Caption = "The ballroom", DateCreated = now, CreatedByAppUserId = HostId,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static Mock<IOrganizationSecurityService> Allowed()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return security;
    }

    private static ControllerContext SignedIn() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, HostId.ToString())], "Bearer")),
        },
    };

    private static HostedEventGalleryController Gallery(SqliteTestDb sqlite)
    {
        var security = Allowed();
        return new HostedEventGalleryController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, security.Object,
            new HostedEventAccess(security.Object), new Mock<IFileStorageService>().Object, new Mock<IMediaIngestService>().Object,
            new Mock<IMediaSanitizationService>().Object) { ControllerContext = SignedIn() };
    }

    private static (VenuePhotoController Controller, Mock<IMediaIngestService> Ingest) Library(SqliteTestDb sqlite)
    {
        var ingest = new Mock<IMediaIngestService>();
        return (new VenuePhotoController(sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, Allowed().Object,
            new Mock<IFileStorageService>().Object, ingest.Object, new Mock<IMediaSanitizationService>().Object) { ControllerContext = SignedIn() }, ingest);
    }

    private static (PublicUserPhotoController Controller, Mock<IFileStorageService> Storage) Public(SqliteTestDb sqlite)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>())).Returns(true);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([1]));
        var media = new Mock<IMediaIngestService>();
        media.Setup(m => m.ServingPathFor(It.IsAny<string>())).Returns<string>(p => p);
        return (new PublicUserPhotoController(sqlite.Factory, storage.Object, media.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        }, storage);
    }

    [Fact]
    public async Task An_offered_picture_waits_for_the_venue_and_is_not_public_until_kept()
    {
        await using var sqlite = await SeedAsync();

        var venue = Assert.IsType<GalleryVenueRecord>(Assert.IsType<OkObjectResult>(
            (await Gallery(sqlite).OfferToVenue(OrganizerId, EventId, ImageId, default)).Result).Value);
        Assert.Equal("The Thomas House", venue.VenueName);
        Assert.Contains(FileId, venue.OfferedUploadFileIds);

        await using (var db = await sqlite.NewContextAsync())
        {
            var offer = await db.VenuePhotos.SingleAsync();
            Assert.Null(offer.AcceptedUtc);
            Assert.Equal(OrganizerId, offer.OfferedByOrganizationId);
            Assert.Equal("The ballroom", offer.Caption);
        }

        Assert.IsType<NotFoundResult>(await Public(sqlite).Controller.GetVenuePhoto(FileId, default));

        var (library, _) = Library(sqlite);
        var photoId = (await sqlite.NewContextAsync()).VenuePhotos.Single().Id;
        Assert.IsType<OkObjectResult>((await library.Accept(VenueOrgId, ProfileId, photoId, default)).Result);

        Assert.IsType<FileStreamResult>(await Public(sqlite).Controller.GetVenuePhoto(FileId, default));
    }

    [Fact]
    public async Task Offering_twice_is_one_offer()
    {
        await using var sqlite = await SeedAsync();
        await Gallery(sqlite).OfferToVenue(OrganizerId, EventId, ImageId, default);
        await Gallery(sqlite).OfferToVenue(OrganizerId, EventId, ImageId, default);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(1, await db.VenuePhotos.CountAsync());
    }

    [Fact]
    public async Task Declining_an_offer_never_takes_the_organizers_picture_from_their_gallery()
    {
        await using var sqlite = await SeedAsync();
        await Gallery(sqlite).OfferToVenue(OrganizerId, EventId, ImageId, default);

        var (library, ingest) = Library(sqlite);
        var photoId = (await sqlite.NewContextAsync()).VenuePhotos.Single().Id;
        Assert.IsType<OkObjectResult>((await library.Delete(VenueOrgId, ProfileId, photoId, default)).Result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Empty(await db.VenuePhotos.ToListAsync());
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == FileId));
        ingest.Verify(i => i.DeleteAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_kept_picture_outlives_the_events_tidy_up()
    {
        await using var sqlite = await SeedAsync();
        await Gallery(sqlite).OfferToVenue(OrganizerId, EventId, ImageId, default);
        var (library, _) = Library(sqlite);
        await library.Accept(VenueOrgId, ProfileId, (await sqlite.NewContextAsync()).VenuePhotos.Single().Id, default);

        await using (var db = await sqlite.NewContextAsync())
        {
            var ev = await db.HostedEvents.SingleAsync();
            var paths = await EventRetention.ClearAsync(db, ev, DateTime.UtcNow, default);
            Assert.Empty(paths);
        }

        await using var check = await sqlite.NewContextAsync();
        Assert.Empty(await check.HostedEventGalleryImages.ToListAsync());
        Assert.True(await check.UploadFiles.AnyAsync(f => f.Id == FileId));
        Assert.IsType<FileStreamResult>(await Public(sqlite).Controller.GetVenuePhoto(FileId, default));
    }

    [Fact]
    public async Task An_unpublished_venue_page_shows_no_pictures()
    {
        await using var sqlite = await SeedAsync(published: false);
        await Gallery(sqlite).OfferToVenue(OrganizerId, EventId, ImageId, default);
        var (library, _) = Library(sqlite);
        await library.Accept(VenueOrgId, ProfileId, (await sqlite.NewContextAsync()).VenuePhotos.Single().Id, default);

        Assert.IsType<NotFoundResult>(await Public(sqlite).Controller.GetVenuePhoto(FileId, default));
    }
}
