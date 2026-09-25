using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// An item's history (store sellers, backlog 251, P2): each change leaves one line in words, a
/// refused or empty save leaves none, and a seller reads their own item's history without the
/// store's prices or its staff's names.
/// </summary>
public sealed class StoreProductHistoryTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!;
    private Guid _categoryId;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "store-history-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _admin.DisplayName = "Ada Admin";
        _hazel = StoreTestData.Person(db, "hazel");
        _hazel.DisplayName = "Hazel Marsh";
        _ivan = StoreTestData.Person(db, "ivan");
        StoreTestData.StoreImageType(db, _admin);
        _categoryId = StoreTestData.Category(db, _admin, "Trigger Objects").Id;
        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Seller, NormalizedName = "SELLER" };
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _hazel.Id, RoleId = role.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _ivan.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private AdminStoreProductController Admin() => new(
        _sqlite.Factory, new Mock<IAuditLogService>().Object,
        new StoreImageStorage(TestMedia.StorageOnDisk(_storageRoot), new MediaSanitizationService(), TestMedia.IngestToDisk(_storageRoot)),
        new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private SellerStoreProductController Seller(AppUser who) => new(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(who.Id) };

    private static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private async Task<StoreProductAdminRecord> NewAsync(string name = "Spirit Box")
        => Ok(await Admin().Create(new CreateStoreProductRequest(name, _categoryId), default));

    private SaveStoreProductRequest Save(StoreProductAdminRecord p, string? name = null, string? description = null, Guid? seller = null)
        => new(_categoryId, null, name ?? p.Name, null, p.ShortDescription, description ?? p.LongDescriptionHtml,
            false, null, null, p.SortOrder, p.Specs, seller ?? p.SellerAppUserId);

    private async Task<List<StoreProductChangeRecord>> HistoryAsync(Guid productId)
        => Ok(await Admin().History(productId, null, null, default)).ToList();

    private async Task<StoreProductAdminRecord> PricedAsync(StoreProductAdminRecord p, decimal price = 49.99m)
    {
        var v = p.Variants.Single();
        return Ok(await Admin().UpdateVariant(p.Id, v.Id, new SaveStoreVariantRequest(v.Sku, price, null, true, true, 0, []), default));
    }

    [Fact]
    public async Task Making_an_item_opens_its_history_with_who_made_it()
    {
        var p = await NewAsync();

        var line = Assert.Single(await HistoryAsync(p.Id));
        Assert.Equal((StoreProductChangeArea.Created, "Created it.", "Ada Admin", StoreChangeActor.Store),
            (line.Area, line.Summary, line.ActorName, line.ActorRole));
    }

    [Fact]
    public async Task A_details_save_says_what_changed_and_one_that_changed_nothing_says_nothing()
    {
        var p = await NewAsync();
        p = Ok(await Admin().Update(p.Id, Save(p, name: "Ovilus Spirit Box", description: "<p>Talks back.</p>"), default));

        var history = await HistoryAsync(p.Id);
        Assert.Equal("Renamed it from “Spirit Box” to “Ovilus Spirit Box”. Changed the description.",
            history.Single(h => h.Area == StoreProductChangeArea.Details).Summary);

        Ok(await Admin().Update(p.Id, Save(p), default));
        Assert.Equal(history.Count, (await HistoryAsync(p.Id)).Count);
    }

    [Fact]
    public async Task A_refused_save_leaves_no_line()
    {
        var p = await NewAsync();
        Assert.IsType<BadRequestObjectResult>((await Admin().Update(p.Id, Save(p, name: "  "), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Admin().Activate(p.Id, default)).Result);   // unpriced

        Assert.Single(await HistoryAsync(p.Id));
    }

    [Fact]
    public async Task Going_on_sale_and_off_is_recorded()
    {
        var p = await PricedAsync(await NewAsync());
        Ok(await Admin().AddImage(p.Id, StoreTestData.Upload(StoreTestData.Jpeg()), "front", null, default));
        Ok(await Admin().Activate(p.Id, default));
        Ok(await Admin().Deactivate(p.Id, default));

        var said = (await HistoryAsync(p.Id)).Select(h => h.Summary).ToList();
        Assert.Equal(["Took it off sale.", "Put it on sale.", "Added a picture."], said.Take(3));
    }

    [Fact]
    public async Task A_price_change_is_its_own_line_and_the_seller_never_sees_it()
    {
        var p = await PricedAsync(await NewAsync());
        Ok(await Admin().Update(p.Id, Save(p, seller: _hazel.Id), default));

        var admin = await HistoryAsync(p.Id);
        Assert.Equal("Changed Default’s price from $0.00 to $49.99.", admin.Single(h => h.Area == StoreProductChangeArea.Price).Summary);
        Assert.Equal("Gave it to Hazel Marsh to sell.", admin.Single(h => h.Area == StoreProductChangeArea.Seller).Summary);

        var hers = Ok(await Seller(_hazel).History(p.Id, null, null, default)).ToList();
        Assert.DoesNotContain(hers, h => h.Area == StoreProductChangeArea.Price);
        Assert.Equal(admin.Count - 1, hers.Count);
        Assert.All(hers, h => Assert.Equal(StoreProductHistory.StoreActorName, h.ActorName));   // never "Ada Admin"
    }

    [Fact]
    public async Task Another_sellers_item_has_no_history_to_read()
    {
        var p = await NewAsync();
        Ok(await Admin().Update(p.Id, Save(p, seller: _hazel.Id), default));

        Assert.IsType<NotFoundResult>((await Seller(_ivan).History(p.Id, null, null, default)).Result);
        var site = await NewAsync("Site stock");
        Assert.IsType<NotFoundResult>((await Seller(_hazel).History(site.Id, null, null, default)).Result);
    }

    [Fact]
    public async Task A_stock_change_and_its_line_go_together_or_not_at_all()
    {
        var p = await NewAsync();
        var v = p.Variants.Single();

        Ok(await Admin().AdjustStock(p.Id, v.Id, new AdjustStockRequest(5, null, StoreStockReason.Received, "Delivery 88"), default));
        Assert.Equal("Received 5 of Default. Note: Delivery 88", (await HistoryAsync(p.Id)).First().Summary);

        var count = (await HistoryAsync(p.Id)).Count;
        Assert.IsType<BadRequestObjectResult>((await Admin().AdjustStock(p.Id, v.Id,
            new AdjustStockRequest(-10, null, StoreStockReason.Damaged, null), default)).Result);
        Assert.Equal(count, (await HistoryAsync(p.Id)).Count);
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(5, await db.StoreProductVariants.Where(x => x.Id == v.Id).Select(x => x.StockOnHand).SingleAsync());
    }

    [Fact]
    public async Task The_stock_page_leaves_one_line_per_item_it_touches()
    {
        var a = await NewAsync("One");
        var b = await NewAsync("Two");
        var stock = new AdminStoreStockController(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(_admin.Id) };

        Ok(await stock.Adjust(new BulkAdjustStockRequest(
            [new BulkStockLine(a.Variants.Single().Id, 3, null), new BulkStockLine(b.Variants.Single().Id, null, 7)],
            StoreStockReason.Received, null), default));

        Assert.Equal("Received 3 of Default.", (await HistoryAsync(a.Id)).First().Summary);
        Assert.Equal("Counted Default: 7 on the shelf (was 0).", (await HistoryAsync(b.Id)).First().Summary);
    }

    [Fact]
    public async Task Options_and_variants_say_what_they_became()
    {
        var p = await NewAsync();
        p = Ok(await Admin().SaveOptions(p.Id, new SaveStoreOptionsRequest(
            [new SaveStoreOptionRequest(null, "Colour", StoreOptionKind.Pill, [new(null, "Black", null, true), new(null, "Red", null, true)])]), default));
        Ok(await Admin().GenerateVariants(p.Id, default));

        var said = (await HistoryAsync(p.Id)).Select(h => h.Summary).ToList();
        Assert.Contains("Set the options to Colour (Black, Red).", said);
        Assert.Contains("Added 2 variants, one for each new combination of choices.", said);

        // The same options again: nothing to say.
        var again = Ok(await Admin().GetById(p.Id, default));
        Ok(await Admin().SaveOptions(p.Id, new SaveStoreOptionsRequest(again.Options.Select(o => new SaveStoreOptionRequest(o.Id, o.Name, o.Kind,
            o.Values.Select(v => new SaveStoreOptionValueRequest(v.Id, v.Value, v.SwatchHex, v.IsActive)).ToList())).ToList()), default));
        Assert.Equal(said.Count, (await HistoryAsync(p.Id)).Count);
    }

    // ── the sentences ────────────────────────────────────────────────────────

    [Fact]
    public void A_count_that_found_what_was_there_says_nothing()
    {
        Assert.Null(StoreProductHistory.DescribeStock("Default", null, 4, 4, StoreStockReason.Correction));
        Assert.Equal("Wrote off 2 of Red as damaged.", StoreProductHistory.DescribeStock("Red", -2, null, 9, StoreStockReason.Damaged));
        Assert.Equal("Took 1 off Red (a correction).", StoreProductHistory.DescribeStock("Red", -1, null, 9, StoreStockReason.Correction));
    }

    [Fact]
    public void A_seller_change_is_its_own_area_and_names_nobody_by_email()
    {
        var before = new StoreProductHistory.DetailsSnapshot("Pod", "pod", Guid.Empty, null, null, null, false, null, null, 0, "", _hazelId);
        var after = before with { SellerAppUserId = null };

        var said = StoreProductHistory.DescribeDetails(before, after, new Dictionary<Guid, string> { [_hazelId] = "Hazel Marsh" });

        var (area, sentence) = Assert.Single(said);
        Assert.Equal(StoreProductChangeArea.Seller, area);
        Assert.Equal("Took it back from Hazel Marsh; the store sells it now.", sentence);
    }

    private static readonly Guid _hazelId = Guid.NewGuid();
}
