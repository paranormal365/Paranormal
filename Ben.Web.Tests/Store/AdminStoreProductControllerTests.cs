using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The product back office: what may go on sale, what may be removed, how options make variants,
/// and how a stock change is refused rather than overdrawn (storefront S1.4).
/// </summary>
public sealed class AdminStoreProductControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private Guid _categoryId;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "store-prod-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        StoreTestData.StoreImageType(db, _admin);
        _categoryId = StoreTestData.Category(db, _admin, "EMF Meters").Id;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private AdminStoreProductController Controller() => new(
        _sqlite.Factory, new Mock<IAuditLogService>().Object,
        new StoreImageStorage(TestMedia.StorageOnDisk(_storageRoot), new MediaSanitizationService(), TestMedia.IngestToDisk(_storageRoot)),
        new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private static string Refusal<T>(ActionResult<T> result)
        => (string)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    private async Task<StoreProductAdminRecord> NewProductAsync(string name = "K-II EMF Meter")
        => Ok(await Controller().Create(new CreateStoreProductRequest(name, _categoryId), default));

    private async Task<StoreProductAdminRecord> PricedAsync(StoreProductAdminRecord p, decimal price = 59.99m)
    {
        var v = p.Variants.Single();
        return Ok(await Controller().UpdateVariant(p.Id, v.Id,
            new SaveStoreVariantRequest(v.Sku, price, null, true, true, 0, []), default));
    }

    private async Task<StoreProductAdminRecord> WithPictureAsync(StoreProductAdminRecord p)
        => Ok(await Controller().AddImage(p.Id, StoreTestData.Upload(StoreTestData.Jpeg()), "front", null, default));

    private async Task<StoreProductAdminRecord> LiveAsync(string name = "K-II EMF Meter")
    {
        var p = await WithPictureAsync(await PricedAsync(await NewProductAsync(name)));
        return Ok(await Controller().Activate(p.Id, default));
    }

    private async Task SoldAsync(Guid productId, Guid variantId)
    {
        await using var db = await _sqlite.NewContextAsync();
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        db.StoreOrderItems.Add(new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = productId, VariantId = variantId,
            ProductName = "K-II", Sku = "X", UnitPrice = 59.99m, Quantity = 1, LineTotal = 59.99m,
            DateCreated = StoreTestData.Now,
        });
        await db.SaveChangesAsync();
    }

    private static SaveStoreOptionsRequest ColourBySize() => new([
        new(null, "Colour", StoreOptionKind.Swatch, [new(null, "Black", "#000000", true), new(null, "Grey", "#808080", true)]),
        new(null, "Size", StoreOptionKind.Pill, [new(null, "Small", null, true), new(null, "Medium", null, true), new(null, "Large", null, true)]),
    ]);

    // ── going on sale ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_product_is_hidden_with_one_unpriced_variant()
    {
        var p = await NewProductAsync();

        Assert.False(p.IsActive);
        Assert.Equal("k-ii-emf-meter", p.Slug);
        var v = Assert.Single(p.Variants);
        Assert.Equal((0m, true, "K-II-EMF-METER", "Default"), (v.Price, v.IsDefault, v.Sku, v.Label));
    }

    [Fact]
    public async Task Going_on_sale_is_refused_until_each_thing_is_in_place()
    {
        var p = await NewProductAsync();
        Assert.Equal("Price the default variant first — it's still $0.00.", Refusal(await Controller().Activate(p.Id, default)));

        p = await PricedAsync(p);
        Assert.Equal("Add at least one picture before showing it.", Refusal(await Controller().Activate(p.Id, default)));

        p = await WithPictureAsync(p);
        var v = p.Variants.Single();
        await Controller().UpdateVariant(p.Id, v.Id, new SaveStoreVariantRequest(v.Sku, 59.99m, null, false, true, 0, []), default);
        Assert.Equal("At least one variant must be active.", Refusal(await Controller().Activate(p.Id, default)));

        await Controller().UpdateVariant(p.Id, v.Id, new SaveStoreVariantRequest(v.Sku, 59.99m, null, true, true, 0, []), default);
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreCategories.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        Assert.Equal("Its category is hidden — show the category first.", Refusal(await Controller().Activate(p.Id, default)));

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreCategories.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, true));
        Assert.True(Ok(await Controller().Activate(p.Id, default)).IsActive);
    }

    [Fact]
    public async Task A_live_product_keeps_a_live_variant_and_a_picture()
    {
        var p = await LiveAsync();
        var v = p.Variants.Single();

        Assert.Equal("The last live variant can't be deactivated while the product is live — take the product off sale first.",
            Refusal(await Controller().UpdateVariant(p.Id, v.Id, new SaveStoreVariantRequest(v.Sku, 59.99m, null, false, true, 0, []), default)));
        Assert.Equal("A live product keeps at least one picture. Add another first, or take it off sale.",
            Refusal(await Controller().DeleteImage(p.Id, p.Images.Single().Id, default)));
    }

    // ── removing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task What_has_been_sold_is_switched_off_not_deleted()
    {
        var p = await PricedAsync(await NewProductAsync());
        var second = await Controller().CreateVariant(p.Id,
            new SaveStoreVariantRequest("KII-SPARE", 49.99m, null, true, false, 1, []), default);
        Assert.Equal("A variant with Default already exists.", Refusal(second));

        await SoldAsync(p.Id, p.Variants.Single().Id);

        var refused = Assert.IsType<BadRequestObjectResult>(await Controller().Delete(p.Id, default));
        Assert.Equal("That product has been sold, so its record must stay. Deactivate it instead.", refused.Value);
        Assert.Equal("That variant has been ordered — deactivate it instead.",
            Refusal(await Controller().DeleteVariant(p.Id, p.Variants.Single().Id, default)));
    }

    [Fact]
    public async Task An_unsold_product_goes_with_its_pictures()
    {
        var p = await WithPictureAsync(await NewProductAsync());
        await Controller().SaveOptions(p.Id, ColourBySize(), default);
        await Controller().GenerateVariants(p.Id, default);

        Assert.IsType<NoContentResult>(await Controller().Delete(p.Id, default));

        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal((0, 0, 0, 0), (await db.StoreProducts.CountAsync(), await db.StoreProductVariants.CountAsync(),
            await db.StoreProductOptionValues.CountAsync(), await db.UploadFiles.CountAsync()));
    }

    [Fact]
    public async Task A_product_keeps_at_least_one_variant()
    {
        var p = await NewProductAsync();
        Assert.Equal("A product keeps at least one variant.", Refusal(await Controller().DeleteVariant(p.Id, p.Variants.Single().Id, default)));
    }

    // ── options and variants ─────────────────────────────────────────────────

    [Fact]
    public async Task Two_colours_by_three_sizes_make_six_variants_once()
    {
        var p = await PricedAsync(await NewProductAsync(), 39m);
        await Controller().SaveOptions(p.Id, ColourBySize(), default);

        var first = Ok(await Controller().GenerateVariants(p.Id, default));
        Assert.Equal(6, first.Added);
        Assert.Equal(6, first.Variants.Count);
        Assert.Contains(first.Variants, v => v.Label == "Black / Small");
        Assert.All(first.Variants, v => Assert.Equal(39m, v.Price));
        Assert.Single(first.Variants, v => v.IsDefault);

        var again = Ok(await Controller().GenerateVariants(p.Id, default));
        Assert.Equal(0, again.Added);
        Assert.Equal(6, again.Variants.Count);
    }

    [Fact]
    public async Task Option_rules_are_refused_in_words()
    {
        var p = await NewProductAsync();
        SaveStoreOptionRequest Pill(string name) => new(null, name, StoreOptionKind.Pill, [new(null, "A", null, true)]);

        Assert.Equal("Three options is the most a product can carry.",
            Refusal(await Controller().SaveOptions(p.Id, new([Pill("One"), Pill("Two"), Pill("Three"), Pill("Four")]), default)));
        Assert.Equal("A swatch needs a colour like #1a2b3c.",
            Refusal(await Controller().SaveOptions(p.Id, new([new(null, "Colour", StoreOptionKind.Swatch, [new(null, "Black", "black", true)])]), default)));

        var withOptions = Ok(await Controller().SaveOptions(p.Id, ColourBySize(), default));
        await Controller().GenerateVariants(p.Id, default);

        // Taking Grey away while variants are made of it is refused, naming one.
        var colour = withOptions.Options[0];
        var size = withOptions.Options[1];
        var withoutGrey = new SaveStoreOptionsRequest([
            new(colour.Id, "Colour", StoreOptionKind.Swatch, [new(colour.Values[0].Id, "Black", "#000000", true)]),
            new(size.Id, "Size", StoreOptionKind.Pill, size.Values.Select(v => new SaveStoreOptionValueRequest(v.Id, v.Value, null, true)).ToList()),
        ]);
        Assert.Matches("^Grey is still used by variant Grey / (Small|Medium|Large) — remove it from the variant first\\.$",
            Refusal(await Controller().SaveOptions(p.Id, withoutGrey, default)));
    }

    [Fact]
    public async Task Variant_rules_are_refused_in_words()
    {
        var other = await NewProductAsync("REM Pod");
        var p = await NewProductAsync();
        var withOptions = Ok(await Controller().SaveOptions(p.Id, ColourBySize(), default));
        var black = withOptions.Options[0].Values[0].Id;
        var small = withOptions.Options[1].Values[0].Id;

        var taken = await Controller().CreateVariant(p.Id, new SaveStoreVariantRequest("rem-pod", 10m, null, true, false, 1, [black, small]), default);
        Assert.IsType<ConflictObjectResult>(taken.Result);
        Assert.Equal("SKU REM-POD is already used by REM Pod — Default.", Refusal(taken));

        Assert.Equal("The old price has to be higher than the price.",
            Refusal(await Controller().CreateVariant(p.Id, new SaveStoreVariantRequest("A1", 10m, 10m, true, false, 1, [black, small]), default)));
        Assert.Equal("Pick one value for each option: Size.",
            Refusal(await Controller().CreateVariant(p.Id, new SaveStoreVariantRequest("A1", 10m, null, true, false, 1, [black]), default)));

        Ok(await Controller().CreateVariant(p.Id, new SaveStoreVariantRequest("A1", 10m, 12m, true, false, 1, [black, small]), default));
        Assert.Equal("A variant with Black / Small already exists.",
            Refusal(await Controller().CreateVariant(p.Id, new SaveStoreVariantRequest("A2", 10m, null, true, false, 1, [small, black]), default)));
        Assert.Equal(1, other.Variants.Count);
    }

    [Fact]
    public async Task Every_change_to_variants_moves_the_price_range()
    {
        var p = await PricedAsync(await NewProductAsync(), 39m);
        await Controller().SaveOptions(p.Id, ColourBySize(), default);
        var variants = Ok(await Controller().GenerateVariants(p.Id, default)).Variants;
        var large = variants.First(v => v.Label == "Grey / Large");
        await Controller().UpdateVariant(p.Id, large.Id,
            new SaveStoreVariantRequest(large.Sku, 49m, null, true, false, large.SortOrder, large.OptionValueIds), default);

        await using (var db = await _sqlite.NewContextAsync())
        {
            var product = await db.StoreProducts.SingleAsync(x => x.Id == p.Id);
            Assert.Equal((39m, 49m), (product.MinPrice, product.MaxPrice));
        }

        // A switched-off variant no longer stretches the range.
        await Controller().UpdateVariant(p.Id, large.Id,
            new SaveStoreVariantRequest(large.Sku, 49m, null, false, false, large.SortOrder, large.OptionValueIds), default);
        await using var check = await _sqlite.NewContextAsync();
        var after = await check.StoreProducts.SingleAsync(x => x.Id == p.Id);
        Assert.Equal((39m, 39m), (after.MinPrice, after.MaxPrice));
    }

    // ── stock ────────────────────────────────────────────────────────────────

    private async Task<(Guid ProductId, Guid VariantId)> StockedAsync(int onHand, int reserved = 0)
    {
        var p = await NewProductAsync();
        var v = p.Variants.Single().Id;
        await using var db = await _sqlite.NewContextAsync();
        await db.StoreProductVariants.Where(x => x.Id == v)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockOnHand, onHand).SetProperty(x => x.StockReserved, reserved));
        return (p.Id, v);
    }

    [Fact]
    public async Task Counting_the_shelf_records_the_difference()
    {
        var (p, v) = await StockedAsync(12);

        var after = Ok(await Controller().AdjustStock(p, v, new AdjustStockRequest(null, 5, StoreStockReason.Correction, "Stocktake"), default));

        Assert.Equal(5, after.Variants.Single().StockOnHand);
        var log = Ok(await Controller().StockLog(p, v, default)).Single();
        Assert.Equal((-7, 5, StoreStockReason.Correction, "Stocktake"), (log.Delta, log.QuantityAfter, log.Reason, log.Note));
    }

    [Fact]
    public async Task Stock_never_goes_below_zero_or_below_what_checkouts_hold()
    {
        var (p, v) = await StockedAsync(12);
        Assert.Equal("Nothing was changed — K-II-EMF-METER can't go below zero.",
            Refusal(await Controller().AdjustStock(p, v, new AdjustStockRequest(-20, null, StoreStockReason.Damaged, null), default)));

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.ExecuteUpdateAsync(s => s.SetProperty(x => x.StockReserved, 6));
        Assert.Equal("Nothing was changed — K-II-EMF-METER can't go below the 6 held by open checkouts.",
            Refusal(await Controller().AdjustStock(p, v, new AdjustStockRequest(-8, null, StoreStockReason.Damaged, null), default)));

        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal(12, (await check.StoreProductVariants.SingleAsync(x => x.Id == v)).StockOnHand);
        Assert.Equal(0, await check.StoreStockMovements.CountAsync());
    }

    [Fact]
    public async Task A_stock_request_says_what_is_wrong_with_it()
    {
        var (p, v) = await StockedAsync(12);
        Assert.Equal("Give either a change or a new quantity, not both.",
            Refusal(await Controller().AdjustStock(p, v, new AdjustStockRequest(1, 2, StoreStockReason.Received, null), default)));
        Assert.Equal("'Sold' is written by the system, not by hand.",
            Refusal(await Controller().AdjustStock(p, v, new AdjustStockRequest(-1, null, StoreStockReason.Sold, null), default)));
    }

    // ── pictures and copies ──────────────────────────────────────────────────

    [Fact]
    public async Task A_product_holds_twelve_pictures()
    {
        var p = await NewProductAsync();
        await using (var db = await _sqlite.NewContextAsync())
        {
            for (var i = 0; i < StoreCatalogRules.MaxImagesPerProduct; i++)
            {
                var file = new UploadFile
                {
                    Id = Guid.NewGuid(), UploadFileTypeId = Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.StoreImageFileTypeId,
                    FileName = "x.jpg", StoredFileName = "x.jpg", ContentType = "image/jpeg", IsPublic = true,
                    DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id,
                };
                db.UploadFiles.Add(file);
                db.StoreProductImages.Add(new StoreProductImage
                {
                    Id = Guid.NewGuid(), ProductId = p.Id, UploadFileId = file.Id, SortOrder = i,
                    DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id,
                });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal("This product already has 12 pictures.",
            Refusal(await Controller().AddImage(p.Id, StoreTestData.Upload(StoreTestData.Jpeg()), null, null, default)));
    }

    [Fact]
    public async Task A_copy_is_hidden_and_has_its_own_files_and_skus()
    {
        var p = await WithPictureAsync(await WithPictureAsync(await PricedAsync(await NewProductAsync())));
        await Controller().SaveOptions(p.Id, ColourBySize(), default);
        await Controller().GenerateVariants(p.Id, default);

        var copy = Ok(await Controller().Duplicate(p.Id, default));

        Assert.False(copy.IsActive);
        Assert.Equal("K-II EMF Meter (copy)", copy.Name);
        Assert.Equal(6, copy.Variants.Count);
        Assert.All(copy.Variants, v => Assert.EndsWith("-COPY", v.Sku));
        Assert.Contains(copy.Variants, v => v.Label == "Grey / Large");
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(4, await db.UploadFiles.CountAsync());
        Assert.Empty(copy.Images.Select(i => i.UploadFileId).Intersect(
            (await db.StoreProductImages.Where(i => i.ProductId == p.Id).Select(i => i.UploadFileId).ToListAsync())));
    }

    [Fact]
    public async Task A_description_is_sanitised_before_it_is_stored()
    {
        var p = await NewProductAsync();

        var saved = Ok(await Controller().Update(p.Id, new SaveStoreProductRequest(
            _categoryId, null, p.Name, null, "Short", "<p>Reads EMF</p><script>alert(1)</script>", false, null, null, 0,
            [new StoreSpecGroup("Detection", [new StoreSpecRecord("Range", "0–20 mG")])], null), default));

        Assert.Contains("Reads EMF", saved.LongDescriptionHtml);
        Assert.DoesNotContain("script", saved.LongDescriptionHtml);
        Assert.Equal("0–20 mG", saved.Specs.Single().Items.Single().Value);
    }
}
