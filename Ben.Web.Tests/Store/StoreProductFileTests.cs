using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Controllers.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
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
/// A product's files and the buyer's downloads (store sellers, backlog 251, P11): each file is for
/// buyers or private (Ben, 09/24/2026); a buyer downloads from a paid order that isn't cancelled,
/// and not for a line wholly refunded; the product's seller and the store's staff keep everything.
/// </summary>
public sealed class StoreProductFileTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!, _buyer = null!;
    private Guid _categoryId;
    private string _root = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _root = Path.Combine(Path.GetTempPath(), "store-files-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _ivan = StoreTestData.Person(db, "ivan");
        _buyer = StoreTestData.Person(db, "buyer");
        StoreTestData.StoreImageType(db, _admin);
        StoreTestData.StoreProductFileType(db, _admin);
        _categoryId = StoreTestData.Category(db, _admin, "Trigger Objects").Id;
        var seller = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Seller, NormalizedName = "SELLER" };
        db.Roles.Add(seller);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _hazel.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _ivan.Id, RoleId = seller.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StoreImageStorage Images() => new(TestMedia.StorageOnDisk(_root), new MediaSanitizationService(), TestMedia.IngestToDisk(_root));

    private SellerStoreProductEditController Seller(AppUser who) => new(_sqlite.Factory, Images(), new CmsMarkupSanitizer(),
        new StoreSellerAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), NullLogger<StoreSellerAlerts>.Instance))
    {
        ControllerContext = StoreTestData.SignedInAs(who.Id),
    };

    private AdminStoreProductController Admin() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object, Images(), new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    /// <summary>The keepers' door. The SuperAdmin policy is the only thing mocked: it says yes for the admin alone.</summary>
    private StoreProductFileController Keepers(AppUser who)
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), RoleNames.SuperAdmin))
            .ReturnsAsync(who.Id == _admin.Id ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        return new StoreProductFileController(_sqlite.Factory, Images(), authorization.Object) { ControllerContext = StoreTestData.SignedInAs(who.Id) };
    }

    private StoreOrderController Buyers(Guid? signedIn = null)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        return new StoreOrderController(_sqlite.Factory, new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site), site, Images())
        {
            ControllerContext = signedIn is { } who ? StoreTestData.SignedInAs(who)
                : new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } },
        };
    }

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;
    private static List<StoreProductFileRecord> Files(ActionResult<IEnumerable<StoreProductFileRecord>> r) => Ok(r).ToList();
    private static string Said<T>(ActionResult<T> r) => (string)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;

    private async Task<Guid> DraftAsync() => Ok(await Seller(_hazel).Create(new CreateSellerItemRequest("REM Pod", _categoryId), default)).Item.Id;

    private static readonly byte[] Firmware = "firmware image bytes"u8.ToArray();

    private Task<ActionResult<IEnumerable<StoreProductFileRecord>>> UploadAsync(AppUser who, Guid id, string title, StoreFileAudience audience,
        StoreProductFileKind kind = StoreProductFileKind.Firmware)
        => Seller(who).AddFile(id, StoreTestData.Upload(Firmware, "application/octet-stream", "rempod-2.1.bin"), title, kind, audience, "2.1", default);

    private Task<ActionResult<IEnumerable<StoreProductFileRecord>>> ManualAsync(Guid id, StoreFileAudience audience, string body = "# Setting up\n\nPress **power**.<script>alert(1)</script>")
        => Seller(_hazel).AddManual(id, new SaveStoreManualRequest("Quick start", body, IsMarkdown: true, audience, "1.0"), default);

    // ── the seller's files ──────────────────────────────────────────────────

    [Fact]
    public async Task A_seller_adds_a_file_and_a_written_manual_and_history_says_who_gets_each()
    {
        var id = await DraftAsync();
        Files(await UploadAsync(_hazel, id, "Firmware 2.1", StoreFileAudience.Buyers));
        var files = Files(await ManualAsync(id, StoreFileAudience.Private));

        Assert.Equal(["Firmware 2.1", "Quick start"], files.Select(f => f.Title));
        var firmware = files[0];
        Assert.Equal((StoreProductFileKind.Firmware, StoreFileAudience.Buyers, "rempod-2.1.bin", (long?)Firmware.Length, false),
            (firmware.Kind, firmware.Audience, firmware.FileName, firmware.SizeBytes, firmware.IsManual));
        Assert.Equal((StoreProductFileKind.Manual, StoreFileAudience.Private, true), (files[1].Kind, files[1].Audience, files[1].IsManual));

        await using var db = await _sqlite.NewContextAsync();
        var manual = await db.StoreProductFiles.SingleAsync(f => f.ManualHtml != null);
        Assert.Contains("<h1", manual.ManualHtml);
        Assert.Contains("<strong>power</strong>", manual.ManualHtml);
        Assert.DoesNotContain("<script", manual.ManualHtml);

        var history = await db.StoreProductChanges.Where(c => c.ProductId == id && c.Area == StoreProductChangeArea.Files).Select(c => c.Summary).ToListAsync();
        Assert.Contains("Added firmware “Firmware 2.1” (for buyers).", history);
        Assert.Contains("Wrote the manual “Quick start” (private).", history);
    }

    [Fact]
    public async Task A_file_needs_a_title_and_fits_under_95_MB()
    {
        var id = await DraftAsync();
        Assert.Contains("title", Said(await UploadAsync(_hazel, id, "  ", StoreFileAudience.Buyers)));

        var huge = new FormFile(new MemoryStream(Firmware), 0, StoreProductFile.MaxBytes + 1, "file", "huge.zip")
        {
            Headers = new HeaderDictionary(), ContentType = "application/zip",
        };
        Assert.Contains("95 MB", Said(await Seller(_hazel).AddFile(id, huge, "Everything", StoreProductFileKind.Software, StoreFileAudience.Buyers, null, default)));
        Assert.Contains("empty", Said(await ManualAsync(id, StoreFileAudience.Buyers, body: "   ")));

        await using var db = await _sqlite.NewContextAsync();
        Assert.False(await db.StoreProductFiles.AnyAsync());
    }

    [Fact]
    public async Task Another_seller_cannot_see_add_change_or_remove_the_files()
    {
        var id = await DraftAsync();
        var file = Files(await UploadAsync(_hazel, id, "Firmware 2.1", StoreFileAudience.Private)).Single();

        Assert.IsType<NotFoundResult>((await Seller(_ivan).Files(id, default)).Result);
        Assert.IsType<NotFoundResult>((await UploadAsync(_ivan, id, "Mine now", StoreFileAudience.Buyers)).Result);
        Assert.IsType<NotFoundResult>((await Seller(_ivan).SaveFile(id, file.Id,
            new SaveStoreProductFileRequest("Mine", StoreProductFileKind.Other, StoreFileAudience.Buyers, null, 0), default)).Result);
        Assert.IsType<NotFoundResult>((await Seller(_ivan).DeleteFile(id, file.Id, default)).Result);

        await using var db = await _sqlite.NewContextAsync();
        var kept = await db.StoreProductFiles.SingleAsync();
        Assert.Equal(("Firmware 2.1", StoreFileAudience.Private), (kept.Title, kept.Audience));
    }

    [Fact]
    public async Task Changing_who_gets_a_file_is_recorded_and_removing_it_removes_its_bytes()
    {
        var id = await DraftAsync();
        var file = Files(await UploadAsync(_hazel, id, "Firmware 2.1", StoreFileAudience.Private)).Single();

        var saved = Files(await Admin().SaveFile(id, file.Id,
            new SaveStoreProductFileRequest("Firmware 2.1", StoreProductFileKind.Firmware, StoreFileAudience.Buyers, "2.1", 0), default)).Single();
        Assert.Equal(StoreFileAudience.Buyers, saved.Audience);

        Guid uploadId;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var change = await db.StoreProductChanges.Where(c => c.Area == StoreProductChangeArea.Files).OrderByDescending(c => c.OccurredUtc).FirstAsync();
            Assert.Equal(("Gave buyers “Firmware 2.1”.", StoreChangeActor.Store), (change.Summary, change.ActorRole));
            uploadId = (await db.StoreProductFiles.SingleAsync()).UploadFileId!.Value;
        }

        Assert.Empty(Files(await Seller(_hazel).DeleteFile(id, file.Id, default)));
        await using (var db = await _sqlite.NewContextAsync())
            Assert.False(await db.UploadFiles.AnyAsync(u => u.Id == uploadId));
    }

    // ── the keepers' door ───────────────────────────────────────────────────

    [Fact]
    public async Task The_seller_and_the_store_download_a_private_file_and_nobody_else_can()
    {
        var id = await DraftAsync();
        var file = Files(await UploadAsync(_hazel, id, "Build notes", StoreFileAudience.Private, StoreProductFileKind.Document)).Single();
        var manual = Files(await ManualAsync(id, StoreFileAudience.Private)).Single(f => f.IsManual);

        foreach (var keeper in new[] { _hazel, _admin })
        {
            var stream = Assert.IsType<FileStreamResult>(await Keepers(keeper).Download(file.Id, default));
            Assert.Equal("rempod-2.1.bin", stream.FileDownloadName);
            using var read = new MemoryStream();
            await stream.FileStream.CopyToAsync(read);
            Assert.Equal(Firmware, read.ToArray());
            Assert.Contains("<strong>power</strong>", Ok(await Keepers(keeper).Manual(manual.Id, default)).Html);
        }

        foreach (var stranger in new[] { _ivan, _buyer })
        {
            Assert.IsType<NotFoundResult>(await Keepers(stranger).Download(file.Id, default));
            Assert.IsType<NotFoundResult>((await Keepers(stranger).Manual(manual.Id, default)).Result);
        }
    }

    // ── the buyer's downloads ───────────────────────────────────────────────

    /// <summary>A paid order for the REM Pod, bought by <see cref="_buyer"/>, with one file for buyers, one private and a manual for buyers.</summary>
    private async Task<(StoreOrder Order, Guid Buyers, Guid Private, Guid Manual, Guid Item)> BoughtAsync(StoreOrderStatus status = StoreOrderStatus.Paid, bool paid = true)
    {
        var id = await DraftAsync();
        var buyers = Files(await UploadAsync(_hazel, id, "Firmware 2.1", StoreFileAudience.Buyers)).Single().Id;
        var hidden = Files(await UploadAsync(_hazel, id, "Build notes", StoreFileAudience.Private, StoreProductFileKind.Document)).Single(f => f.Title == "Build notes").Id;
        var manual = Files(await ManualAsync(id, StoreFileAudience.Buyers)).Single(f => f.IsManual).Id;

        await using var db = await _sqlite.NewContextAsync();
        var variant = await db.StoreProductVariants.FirstAsync(v => v.ProductId == id);
        var order = StoreTestData.Order(db, status, await db.AppUsers.SingleAsync(u => u.Id == _buyer.Id));
        order.PaidUtc = paid ? DateTime.UtcNow.AddHours(-1) : null;
        var item = new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = id, VariantId = variant.Id, ProductName = "REM Pod", Sku = variant.Sku,
            UnitPrice = 30m, Quantity = 2, LineTotal = 60m, DateCreated = DateTime.UtcNow,
        };
        db.StoreOrderItems.Add(item);
        await db.SaveChangesAsync();
        return (order, buyers, hidden, manual, item.Id);
    }

    private async Task<List<StoreOrderDownloadRecord>> DownloadsAsync(StoreOrder order, Guid? signedIn = null, string? token = null)
    {
        var answer = (await Buyers(signedIn).Downloads(order.Id, token, default)).Result;
        return answer is OkObjectResult ok ? ((IEnumerable<StoreOrderDownloadRecord>)ok.Value!).ToList() : throw new InvalidOperationException(answer?.GetType().Name);
    }

    [Fact]
    public async Task A_paid_buyer_sees_the_files_for_buyers_and_never_a_private_one()
    {
        var (order, buyers, hidden, manual, _) = await BoughtAsync();

        var list = await DownloadsAsync(order, signedIn: _buyer.Id);
        Assert.Equal(new[] { buyers, manual }.Order(), list.Select(d => d.FileId).Order());
        Assert.DoesNotContain(list, d => d.FileId == hidden);
        Assert.Equal(list, await DownloadsAsync(order, token: order.AccessToken));   // a guest's link works the same

        var stream = Assert.IsType<FileStreamResult>(await Buyers(_buyer.Id).Download(order.Id, buyers, null, default));
        Assert.Equal("rempod-2.1.bin", stream.FileDownloadName);
        await stream.FileStream.DisposeAsync();
        Assert.Contains("<h1", Ok(await Buyers().Manual(order.Id, manual, order.AccessToken, default)).Html);

        Assert.IsType<NotFoundResult>(await Buyers(_buyer.Id).Download(order.Id, hidden, null, default));
        Assert.IsType<NotFoundResult>(await Buyers(_buyer.Id).Download(order.Id, manual, null, default));   // a manual is read, not streamed
        Assert.IsType<NotFoundResult>((await Buyers(_buyer.Id).Manual(order.Id, buyers, null, default)).Result);   // and a file isn't a manual
    }

    [Fact]
    public async Task A_stranger_gets_404_from_every_download_door()
    {
        var (order, buyers, _, manual, _) = await BoughtAsync();

        foreach (var door in new[] { Buyers(), Buyers(_ivan.Id) })
        {
            Assert.IsType<NotFoundResult>((await door.Downloads(order.Id, "wrong", default)).Result);
            Assert.IsType<NotFoundResult>(await door.Download(order.Id, buyers, null, default));
            Assert.IsType<NotFoundResult>((await door.Manual(order.Id, manual, null, default)).Result);
        }
    }

    [Fact]
    public async Task Nothing_downloads_before_payment_or_after_a_cancellation()
    {
        var (unpaid, buyers, _, _, _) = await BoughtAsync(StoreOrderStatus.PendingPayment, paid: false);
        Assert.Empty(await DownloadsAsync(unpaid, signedIn: _buyer.Id));
        Assert.IsType<NotFoundResult>(await Buyers(_buyer.Id).Download(unpaid.Id, buyers, null, default));

        var (cancelled, cancelledFile, _, _, _) = await BoughtAsync(StoreOrderStatus.Cancelled);
        Assert.Empty(await DownloadsAsync(cancelled, signedIn: _buyer.Id));
        Assert.IsType<NotFoundResult>(await Buyers(_buyer.Id).Download(cancelled.Id, cancelledFile, null, default));
    }

    [Fact]
    public async Task A_wholly_refunded_line_takes_its_downloads_with_it_but_a_partial_refund_does_not()
    {
        var (order, buyers, _, _, item) = await BoughtAsync();

        await using (var db = await _sqlite.NewContextAsync())
        {
            (await db.StoreOrderItems.SingleAsync(i => i.Id == item)).QuantityRefunded = 1;
            await db.SaveChangesAsync();
        }
        Assert.Contains(await DownloadsAsync(order, signedIn: _buyer.Id), d => d.FileId == buyers);

        await using (var db = await _sqlite.NewContextAsync())
        {
            (await db.StoreOrderItems.SingleAsync(i => i.Id == item)).QuantityRefunded = 2;
            await db.SaveChangesAsync();
        }
        Assert.Empty(await DownloadsAsync(order, signedIn: _buyer.Id));
        Assert.IsType<NotFoundResult>(await Buyers(_buyer.Id).Download(order.Id, buyers, null, default));
    }

    [Fact]
    public async Task A_file_of_a_product_not_on_the_order_does_not_download_through_it()
    {
        var (order, _, _, _, _) = await BoughtAsync();
        var other = Ok(await Seller(_ivan).Create(new CreateSellerItemRequest("Spirit Box", _categoryId), default)).Item.Id;
        var elsewhere = Files(await UploadAsync(_ivan, other, "Spirit Box firmware", StoreFileAudience.Buyers)).Single().Id;

        Assert.IsType<NotFoundResult>(await Buyers(_buyer.Id).Download(order.Id, elsewhere, null, default));
    }
}
