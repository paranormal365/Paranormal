using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Versions (store sellers, backlog 251, P13): "A new version links back to the old one and the old one
/// links forward. The seller chooses what happens to the old one: sell out the remaining stock, keep
/// offering it, or discontinue it." — applied once, on the new version's first day on sale.
/// </summary>
public sealed class StoreProductVersionTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!;
    private Guid _hers, _ours, _draft;
    private string _root = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _root = Path.Combine(Path.GetTempPath(), "store-versions-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _ivan = StoreTestData.Person(db, "ivan");
        StoreTestData.StoreImageType(db, _admin);
        StoreTestData.StoreProductFileType(db, _admin);
        var shelf = StoreTestData.Category(db, _admin, "Trigger Objects");
        var hers = StoreTestData.Product(db, _admin, shelf);
        (hers.Name, hers.SellerAppUserId, hers.FirstOnSaleUtc) = ("Hand-Built REM Pod", _hazel.Id, StoreTestData.Now);
        _hers = hers.Id;
        var ours = StoreTestData.Product(db, _admin, shelf);
        (ours.Name, ours.FirstOnSaleUtc) = ("K-II Meter", StoreTestData.Now);
        _ours = ours.Id;
        var draft = StoreTestData.Product(db, _admin, shelf, active: false);
        draft.SellerAppUserId = _hazel.Id;
        _draft = draft.Id;
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

    private PublicStoreController Page() => new(_sqlite.Factory, null!, Options.Create(new Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeOptions()))
    {
        ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
    };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;
    private static string Said<T>(ActionResult<T> r) => (string)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;

    private async Task<StoreProduct> ReadAsync(Guid id)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreProducts.AsNoTracking().Include(p => p.Variants).SingleAsync(p => p.Id == id);
    }

    private async Task<StoreProductDetail> PublicAsync(Guid id) => Ok(await Page().Product((await ReadAsync(id)).Slug, null, default));

    private async Task<List<string>> HistoryAsync(Guid id, StoreProductChangeArea area)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreProductChanges.Where(c => c.ProductId == id && c.Area == area).OrderBy(c => c.OccurredUtc).Select(c => c.Summary).ToListAsync();
    }

    /// <summary>The store's own item with a picture, a new version of it started, priced — ready to go on sale.</summary>
    private async Task<Guid> NewVersionOfOursAsync(StoreSupersededPolicy policy)
    {
        Ok(await Admin().AddImage(_ours, StoreTestData.Upload(StoreTestData.Jpeg()), "front", null, default));
        return Ok(await Admin().StartVersion(_ours, new StartStoreVersionRequest("v2", policy), default)).ProductId;
    }

    private async Task PutOnSaleAsync(Guid id) => Ok(await Admin().Activate(id, default));

    // ── starting one ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_seller_starts_a_new_version_that_copies_everything_but_the_stock()
    {
        await using (var db = await _sqlite.NewContextAsync())
        {
            db.StoreProductParts.Add(new StoreProductPart { Id = Guid.NewGuid(), ProductId = _hers, Name = "Antenna", Price = 12.5m, QuantityPerUnit = 1, DateCreated = DateTime.UtcNow });
            db.StoreProductFaqs.Add(new StoreProductFaq { Id = Guid.NewGuid(), ProductId = _hers, Question = "Batteries?", Answer = "Four AA.", DateCreated = DateTime.UtcNow });
            db.StoreProductFiles.Add(new StoreProductFile { Id = Guid.NewGuid(), ProductId = _hers, Title = "Quick start", Kind = StoreProductFileKind.Manual,
                Audience = StoreFileAudience.Buyers, ManualHtml = "<p>Hi</p>", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var newId = Ok(await Seller(_hazel).StartVersion(_hers, new StartStoreVersionRequest(" v2 ", StoreSupersededPolicy.SellOut), default)).ProductId;

        var (old, fresh) = (await ReadAsync(_hers), await ReadAsync(newId));
        Assert.Equal((false, _hazel.Id, _hers, "v2", StoreSupersededPolicy.SellOut, "Hand-Built REM Pod"),
            (fresh.IsActive, fresh.SellerAppUserId, fresh.PreviousVersionProductId, fresh.VersionLabel, fresh.SupersededPolicy, fresh.Name));
        Assert.NotEqual(old.Slug, fresh.Slug);
        var variant = Assert.Single(fresh.Variants);
        Assert.Equal((0, old.Variants.Single().Price), (variant.StockOnHand, variant.Price));
        Assert.EndsWith("-V2", variant.Sku);

        await using (var db = await _sqlite.NewContextAsync())
        {
            Assert.Equal(1, await db.StoreProductParts.CountAsync(x => x.ProductId == newId));
            Assert.Equal(1, await db.StoreProductFaqs.CountAsync(x => x.ProductId == newId));
            Assert.Equal(1, await db.StoreProductFiles.CountAsync(x => x.ProductId == newId));
        }
        Assert.Contains("Started a new version, v2, as a draft.", await HistoryAsync(_hers, StoreProductChangeArea.Versions));
        Assert.Single(await HistoryAsync(newId, StoreProductChangeArea.Created), s => s.StartsWith("Started version v2", StringComparison.Ordinal));

        var info = Ok(await Seller(_hazel).Version(_hers, default));
        Assert.Equal(newId, info.Next?.Id);
        Assert.Equal(_hers, Ok(await Seller(_hazel).Version(newId, default)).Previous?.Id);
    }

    [Fact]
    public async Task Starting_a_version_is_refused_in_words_when_it_should_be()
    {
        Assert.Contains("hasn't been on sale", Said(await Seller(_hazel).StartVersion(_draft, new StartStoreVersionRequest("v2", StoreSupersededPolicy.SellOut), default)));
        Assert.Contains("Name the new version", Said(await Seller(_hazel).StartVersion(_hers, new StartStoreVersionRequest("  ", StoreSupersededPolicy.SellOut), default)));
        Assert.IsType<NotFoundResult>((await Seller(_ivan).StartVersion(_hers, new StartStoreVersionRequest("v2", StoreSupersededPolicy.SellOut), default)).Result);

        Ok(await Seller(_hazel).StartVersion(_hers, new StartStoreVersionRequest("v2", StoreSupersededPolicy.SellOut), default));
        Assert.Contains("newer version already", Said(await Seller(_hazel).StartVersion(_hers, new StartStoreVersionRequest("v3", StoreSupersededPolicy.SellOut), default)));
        Assert.IsType<BadRequestObjectResult>(await Admin().Delete(_hers, default));   // a newer version links back to it
    }

    // ── what happens to the old one ─────────────────────────────────────────

    [Fact]
    public async Task Discontinue_takes_the_old_one_off_and_its_page_says_what_replaced_it()
    {
        var v2 = await NewVersionOfOursAsync(StoreSupersededPolicy.Discontinue);
        Assert.True((await ReadAsync(_ours)).IsActive);   // nothing happens while the new one is a draft

        await PutOnSaleAsync(v2);

        var old = await ReadAsync(_ours);
        Assert.False(old.IsActive);
        Assert.NotNull(old.DiscontinuedUtc);
        var oldPage = await PublicAsync(_ours);
        Assert.True(oldPage.Discontinued);
        Assert.False(oldPage.IsPreview);
        Assert.Equal(v2, oldPage.NewerVersion?.Id);
        Assert.False(oldPage.CanAsk);

        var newPage = await PublicAsync(v2);
        Assert.Equal((_ours, "v2"), (newPage.OlderVersion?.Id, newPage.VersionLabel));
        Assert.Contains("Version v2 went on sale, replacing this one — taken off sale.", await HistoryAsync(_ours, StoreProductChangeArea.Versions));
    }

    [Fact]
    public async Task Keep_offering_leaves_both_on_sale_each_linking_to_the_other()
    {
        var v2 = await NewVersionOfOursAsync(StoreSupersededPolicy.KeepOffering);
        await PutOnSaleAsync(v2);

        Assert.True((await ReadAsync(_ours)).IsActive);
        var oldPage = await PublicAsync(_ours);
        Assert.Equal((false, v2), (oldPage.Discontinued, oldPage.NewerVersion?.Id));
        Assert.Contains("Version v2 went on sale. This one stays on sale beside it.", await HistoryAsync(_ours, StoreProductChangeArea.Versions));
    }

    [Fact]
    public async Task Sell_out_keeps_the_old_one_until_its_stock_is_gone_then_the_sweep_takes_it_off()
    {
        var v2 = await NewVersionOfOursAsync(StoreSupersededPolicy.SellOut);
        await PutOnSaleAsync(v2);

        var old = await ReadAsync(_ours);
        Assert.True(old.IsActive);
        Assert.NotNull(old.SellingOutSinceUtc);

        await using (var db = await _sqlite.NewContextAsync())
            Assert.Equal(0, await StoreProductVersions.SweepSoldOutAsync(db, DateTime.UtcNow, default));   // five still on hand
        await using (var db = await _sqlite.NewContextAsync())
        {
            await db.StoreProductVariants.Where(v => v.ProductId == _ours).ExecuteUpdateAsync(u => u.SetProperty(v => v.StockOnHand, 0));
            Assert.Equal(1, await StoreProductVersions.SweepSoldOutAsync(db, DateTime.UtcNow, default));
        }

        old = await ReadAsync(_ours);
        Assert.Equal((false, true), (old.IsActive, old.DiscontinuedUtc is not null));
        Assert.True((await PublicAsync(_ours)).Discontinued);
        Assert.Contains("Sold out after a newer version replaced it — taken off sale.", await HistoryAsync(_ours, StoreProductChangeArea.Versions));
    }

    [Fact]
    public async Task Sell_out_with_nothing_left_takes_the_old_one_off_at_once()
    {
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(v => v.ProductId == _ours).ExecuteUpdateAsync(u => u.SetProperty(v => v.StockOnHand, 0));
        var v2 = await NewVersionOfOursAsync(StoreSupersededPolicy.SellOut);
        await PutOnSaleAsync(v2);
        Assert.False((await ReadAsync(_ours)).IsActive);
    }

    [Fact]
    public async Task The_policy_happens_once_and_is_fixed_after_it_has()
    {
        var v2 = await NewVersionOfOursAsync(StoreSupersededPolicy.KeepOffering);
        Ok(await Admin().SaveVersion(v2, new SaveStoreVersionRequest("v2", StoreSupersededPolicy.Discontinue), default));
        Assert.Contains("When this goes on sale, the old one comes off sale straight away.", await HistoryAsync(v2, StoreProductChangeArea.Versions));

        await PutOnSaleAsync(v2);
        Assert.False((await ReadAsync(_ours)).IsActive);

        // Put back on sale by hand: no longer "discontinued". The new one off and on again doesn't take it off twice.
        await PutOnSaleAsync(_ours);
        Assert.Null((await ReadAsync(_ours)).DiscontinuedUtc);
        Ok(await Admin().Deactivate(v2, default));
        await PutOnSaleAsync(v2);
        Assert.True((await ReadAsync(_ours)).IsActive);

        Assert.Contains("has gone on sale", Said(await Admin().SaveVersion(v2, new SaveStoreVersionRequest("v2", StoreSupersededPolicy.SellOut), default)));
        Ok(await Admin().SaveVersion(v2, new SaveStoreVersionRequest("2026 edition", StoreSupersededPolicy.Discontinue), default));   // the name can still change
        Assert.Equal("2026 edition", (await ReadAsync(v2)).VersionLabel);
    }
}
