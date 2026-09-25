using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Controllers.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// A product page's extras (store sellers, backlog 251, P14): the store's reviews switch (off hides
/// the reviews and the stars everywhere, and nobody can write one); the item's own return and warranty
/// words; and up to three videos, served stripped and only while a product holds them.
/// </summary>
public sealed class StoreProductExtrasTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!, _buyer = null!;
    private Guid _hers;
    private string _root = null!;

    private static readonly byte[] Recording = "an mp4 with a location in it"u8.ToArray();
    private static readonly byte[] Clean = "the same mp4 with nothing in it"u8.ToArray();

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _root = Path.Combine(Path.GetTempPath(), "store-extras-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _ivan = StoreTestData.Person(db, "ivan");
        _buyer = StoreTestData.Person(db, "buyer");
        StoreTestData.StoreImageType(db, _admin);
        StoreTestData.StoreProductFileType(db, _admin);
        var shelf = StoreTestData.Category(db, _admin, "Trigger Objects");
        var hers = StoreTestData.Product(db, _admin, shelf);
        (hers.Name, hers.SellerAppUserId, hers.FirstOnSaleUtc) = ("Hand-Built REM Pod", _hazel.Id, StoreTestData.Now);
        _hers = hers.Id;

        // Two approved reviews: 5 and 3 stars.
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid, _buyer);
        order.PaidUtc = DateTime.UtcNow;
        foreach (var (who, stars) in new[] { (_buyer, 5), (_ivan, 3) })
            db.StoreReviews.Add(new StoreReview
            {
                Id = Guid.NewGuid(), ProductId = _hers, AuthorAppUserId = who.Id, OrderId = order.Id, Rating = stars, Title = "Works",
                Body = "It works well enough.", Status = StoreReviewStatus.Approved, DateCreated = DateTime.UtcNow, CreatedByAppUserId = who.Id,
            });
        var seller = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Seller, NormalizedName = "SELLER" };
        db.Roles.Add(seller);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _hazel.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _ivan.Id, RoleId = seller.Id });
        await db.SaveChangesAsync();
        await StoreRatingCaches.RecomputeAsync(db, _hers);
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Storage whose stripper works — it hands back <see cref="Clean"/> — so the served copy can be told from the original.</summary>
    private StoreImageStorage Images()
    {
        var stripper = new Mock<IAvMetadataStripper>();
        stripper.Setup(s => s.IsAvailable).Returns(true);
        stripper.Setup(s => s.CanStrip(It.Is<string?>(t => t != null && t.StartsWith("video/")))).Returns(true);
        stripper.Setup(s => s.StripAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Clean);
        var disk = TestMedia.StorageOnDisk(_root);
        var ingest = new MediaIngestService(disk, new FileMetadataExtractorService(), new MediaSanitizationService(), stripper.Object,
            NullLogger<MediaIngestService>.Instance);
        return new StoreImageStorage(disk, new MediaSanitizationService(), ingest);
    }

    private SellerStoreProductEditController Seller(AppUser who) => new(_sqlite.Factory, Images(), new CmsMarkupSanitizer(),
        new StoreSellerAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), NullLogger<StoreSellerAlerts>.Instance))
    {
        ControllerContext = StoreTestData.SignedInAs(who.Id),
    };

    private AdminStoreProductController Admin() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object, Images(), new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private PublicStoreController Page() => new(_sqlite.Factory, null!, Options.Create(new Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeOptions()))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private PublicStoreVideoController VideoDoor() => new(_sqlite.Factory, Images())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private MyStoreEngagementController Reviewer(AppUser who) => new(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(who.Id) };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;
    private static string Said<T>(ActionResult<T> r) => (string)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;

    private async Task<string> SlugAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreProducts.Where(p => p.Id == _hers).Select(p => p.Slug).SingleAsync();
    }

    private async Task<(decimal Average, int Count)> StarsAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreProducts.Where(p => p.Id == _hers).Select(p => new ValueTuple<decimal, int>(p.AverageRating, p.ReviewCount)).SingleAsync();
    }

    private static IFormFile Video(string contentType = "video/mp4", string name = "demo.mp4", long? length = null)
        => new FormFile(new MemoryStream(Recording), 0, length ?? Recording.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };

    // ── reviews ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Switching_reviews_off_hides_them_and_the_stars_and_stops_new_ones()
    {
        Assert.Equal((4m, 2), await StarsAsync());

        Ok(await Admin().SaveExtras(_hers, new SaveStoreExtrasRequest(null, null, ReviewsEnabled: false), default));

        Assert.Equal((0m, 0), await StarsAsync());   // cards, sorts and filters read these
        var page = Ok(await Page().Product(await SlugAsync(), null, default));
        Assert.Equal((false, 0), (page.ReviewsEnabled, page.Reviews.Count));
        Assert.Equal(0, Ok(await Page().Reviews(await SlugAsync(), null, null, default)).Total);
        Assert.Equal(StoreReviewSentences.ReviewsOff,
            Said(await Reviewer(_buyer).Review(_hers, new SubmitStoreReviewRequest(4, "Again", "Still works well enough."), default)));

        // A seller's save can't switch them back on — the switch isn't in her request at all.
        Ok(await Seller(_hazel).SaveExtras(_hers, new SaveSellerExtrasRequest("Unused and boxed, within 30 days.", null), default));
        Assert.False(Ok(await Admin().Extras(_hers, default)).ReviewsEnabled);

        Ok(await Admin().SaveExtras(_hers, new SaveStoreExtrasRequest("Unused and boxed, within 30 days.", null, ReviewsEnabled: true), default));
        Assert.Equal((4m, 2), await StarsAsync());

        await using var db = await _sqlite.NewContextAsync();
        var lines = await db.StoreProductChanges.Where(c => c.ProductId == _hers && c.Area == StoreProductChangeArea.Page).OrderBy(c => c.OccurredUtc).Select(c => c.Summary).ToListAsync();
        Assert.Equal(["Switched reviews off — its reviews and stars are hidden.", "Changed its return words.", "Switched reviews on."], lines);
    }

    // ── words ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_items_own_return_and_warranty_words_show_on_its_page()
    {
        Ok(await Seller(_hazel).SaveExtras(_hers, new SaveSellerExtrasRequest("  Unused and boxed.  ", "Repaired free for a year."), default));
        var page = Ok(await Page().Product(await SlugAsync(), null, default));
        Assert.Equal(("Unused and boxed.", "Repaired free for a year."), (page.ReturnPolicyText, page.WarrantyText));

        Assert.Contains("2000 characters", Said(await Seller(_hazel).SaveExtras(_hers, new SaveSellerExtrasRequest(new string('x', 2001), null), default)));
        Assert.IsType<NotFoundResult>((await Seller(_ivan).SaveExtras(_hers, new SaveSellerExtrasRequest("Mine", null), default)).Result);
        Ok(await Seller(_hazel).SaveExtras(_hers, new SaveSellerExtrasRequest(null, " "), default));
        page = Ok(await Page().Product(await SlugAsync(), null, default));
        Assert.Equal((null, null), (page.ReturnPolicyText, page.WarrantyText));
    }

    // ── videos ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_video_plays_on_the_page_from_its_stripped_copy()
    {
        var extras = Ok(await Seller(_hazel).AddVideo(_hers, Video(), "Setting it up", default));
        var video = Assert.Single(extras.Videos);
        Assert.Equal(("Setting it up", "video/mp4"), (video.Title, video.ContentType));
        Assert.Equal(video.UploadFileId, Assert.Single(Ok(await Page().Product(await SlugAsync(), null, default)).Videos!).UploadFileId);

        var served = Assert.IsType<FileStreamResult>(await VideoDoor().Get(video.UploadFileId, default));
        Assert.True(served.EnableRangeProcessing);
        using var bytes = new MemoryStream();
        await served.FileStream.CopyToAsync(bytes);
        Assert.Equal(Clean, bytes.ToArray());

        // The door serves only what a product's video holds — not any other upload, however real.
        var stranger = Guid.NewGuid();
        await using (var db = await _sqlite.NewContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = stranger, UploadFileTypeId = Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.StoreProductFileTypeId, FileName = "case.mp4",
                StoredFileName = "case.mp4", ContentType = "video/mp4", FileSize = 4, StoragePath = "cases/case.mp4",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = _admin.Id,
            });
            await db.SaveChangesAsync();
        }
        Assert.IsType<NotFoundResult>(await VideoDoor().Get(stranger, default));

        Assert.Empty(Ok(await Seller(_hazel).DeleteVideo(_hers, video.Id, default)).Videos);
        Assert.IsType<NotFoundResult>(await VideoDoor().Get(video.UploadFileId, default));
    }

    [Fact]
    public async Task Videos_are_mp4_webm_or_mov_three_at_most_and_only_the_sellers_own()
    {
        Assert.Contains("mp4, webm or mov", Said(await Seller(_hazel).AddVideo(_hers, Video("application/zip", "demo.zip"), null, default)));
        Assert.Contains("95 MB", Said(await Seller(_hazel).AddVideo(_hers, Video(length: StoreProductVideo.MaxBytes + 1), null, default)));
        Assert.IsType<NotFoundResult>((await Seller(_ivan).AddVideo(_hers, Video(), null, default)).Result);

        Ok(await Seller(_hazel).AddVideo(_hers, Video(), "One", default));
        Ok(await Seller(_hazel).AddVideo(_hers, Video("video/webm", "two.webm"), "Two", default));
        var three = Ok(await Admin().AddVideo(_hers, Video("video/quicktime", "three.mov"), "Three", default));
        Assert.Equal(["One", "Two", "Three"], three.Videos.Select(v => v.Title));
        Assert.Contains("3 videos at most", Said(await Seller(_hazel).AddVideo(_hers, Video(), "Four", default)));

        Assert.IsType<NotFoundResult>((await Seller(_ivan).DeleteVideo(_hers, three.Videos[0].Id, default)).Result);
    }
}
