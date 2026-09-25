using System.Net;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Web.Website.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Store pictures serve to anybody while something in the store points at them — even with the
/// shop switched off — and never otherwise (storefront S1.8).
/// </summary>
public sealed class PublicStoreImageControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private string _root = null!;
    private AppUser _admin = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _root = Path.Combine(Path.GetTempPath(), "store-img-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        StoreTestData.StoreImageType(db, _admin);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private PublicStoreImageController Controller() => new(_sqlite.Factory, TestMedia.StorageOnDisk(_root), new MediaSanitizationService())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    /// <summary>A stored picture (bytes and thumbnail on disk), attached to nothing yet.</summary>
    private async Task<Guid> StoredPictureAsync()
    {
        var storage = new StoreImageStorage(TestMedia.StorageOnDisk(_root), new MediaSanitizationService(), TestMedia.IngestToDisk(_root));
        await using var db = await _sqlite.NewContextAsync();
        var (file, _) = await storage.SaveAsync(db, StoreTestData.Jpeg(2400, 1600), "image/jpeg", "a.jpg", "products/x", _admin.Id, default);
        await db.SaveChangesAsync();
        return file!.Id;
    }

    [Fact]
    public async Task A_picture_nothing_in_the_store_points_at_is_not_served()
    {
        var id = await StoredPictureAsync();
        Assert.IsType<NotFoundResult>(await Controller().Get(id, default));
    }

    /// <summary>The shop is dark (no features.store row) and the product hidden; the admin still sees it.</summary>
    [Fact]
    public async Task A_hidden_product_s_picture_serves_while_the_store_is_off_and_caches_for_a_year()
    {
        var id = await StoredPictureAsync();
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            var product = StoreTestData.Product(db, admin, StoreTestData.Category(db, admin, active: false), active: false);
            db.StoreProductImages.Add(new StoreProductImage
            {
                Id = Guid.NewGuid(), ProductId = product.Id, UploadFileId = id, DateCreated = StoreTestData.Now, CreatedByAppUserId = admin.Id,
            });
            await db.SaveChangesAsync();
            Assert.False(await db.SiteSettings.AnyAsync());
        }

        var controller = Controller();
        var full = Assert.IsType<FileStreamResult>(await controller.Get(id, default));
        Assert.Equal(PublicStoreImageController.CacheForever, controller.Response.Headers.CacheControl.ToString());
        using var fullBitmap = SkiaSharp.SKBitmap.Decode(full.FileStream);
        Assert.Equal(1600, fullBitmap.Width);

        var thumb = Assert.IsType<FileStreamResult>(await Controller().GetThumbnail(id, default));
        using var thumbBitmap = SkiaSharp.SKBitmap.Decode(thumb.FileStream);
        Assert.Equal(400, thumbBitmap.Width);
    }

    [Fact]
    public async Task A_category_picture_and_a_seeded_one_in_the_row_both_serve()
    {
        var seeded = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.StoreImageFileTypeId,
            FileName = "seed.jpg", StoredFileName = "seed.jpg", ContentType = "image/jpeg", FileData = StoreTestData.Jpeg(120, 90),
            IsPublic = true, DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id,
        };
        await using (var db = await _sqlite.NewContextAsync())
        {
            db.UploadFiles.Add(seeded);
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            StoreTestData.Category(db, admin).ImageUploadFileId = seeded.Id;
            await db.SaveChangesAsync();
        }

        var served = Assert.IsType<FileContentResult>(await Controller().Get(seeded.Id, default));
        Assert.Equal(seeded.FileData, served.FileContents);
    }
}

/// <summary>The website's relay passes a caller's cache rule through, and keeps its private default otherwise.</summary>
public sealed class MediaProxyTests
{
    private sealed class Upstream : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
    }

    private static async Task<string> CacheControlAsync(string? cacheControl)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Upstream()));
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await MediaProxy.StreamAsync("http://api.test/x", null, factory.Object, ctx, default, cacheControl);
        return ctx.Response.Headers.CacheControl.ToString();
    }

    [Fact]
    public async Task A_public_picture_is_cached_by_anybody_and_everything_else_stays_private()
    {
        Assert.Equal("public, max-age=31536000, immutable", await CacheControlAsync("public, max-age=31536000, immutable"));
        Assert.Equal("private, max-age=3600", await CacheControlAsync(null));
    }
}
