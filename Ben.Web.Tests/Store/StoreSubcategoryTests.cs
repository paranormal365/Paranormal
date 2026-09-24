using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Subcategories (Ben, 09/24): one level deep; a shelf reaches shoppers only when something is on
/// sale under it; hiding a parent hides everything under it; only SuperAdmins arrange them.
/// </summary>
public sealed class StoreSubcategoryTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "store-sub-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        StoreTestData.StoreImageType(db, _admin);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private PublicStoreController Store() => new(
        _sqlite.Factory, new StorePaymentSetup(false, true, true), Options.Create(new StripeOptions { SecretKey = "sk_test_x", PublishableKey = "pk_test_x" }))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private AdminStoreCategoryController Admin() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object,
        new StoreImageStorage(TestMedia.StorageOnDisk(_storageRoot), new MediaSanitizationService(), TestMedia.IngestToDisk(_storageRoot)))
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;
    private static string Refusal<T>(ActionResult<T> r) => (string)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;

    /// <summary>Meters (top) › Data loggers (sub, one product); Radios (top, empty); Loggers' empty sibling.</summary>
    private async Task<(StoreCategory Meters, StoreCategory Loggers, StoreCategory EmptySub, StoreCategory Radios, StoreProduct Logger)> ShelvesAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var meters = StoreTestData.Category(db, admin, "Meters", sortOrder: 0);
        var radios = StoreTestData.Category(db, admin, "Radios", sortOrder: 1);
        var loggers = StoreTestData.Category(db, admin, "Data loggers");
        loggers.ParentCategoryId = meters.Id;
        var emptySub = StoreTestData.Category(db, admin, "Thermal");
        emptySub.ParentCategoryId = meters.Id;
        var logger = StoreTestData.Product(db, admin, loggers);
        await db.SaveChangesAsync();
        return (meters, loggers, emptySub, radios, logger);
    }

    // ── What shoppers see ────────────────────────────────────────────────────

    [Fact]
    public async Task A_parent_shows_when_only_its_subcategory_has_something_on_sale()
    {
        var s = await ShelvesAsync();
        var cards = Ok(await Store().Categories(default)).ToList();

        Assert.Equal(["Meters", "Data loggers"], cards.Select(c => c.Name));
        Assert.Equal((1, (Guid?)null), (cards[0].ProductCount, cards[0].ParentId));
        Assert.Equal(s.Meters.Id, cards[1].ParentId);
    }

    [Fact]
    public async Task The_front_page_tiles_are_top_level_only()
    {
        await ShelvesAsync();
        var home = Ok(await Store().Home(default));
        Assert.Equal(["Meters"], home.Categories.Select(c => c.Name));
    }

    [Fact]
    public async Task A_parent_shelf_lists_its_subcategories_products()
    {
        var s = await ShelvesAsync();
        var listing = Ok(await Store().Category(s.Meters.Slug, default));
        Assert.Equal([s.Logger.Name], listing.Products.Select(p => p.Name));
        Assert.Equal(s.Meters.Id, listing.Category!.Id);
    }

    [Fact]
    public async Task An_empty_shelf_answers_like_a_missing_one()
    {
        var s = await ShelvesAsync();
        Assert.IsType<NotFoundObjectResult>((await Store().Category(s.Radios.Slug, default)).Result);
        Assert.IsType<NotFoundObjectResult>((await Store().Category(s.EmptySub.Slug, default)).Result);
        Assert.IsType<NotFoundObjectResult>((await Store().Category("no-such-shelf", default)).Result);
    }

    [Fact]
    public async Task Hiding_a_parent_takes_its_subcategories_products_off_the_store()
    {
        var s = await ShelvesAsync();
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreCategories.Where(c => c.Id == s.Meters.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.IsActive, false));

        await using var read = await _sqlite.NewContextAsync();
        Assert.False(await StoreCatalogue.LiveProducts(read).AnyAsync(p => p.Id == s.Logger.Id));
        Assert.Empty(Ok(await Store().Categories(default)));
        Assert.IsType<NotFoundObjectResult>((await Store().Category(s.Loggers.Slug, default)).Result);
    }

    [Fact]
    public async Task The_cart_refuses_a_product_whose_parent_is_hidden()
    {
        var s = await ShelvesAsync();
        await using var db = await _sqlite.NewContextAsync();
        var variant = await db.StoreProductVariants.SingleAsync(v => v.ProductId == s.Logger.Id);
        await db.StoreCategories.Where(c => c.Id == s.Meters.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.IsActive, false));

        var caller = StoreCartCaller.From(_admin.Id, null);
        var outcome = await new StoreCartService(db).AddAsync(caller, variant.Id, 1);

        Assert.Equal(StoreCartOutcome.NotFound, outcome.Outcome);
    }

    // ── Arranging them (SuperAdmin) ──────────────────────────────────────────

    [Fact]
    public async Task Subcategories_go_one_level_deep()
    {
        var s = await ShelvesAsync();

        var underASub = await Admin().Create(new SaveStoreCategoryRequest("Loggers mini", null, null, true, false, s.Loggers.Id), default);
        var parentBecomesSub = await Admin().Update(s.Meters.Id, new SaveStoreCategoryRequest("Meters", s.Meters.Slug, null, true, false, s.Radios.Id), default);
        var underItself = await Admin().Update(s.Radios.Id, new SaveStoreCategoryRequest("Radios", s.Radios.Slug, null, true, false, s.Radios.Id), default);

        Assert.Contains("one level deep", Refusal(underASub));
        Assert.Contains("has subcategories of its own", Refusal(parentBecomesSub));
        Assert.Contains("can't sit under itself", Refusal(underItself));
    }

    [Fact]
    public async Task A_category_can_move_under_a_top_level_one_and_back()
    {
        var s = await ShelvesAsync();
        var moved = Ok(await Admin().Update(s.Radios.Id, new SaveStoreCategoryRequest("Radios", s.Radios.Slug, null, true, false, s.Meters.Id), default));
        Assert.Equal((s.Meters.Id, "Meters"), (moved.Category.ParentCategoryId, moved.Category.ParentName));

        var back = Ok(await Admin().Update(s.Radios.Id, new SaveStoreCategoryRequest("Radios", s.Radios.Slug, null, true, false), default));
        Assert.Null(back.Category.ParentCategoryId);
    }

    [Fact]
    public async Task A_parent_with_subcategories_cannot_be_deleted()
    {
        var s = await ShelvesAsync();
        var refused = await Admin().Delete(s.Meters.Id, default);
        Assert.Contains("2 subcategories", (string)Assert.IsType<BadRequestObjectResult>(refused).Value!);
    }

    [Fact]
    public async Task Hiding_a_parent_counts_its_subcategories_products_before_and_after()
    {
        var s = await ShelvesAsync();
        var list = Ok(await Admin().GetAll(default)).ToList();
        Assert.Equal(1, list.Single(c => c.Id == s.Meters.Id).LiveProductCount);

        var saved = Ok(await Admin().Update(s.Meters.Id, new SaveStoreCategoryRequest("Meters", s.Meters.Slug, null, IsActive: false, false), default));
        Assert.Equal(1, saved.HiddenProducts);
    }

    [Fact]
    public async Task The_admin_list_reads_as_a_tree()
    {
        await ShelvesAsync();
        var names = Ok(await Admin().GetAll(default)).Select(c => c.Name).ToList();
        Assert.Equal(["Meters", "Data loggers", "Thermal", "Radios"], names);
    }
}
