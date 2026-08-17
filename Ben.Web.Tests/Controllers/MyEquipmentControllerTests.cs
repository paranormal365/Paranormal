using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Ben.Data.WebApi.Services;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="MyEquipmentController"/> — the signed-in user's own equipment list
/// (backlog item #55, Phase 1: personal gear, no sharing/org-owned/checkout yet).
/// </summary>
/// <remarks>
/// The rule these tests exist to hold: ownership checks match id AND owner together and answer
/// 404 on a mismatch, never 403 — confirming an id exists to someone who doesn't own it is its
/// own small leak. The serial number is the other rule under test: present for the owner, absent
/// for everyone else (here, absent because a stranger can't even load the row).
/// </remarks>
public class MyEquipmentControllerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid CategoryId, Guid BrandId, Guid ModelId);

    private static MyEquipmentController Build(
        IDbContextFactory<BenDataContext> f, Guid userId, Mock<IFileStorageService>? storageMock = null)
    {
        // Only stub defaults when the caller didn't hand in a pre-configured mock — re-applying a
        // Setup on top of the caller's own overwrites it (Moq's "last Setup wins"), which is
        // exactly the trap that made DeleteAsync's expected path silently diverge here once.
        var storage = storageMock ?? new Mock<IFileStorageService>();
        if (storageMock is null)
        {
            storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path");
            storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);
        }

        return new MyEquipmentController(f, storage.Object, new Mock<IAuditLogService>().Object, BuildIngest(storage))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };
    }

    private static IFormFile MakeFile(string fileName = "gear.jpg", string contentType = "image/jpeg", long size = 64)
    {
        var fileMock = new Mock<IFormFile>();
        var bytes    = TestImages.Jpeg();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(bytes.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
        return fileMock.Object;
    }

    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var categoryId = Guid.NewGuid();
        var brandId    = Guid.NewGuid();
        var modelId    = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = OwnerId, UserName = "owner@t", Email = "owner@t", DisplayName = "Owner" });
        db.Users.Add(new AppUser { Id = StrangerId, UserName = "stranger@t", Email = "stranger@t", DisplayName = "Stranger" });

        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Zoom", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "H1n", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        await db.SaveChangesAsync();

        return new World(factory, categoryId, brandId, modelId);
    }

    /// <summary>
    /// A real ingest service over the mocked storage — tests exercise the actual sanitize/extract
    /// path rather than mocking past the thing phase 6a exists to guarantee.
    /// </summary>
    private static IMediaIngestService BuildIngest(Mock<IFileStorageService>? storage = null)
        => new MediaIngestService(
            (storage ?? new Mock<IFileStorageService>()).Object,
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            NullLogger<MediaIngestService>.Instance);

    [Fact]
    public async Task Create_Then_GetAll_ReturnsItemWithSerialVisibleToOwner()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, OwnerId);

        var created = await ctrl.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", "SN-12345", null, "Bought used"), default);
        var record = Assert.IsType<EquipmentItemRecord>(Assert.IsType<OkObjectResult>(created.Result).Value);
        Assert.Equal("SN-12345", record.SerialNumber);
        Assert.True(record.Flags.IsOwner);
        Assert.True(record.Flags.CanSeeSerial);

        var all = await ctrl.GetAll(default);
        var list = Assert.IsAssignableFrom<IEnumerable<EquipmentItemRecord>>(Assert.IsType<OkObjectResult>(all.Result).Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetOne_ForSomeoneElsesItem_ReturnsNotFound_NeverForbid()
    {
        var w = await SeedAsync();
        var owner = Build(w.Factory, OwnerId);
        var created = await owner.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", "SN-1", null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;

        var stranger = Build(w.Factory, StrangerId);
        var result = await stranger.GetOne(itemId, default);

        // Never a 403 here — confirming existence to a non-owner is its own leak.
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ForSomeoneElsesItem_ReturnsNotFound()
    {
        var w = await SeedAsync();
        var owner = Build(w.Factory, OwnerId);
        var created = await owner.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", "SN-1", null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;

        var stranger = Build(w.Factory, StrangerId);
        var result = await stranger.Update(itemId, new UpsertEquipmentItemRequest(w.ModelId, "Hijacked", null, null, null), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ForSomeoneElsesItem_ReturnsNotFound_AndLeavesItemIntact()
    {
        var w = await SeedAsync();
        var owner = Build(w.Factory, OwnerId);
        var created = await owner.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", "SN-1", null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;

        var stranger = Build(w.Factory, StrangerId);
        var deleteResult = await stranger.Delete(itemId, default);
        Assert.IsType<NotFoundResult>(deleteResult);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.EquipmentItems.AnyAsync(i => i.Id == itemId));
    }

    [Fact]
    public async Task AttachPhoto_FirstPhoto_BecomesPrimary_AndVisibleOnItem()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, OwnerId);
        var created = await ctrl.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", null, null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;

        var photoResult = await ctrl.AttachPhoto(itemId, MakeFile(), default);
        var photo = Assert.IsType<EquipmentItemPhotoRecord>(Assert.IsType<OkObjectResult>(photoResult.Result).Value);
        Assert.True(photo.IsPrimary);

        var fetched = await ctrl.GetOne(itemId, default);
        var record = (EquipmentItemRecord)((OkObjectResult)fetched.Result!).Value!;
        Assert.Single(record.Photos);
        Assert.True(record.Photos[0].IsPrimary);
    }

    [Fact]
    public async Task DetachPhoto_DeletesUploadFileRow_AndCallsStorageDelete()
    {
        var w = await SeedAsync();
        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path.jpg");
        storageMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storageMock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var ctrl = Build(w.Factory, OwnerId, storageMock);
        var created = await ctrl.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", null, null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;
        var photoResult = await ctrl.AttachPhoto(itemId, MakeFile(), default);
        var photoId = ((EquipmentItemPhotoRecord)((OkObjectResult)photoResult.Result!).Value!).Id;

        var detachResult = await ctrl.DetachPhoto(itemId, photoId, default);
        Assert.IsType<NoContentResult>(detachResult);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False(await db.EquipmentItemPhotos.AnyAsync(p => p.Id == photoId));
        Assert.Empty(await db.UploadFiles.Where(f => f.UploadFileTypeId == Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.EquipmentPhotoFileTypeId).ToListAsync());
        storageMock.Verify(s => s.DeleteAsync("fake/path.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetachPrimaryPhoto_PromotesNextPhotoToPrimary()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, OwnerId);
        var created = await ctrl.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", null, null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;

        var first  = (EquipmentItemPhotoRecord)((OkObjectResult)(await ctrl.AttachPhoto(itemId, MakeFile("a.jpg"), default)).Result!).Value!;
        var second = (EquipmentItemPhotoRecord)((OkObjectResult)(await ctrl.AttachPhoto(itemId, MakeFile("b.jpg"), default)).Result!).Value!;
        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        await ctrl.DetachPhoto(itemId, first.Id, default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var remaining = await db.EquipmentItemPhotos.SingleAsync(p => p.Id == second.Id);
        Assert.True(remaining.IsPrimary);
    }

    [Fact]
    public async Task Delete_RemovesItem_Photos_AndUploadFiles()
    {
        var w = await SeedAsync();
        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path.jpg");
        storageMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storageMock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var ctrl = Build(w.Factory, OwnerId, storageMock);
        var created = await ctrl.Create(new UpsertEquipmentItemRequest(w.ModelId, "My H1n", null, null, null), default);
        var itemId = ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;
        await ctrl.AttachPhoto(itemId, MakeFile(), default);

        var deleteResult = await ctrl.Delete(itemId, default);
        Assert.IsType<NoContentResult>(deleteResult);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False(await db.EquipmentItems.AnyAsync(i => i.Id == itemId));
        Assert.False(await db.EquipmentItemPhotos.AnyAsync(p => p.EquipmentItemId == itemId));
        storageMock.Verify(s => s.DeleteAsync("fake/path.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Metadata separation (phase 6a) ───────────────────────────────────────

    /// <summary>
    /// Ben's rule: metadata is linked to the record and removed from the media. This asserts both
    /// halves of that in one pass on a real upload — the row exists, and the bytes that would be
    /// served carry nothing.
    /// </summary>
    [Fact]
    public async Task UploadingAPhotoRecordsItsMetadataAndServesStrippedBytes()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: false);

        // Capture what actually got written, per path, so we can inspect the served copy.
        var written = new Dictionary<string, byte[]>();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path.jpg");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns((string path, Stream data, CancellationToken _) =>
               {
                   using var buffer = new MemoryStream();
                   data.CopyTo(buffer);
                   written[path] = buffer.ToArray();
                   return Task.CompletedTask;
               });

        var ctrl = Build(w.Factory, OwnerId, storage);
        var result = await ctrl.AttachPhoto(itemId, GpsPhoto(), default);
        Assert.IsType<OkObjectResult>(result.Result);

        // The metadata row exists and carries the GPS that was in the file.
        await using var db = await w.Factory.CreateDbContextAsync();
        var metadata = await db.UploadFileMetadata.SingleAsync();
        Assert.NotNull(metadata.GpsLatitude);

        // And the copy that gets served has none of it.
        Assert.True(written.ContainsKey("fake/path.jpg.clean.jpg"), "no sanitized copy was written");
        Assert.False(HasGps(written["fake/path.jpg.clean.jpg"]));

        // The original is kept, untouched, carrying its GPS — it is the evidence.
        Assert.True(HasGps(written["fake/path.jpg"]));
    }

    [Fact]
    public async Task UploadingSomethingThatIsNotAnImageIsRefused()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: false);

        var notAnImage = new Mock<IFormFile>();
        var bytes = "definitely not a jpeg"u8.ToArray();
        notAnImage.Setup(f => f.FileName).Returns("sneaky.jpg");
        notAnImage.Setup(f => f.Length).Returns(bytes.Length);
        notAnImage.Setup(f => f.ContentType).Returns("image/jpeg");
        notAnImage.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));

        var result = await Build(w.Factory, OwnerId).AttachPhoto(itemId, notAnImage.Object, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>A real JPEG carrying a GPS EXIF block — see MediaSanitizationServiceTests.</summary>
    private static IFormFile GpsPhoto()
    {
        var bytes = TestImages.JpegWithGps();
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("gps.jpg");
        file.Setup(f => f.Length).Returns(bytes.Length);
        file.Setup(f => f.ContentType).Returns("image/jpeg");
        file.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
        return file.Object;
    }

    private static bool HasGps(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        return MetadataExtractor.ImageMetadataReader.ReadMetadata(stream)
            .Any(d => d is MetadataExtractor.Formats.Exif.GpsDirectory);
    }

    // ── Photo byte access ────────────────────────────────────────────────────
    //
    // GetContent is deliberately not blanket [Authorize] — a publicly-listed item has to show its
    // photos to visitors with no token. These pin the boundary that widening created.

    private static EquipmentPhotoContentController BuildPhotos(
        IDbContextFactory<BenDataContext> f, Guid? userId, Mock<IFileStorageService>? storageMock = null)
    {
        var storage = storageMock ?? new Mock<IFileStorageService>();
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        var identity = userId is null
            ? new ClaimsIdentity()   // anonymous: no authentication type, no claims
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer");

        return new EquipmentPhotoContentController(f, storage.Object, BuildIngest(storage))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static async Task<Guid> AttachPhotoAndGetIdAsync(World w, Guid itemId)
    {
        var ctrl = Build(w.Factory, OwnerId);
        var photoResult = await ctrl.AttachPhoto(itemId, MakeFile(), default);
        return ((EquipmentItemPhotoRecord)((OkObjectResult)photoResult.Result!).Value!).Id;
    }

    private static async Task<Guid> CreateItemAsync(World w, bool publiclyListed)
    {
        var ctrl = Build(w.Factory, OwnerId);
        var created = await ctrl.Create(
            new UpsertEquipmentItemRequest(w.ModelId, "Recorder", "SN-9", null, null, publiclyListed), default);
        return ((EquipmentItemRecord)((OkObjectResult)created.Result!).Value!).Id;
    }

    [Fact]
    public async Task PhotoContent_OfAPrivateItem_IsNotFoundForAnAnonymousCaller()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: false);
        var photoId = await AttachPhotoAndGetIdAsync(w, itemId);

        var result = await BuildPhotos(w.Factory, userId: null).GetContent(photoId, default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PhotoContent_OfAPrivateItem_IsNotFoundForAnotherSignedInUser()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: false);
        var photoId = await AttachPhotoAndGetIdAsync(w, itemId);

        var result = await BuildPhotos(w.Factory, StrangerId).GetContent(photoId, default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PhotoContent_OfAPubliclyListedItem_IsServedToAnAnonymousCaller()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: true);
        var photoId = await AttachPhotoAndGetIdAsync(w, itemId);

        var result = await BuildPhotos(w.Factory, userId: null).GetContent(photoId, default);
        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task PhotoContent_OfAPubliclyListedButRetiredItem_IsNotFoundAnonymously()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: true);
        var photoId = await AttachPhotoAndGetIdAsync(w, itemId);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == itemId);
            item.IsRetired = true;
            await db.SaveChangesAsync();
        }

        var result = await BuildPhotos(w.Factory, userId: null).GetContent(photoId, default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PhotoContent_OfAPrivateItem_IsServedToItsOwner()
    {
        var w = await SeedAsync();
        var itemId = await CreateItemAsync(w, publiclyListed: false);
        var photoId = await AttachPhotoAndGetIdAsync(w, itemId);

        var result = await BuildPhotos(w.Factory, OwnerId).GetContent(photoId, default);
        Assert.IsType<FileStreamResult>(result);
    }

    // ── Visibility and lending fields ────────────────────────────────────────

    [Fact]
    public async Task Create_DefaultsToPrivateAndNotLoanable()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, OwnerId);

        var created = await ctrl.Create(new UpsertEquipmentItemRequest(w.ModelId, "Recorder", null, null, null), default);
        var record = (EquipmentItemRecord)((OkObjectResult)created.Result!).Value!;

        // Publishing property and offering to lend it are both opt-in.
        Assert.False(record.IncludeInGlobalCatalog);
        Assert.Equal(EquipmentLoanAudience.NotLoanable, record.LoanAudience);
    }

    [Fact]
    public async Task Update_PersistsCombinedLoanAudience()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, OwnerId);
        var itemId = await CreateItemAsync(w, publiclyListed: false);

        var both = EquipmentLoanAudience.SharedGroups | EquipmentLoanAudience.GroupMembers;
        var updated = await ctrl.Update(itemId,
            new UpsertEquipmentItemRequest(w.ModelId, "Recorder", null, null, null, true, both), default);
        var record = (EquipmentItemRecord)((OkObjectResult)updated.Result!).Value!;

        Assert.True(record.IncludeInGlobalCatalog);
        Assert.Equal(both, record.LoanAudience);
        Assert.True(record.LoanAudience.HasFlag(EquipmentLoanAudience.SharedGroups));
        Assert.False(record.LoanAudience.HasFlag(EquipmentLoanAudience.IndividualUsers));
    }

    [Fact]
    public async Task Create_WithUnknownModel_ReturnsBadRequest()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, OwnerId);
        var result = await ctrl.Create(new UpsertEquipmentItemRequest(Guid.NewGuid(), "Ghost Model", null, null, null), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
