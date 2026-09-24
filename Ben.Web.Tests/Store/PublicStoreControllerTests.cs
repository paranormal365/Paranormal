using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The public store answers from the live catalogue only, filters and sorts as the listing says,
/// and shows a hidden product to a SuperAdmin's preview and to nobody else (storefront S2.2).
/// </summary>
public sealed class PublicStoreControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private Guid _admin;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        StoreTestData.StoreImageType(db, admin);
        await db.SaveChangesAsync();
        _admin = admin.Id;
        await StoreDemoSeeder.SeedCoreAsync(db, _admin, default);
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private PublicStoreController Controller(string query = "", ClaimsPrincipal? user = null) => new(
        _sqlite.Factory, new StorePaymentSetup(false, true, true), Options.Create(new StripeOptions { SecretKey = "sk_test_x", PublishableKey = "pk_test_x" }))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { QueryString = new QueryString(query) },
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
            },
        },
    };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;

    private async Task<StoreListingResponse> ListAsync(string query = "") => Ok(await Controller(query).Products(default));

    private static ClaimsPrincipal As(string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "Bearer"));

    [Fact]
    public async Task A_hidden_product_is_nowhere_on_the_store()
    {
        var all = await ListAsync();
        Assert.Equal(6, all.Total);
        Assert.DoesNotContain(all.Products, p => p.Slug == "boo-buddy");

        var missing = await Controller().Product("boo-buddy", null, default);
        Assert.Equal(PublicStoreController.NoSuchProduct, Assert.IsType<NotFoundObjectResult>(missing.Result).Value);
        Assert.Empty(Ok(await Controller().Suggest("boo", default)));
    }

    [Fact]
    public async Task A_product_on_a_hidden_shelf_is_hidden_with_it()
    {
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreCategories.Where(c => c.Slug == "spirit-boxes").ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));

        Assert.DoesNotContain((await ListAsync()).Products, p => p.Slug == "p-sb7-spirit-box");
        Assert.IsType<NotFoundObjectResult>((await Controller().Category("spirit-boxes", default)).Result);
        Assert.IsType<NotFoundObjectResult>((await Controller().Product("p-sb7-spirit-box", null, default)).Result);
    }

    [Fact]
    public async Task Filters_narrow_the_listing_and_the_counts_describe_the_shelf()
    {
        Assert.Equal(["investigators-field-bag"], (await ListAsync("?opt=Colour:Olive")).Products.Select(p => p.Slug));
        Assert.Equal(["h1n-handy-recorder", "investigators-field-bag"],
            (await ListAsync("?opt=Colour:Olive&opt=Colour:Grey")).Products.Select(p => p.Slug).Order());
        Assert.Empty((await ListAsync("?opt=Colour:Olive&opt=Size:Tiny")).Products);

        var band = await ListAsync("?price=50-100");
        Assert.Equal(["h1n-handy-recorder", "k-ii-emf-meter", "p-sb7-spirit-box"], band.Products.Select(p => p.Slug).Order());

        var colour = band.Facets.Options.Single(o => o.Name == "Colour");
        Assert.Equal(3, colour.Values.Single(v => v.Label == "Black").Count);
        Assert.Equal("#556b2f", colour.Values.Single(v => v.Label == "Olive").SwatchHex);
        Assert.Contains(band.Facets.PriceBands, b => b.Key == "under-25" && b.Count == 1);
    }

    [Fact]
    public async Task Sorting_and_searching()
    {
        Assert.Equal("single-unit-probe", (await ListAsync("?sort=price-asc")).Products[0].Slug);
        Assert.Equal("rem-pod", (await ListAsync("?sort=price-desc")).Products[0].Slug);
        Assert.Equal(["p-sb7-spirit-box"], (await ListAsync("?q=spirit")).Products.Select(p => p.Slug));
        Assert.Equal(["h1n-handy-recorder"], (await ListAsync("?q=h1n-gr")).Products.Select(p => p.Slug));

        var suggestions = Ok(await Controller().Suggest("re", default)).Select(s => s.Slug).ToList();
        Assert.Equal("rem-pod", suggestions[0]);
        Assert.Empty(Ok(await Controller().Suggest("r", default)));
    }

    [Fact]
    public async Task The_listing_pages_by_sixteen()
    {
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin);
            var shelf = await db.StoreCategories.SingleAsync(c => c.Slug == "field-accessories");
            for (var i = 0; i < 12; i++) StoreTestData.Product(db, admin, shelf);
            await db.SaveChangesAsync();
        }

        var first = await ListAsync();
        var second = await ListAsync("?page=2");
        var beyond = await ListAsync("?page=9");

        Assert.Equal((18, 16, 2), (first.Total, first.Products.Count, second.Products.Count));
        Assert.Empty(first.Products.Select(p => p.Id).Intersect(second.Products.Select(p => p.Id)));
        Assert.Equal(2, beyond.Page);
    }

    [Fact]
    public async Task Cards_say_what_is_left_and_what_is_sold_out()
    {
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(v => v.Sku == "SINGLE-UNIT-PROBE").ExecuteUpdateAsync(s => s.SetProperty(v => v.StockOnHand, 0));

        var cards = (await ListAsync()).Products.ToDictionary(p => p.Slug);

        Assert.Equal(((int?)2, true), (cards["rem-pod"].StockLeft, cards["rem-pod"].InStock));
        Assert.Null(cards["k-ii-emf-meter"].StockLeft);
        Assert.Equal((false, (int?)null), (cards["single-unit-probe"].InStock, cards["single-unit-probe"].StockLeft));
        Assert.Equal((69.99m, (int?)14), (cards["k-ii-emf-meter"].CompareAtPrice, cards["k-ii-emf-meter"].DiscountPercent));
        Assert.True(cards["p-sb7-spirit-box"].InStock);
        Assert.Null(cards["p-sb7-spirit-box"].SingleVariantId);
        Assert.NotNull(cards["k-ii-emf-meter"].SingleVariantId);
        Assert.True(cards["rem-pod"].IsNew);
    }

    [Fact]
    public async Task A_sold_out_choice_stays_but_a_retired_one_goes()
    {
        var detail = Ok(await Controller().Product("p-sb7-spirit-box", null, default));
        var camo = detail.Options.Single().Values.Single(v => v.Value == "Camo");
        Assert.Equal(0, detail.Variants.Single(v => v.OptionValueIds.Contains(camo.Id)).Available);

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(v => v.Sku == "PSB7-CAMO").ExecuteUpdateAsync(s => s.SetProperty(v => v.IsActive, false));

        var after = Ok(await Controller().Product("p-sb7-spirit-box", null, default));
        Assert.Equal(["Black"], after.Options.Single().Values.Select(v => v.Value));
        Assert.Single(after.Variants);
    }

    [Fact]
    public async Task Only_a_superadmin_can_preview_a_hidden_product()
    {
        Assert.IsType<NotFoundObjectResult>((await Controller(user: As(RoleNames.Moderator)).Product("boo-buddy", 1, default)).Result);
        Assert.IsType<NotFoundObjectResult>((await Controller().Product("boo-buddy", 1, default)).Result);

        var preview = Ok(await Controller(user: As(RoleNames.SuperAdmin)).Product("boo-buddy", 1, default));
        Assert.True(preview.IsPreview);
        Assert.False(Ok(await Controller(user: As(RoleNames.SuperAdmin)).Product("k-ii-emf-meter", 1, default)).IsPreview);
    }

    [Fact]
    public async Task A_product_page_carries_its_specs_equipment_and_neighbours()
    {
        var kii = Ok(await Controller().Product("k-ii-emf-meter", null, default));
        Assert.Equal("Detection", kii.Specs[0].GroupName);
        Assert.Single(kii.Images);
        Assert.Equal((3, 30), (kii.LowStockThreshold, kii.ReturnsWindowDays));

        var bag = Ok(await Controller().Product("investigators-field-bag", null, default));
        Assert.Equal(["Colour", "Size"], bag.Options.Select(o => o.Name));
        Assert.Contains(bag.Related, r => r.Slug == "single-unit-probe");
        Assert.DoesNotContain(bag.Related, r => r.Slug == "investigators-field-bag");
    }

    [Fact]
    public async Task A_look_at_a_live_product_is_counted_and_nothing_else_is()
    {
        Guid kii, boo;
        await using (var db = await _sqlite.NewContextAsync())
        {
            kii = (await db.StoreProducts.SingleAsync(p => p.Slug == "k-ii-emf-meter")).Id;
            boo = (await db.StoreProducts.SingleAsync(p => p.Slug == "boo-buddy")).Id;
        }
        Assert.IsType<NoContentResult>(await Controller().Viewed(kii, default));
        Assert.IsType<NoContentResult>(await Controller().Viewed(boo, default));
        Assert.IsType<NoContentResult>(await Controller().Viewed(Guid.NewGuid(), default));

        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal((1, 0), ((await check.StoreProducts.SingleAsync(p => p.Id == kii)).ViewCount, (await check.StoreProducts.SingleAsync(p => p.Id == boo)).ViewCount));
    }

    [Fact]
    public async Task The_home_page_has_slides_shelves_and_rails()
    {
        var home = Ok(await Controller().Home(default));
        Assert.Equal(5, home.Categories.Count);
        Assert.Equal(5, home.Slides.Count);
        Assert.Equal(["k-ii-emf-meter"], home.Featured.Select(p => p.Slug));
        Assert.Equal("rem-pod", home.NewArrivals[0].Slug);
        Assert.Equal(6, home.Popular.Count);
    }

    [Fact]
    public async Task Info_says_what_a_buyer_needs_and_nothing_secret()
    {
        var info = Ok(await Controller().Info(default));
        Assert.Equal((true, 0m, (decimal?)null, 30, true, false, "pk_test_x"),
            (info.CheckoutEnabled, info.ShippingFlatRate, info.FreeShippingOver, info.ReturnsWindowDays, info.PaymentsEnabled, info.FakeCheckout, info.PublishableKey));
        Assert.DoesNotContain("sk_", System.Text.Json.JsonSerializer.Serialize(info));
    }
}
