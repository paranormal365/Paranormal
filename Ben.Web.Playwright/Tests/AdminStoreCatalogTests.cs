using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The store's back office, driven the way a SuperAdmin drives it (storefront S1.11): a product
/// from nothing to on sale, stock received across variants, the CSV, settings, codes, and the
/// warning before a category takes its products off the store.
/// </summary>
/// <remarks>
/// <para>Every test makes its own catalogue through the admin API with a name nobody else uses, so
/// nothing here reads or changes stock another fixture depends on. Settings and the store switch
/// are shared, so the tests that touch them put back exactly what they found.</para>
///
/// <para>All of this works with the shop switched OFF — the state the e2e database starts in — which
/// is the point: the catalogue is entered before anybody can see it.</para>
/// </remarks>
[TestFixture]
[Category("Store")]
public class AdminStoreCatalogTests : BenTestBase
{
    [SetUp]
    public async Task SignInAsync() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    private static string FixturePhoto => StoreTestApi.FixturePhoto;

    /// <summary>Types into a field, retrying until it holds, then leaves it so its change binding fires.</summary>
    private async Task SetAsync(string selector, string value)
    {
        await FillAndConfirmAsync(selector, value);
        await Page.Locator(selector).PressAsync("Tab");
    }

    // ── the tests ────────────────────────────────────────────────────────────

    [Test]
    [Description("Store is a group in the administration menu, with every store screen under it.")]
    public async Task The_store_group_is_in_the_administration_menu()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/dashboard");
        await WaitForTheCircuitAsync();

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "Administration", Exact = false }).First,
            Page.GetByRole(AriaRole.Link, new() { Name = "Store", Exact = true }).First);
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "Store", Exact = true }).First,
            Page.GetByRole(AriaRole.Link, new() { Name = "Store Settings", Exact = false }).First);

        foreach (var entry in new[] { "Store Dashboard", "Categories", "Products", "Stock", "Discount Codes", "Reviews", "Store Settings" })
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = entry, Exact = true }).First).ToBeVisibleAsync();
    }

    [Test]
    [Description("The editor's Preview draws the product page from the unsaved form, with the store switched off (Ben, 09/24/2026).")]
    public async Task The_preview_shows_unsaved_work_while_the_store_is_dark()
    {
        using var api = await StoreTestApi.OpenAsync();
        var categoryId = await api.CategoryAsync(Unique("Trigger Objects"));
        var product = await api.ProductAsync(categoryId, Unique("Cat Ball"), price: 24.5m);
        var id = product.GetProperty("id").GetGuid();

        var wasOn = await SetTheStoreAsync(on: false);
        try
        {
            // The products list's Preview opens the editor on its Preview tab — not the store's own
            // address, which is behind the store switch and answers "Page not found" while it is off.
            await Page.GotoAsync($"{BaseUrl}/admin/store/products");
            await WaitForTheCircuitAsync();
            var name = product.GetProperty("name").GetString()!;
            await FillAndConfirmAsync("#products-search", name);
            var row = Page.Locator("[data-testid=product-row]").Filter(new() { HasText = name });
            await Expect(row).ToHaveCountAsync(1, new() { Timeout = 15_000 });
            await ClickUntilUrlAsync(row.Locator("[data-testid=product-preview]"), "tab=preview");
            await Expect(Page.Locator("[data-testid=editor-preview-page]")).ToContainTextAsync("$24.50", new() { Timeout = 30_000 });
            await Expect(Page.Locator("[data-testid=editor-preview-page]")).ToContainTextAsync("Not on sale");

            // Typed on the Details tab and NOT saved: the preview draws it anyway.
            var typed = Unique("Cat Ball Mk II");
            await OpenTabAsync("Details", Page.Locator("#product-name"));
            await SetAsync("#product-name", typed);
            await SetAsync("#product-short", "Lights up when something touches it.");
            await ClickUntilAsync(Page.Locator("[data-testid=product-preview]").First, Page.Locator("[data-testid=editor-preview-page]"));
            await Expect(Page.Locator("[data-testid=editor-preview-page] h1")).ToContainTextAsync(typed);
            await Expect(Page.Locator("[data-testid=editor-preview-page]")).ToContainTextAsync("Lights up when something touches it.");
            await Expect(Page.Locator("[data-testid=editor-preview]")).ToContainTextAsync("unsaved changes");

            // Still unsaved: the product itself has its old name.
            var stored = await api.SendAsync(HttpMethod.Get, $"/api/admin/store/products/{id}");
            Assert.That(stored.GetProperty("name").GetString(), Is.EqualTo(product.GetProperty("name").GetString()));
        }
        finally { await PutTheStoreBackAsync(wasOn); }
    }

    [Test]
    [Description("A product goes from a name to on sale: picture, options, four variants, a price, stock, activate.")]
    public async Task A_product_goes_from_nothing_to_on_sale()
    {
        using var api = await StoreTestApi.OpenAsync();
        var categoryName = Unique("Spirit Boxes");
        await api.CategoryAsync(categoryName);
        var productName = Unique("Spirit Box");

        // The list's "New product" dialog, then straight into the editor.
        await Page.GotoAsync($"{BaseUrl}/admin/store/products");
        await WaitForTheCircuitAsync();
        await ClickUntilAsync(Page.Locator("#product-new"), Page.Locator("#new-product-name"));
        await SetAsync("#new-product-name", productName);
        await Page.Locator("#new-product-category").SelectOptionAsync(new SelectOptionValue { Label = categoryName });
        await ClickUntilUrlAsync(Page.Locator("#new-product-save"), "/edit");
        await Expect(Page.Locator("#product-status")).ToHaveTextAsync("Hidden", new() { Timeout = 30_000 });

        // Going on sale is refused, in words, until it is priced.
        await ClickUntilAsync(Page.Locator("#product-activate"), Page.Locator("#product-refusal"));
        await Expect(Page.Locator("#product-refusal")).ToContainTextAsync("Price the default variant first");

        // Price the default first — generated variants take their price from it.
        await OpenTabAsync("Options & variants", Page.Locator("#variants"));
        var priceBox = Page.Locator("#variants input[type=number]").First;
        await priceBox.FillAsync("39.99");
        await priceBox.PressAsync("Tab");
        await Page.Locator("[data-testid=variant-save]").First.ClickAsync();
        await Expect(Page.Locator("#variant-refusal")).ToHaveCountAsync(0);

        // Straight on, without waiting for that save to answer: what is typed next must survive it.

        // Two options, two values each.
        await ClickUntilAsync(Page.Locator("#option-add"), Page.Locator("#option-name-0"));
        await SetAsync("#option-name-0", "Size");
        await SetAsync("#option-0-value-0", "Small");
        await Page.Locator("[data-testid=option-card]").Nth(0).GetByRole(AriaRole.Button, new() { Name = "Add a value" }).ClickAsync();
        await SetAsync("#option-0-value-1", "Large");
        await ClickUntilAsync(Page.Locator("#option-add"), Page.Locator("#option-name-1"));
        await SetAsync("#option-name-1", "Edition");
        await SetAsync("#option-1-value-0", "Standard");
        await Page.Locator("[data-testid=option-card]").Nth(1).GetByRole(AriaRole.Button, new() { Name = "Add a value" }).ClickAsync();
        await SetAsync("#option-1-value-1", "Pro");
        await Page.Locator("#options-save").ClickAsync();
        var saved = Page.GetByText("Options saved.").Or(Page.Locator("#options-refusal"));
        await Expect(saved.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That(await Page.Locator("#options-refusal").CountAsync(), Is.Zero,
            await Page.Locator("#options-refusal").CountAsync() > 0 ? await Page.Locator("#options-refusal").InnerTextAsync() : "");

        await Page.Locator("#variants-generate").ClickAsync();
        await Expect(Page.Locator("[data-testid=variant-row]")).ToHaveCountAsync(4, new() { Timeout = 20_000 });
        await Expect(Page.Locator("#variants")).ToContainTextAsync("Small / Standard");

        // Stock on one variant, through the dialog, and it shows in the log.
        await Page.Locator("[data-testid=variant-stock]").First.ClickAsync();
        await SetAsync("#stock-delta", "10");
        await Page.Locator("#stock-adjust-save").ClickAsync();
        await Expect(Page.Locator("[data-testid=variant-row]").First).ToContainTextAsync("10");
        await Page.Locator("[data-testid=variant-row]").First.GetByRole(AriaRole.Button, new() { Name = "Stock log" }).ClickAsync();
        await Expect(Page.Locator("[data-testid=stock-log-row]")).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Page.Keyboard.PressAsync("Escape");

        // A picture, then on sale.
        await OpenTabAsync("Pictures", Page.Locator("#product-image-input"));
        await Page.Locator("#product-image-input").SetInputFilesAsync(FixturePhoto);
        await Expect(Page.Locator("[data-testid=picture-card]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        await ClickUntilAsync(Page.Locator("#product-activate"), Page.Locator("#product-deactivate"));
        await Expect(Page.Locator("#product-status")).ToHaveTextAsync("Live");
    }

    [Test]
    [Description("The stock page receives a delivery across two variants in one save, and logs both.")]
    public async Task Stock_page_receives_across_two_variants_in_one_save()
    {
        using var api = await StoreTestApi.OpenAsync();
        var name = Unique("Field Bag");
        var product = await api.ProductAsync(await api.CategoryAsync(Unique("Bags")), name, twoSizes: true);

        await Page.GotoAsync($"{BaseUrl}/admin/store/stock");
        await WaitForTheCircuitAsync();
        await SetAsync("#stock-search", name);
        await Expect(Page.Locator("[data-testid=stock-row]")).ToHaveCountAsync(2, new() { Timeout = 20_000 });

        var boxes = Page.Locator("[data-testid=stock-receive]");
        await boxes.Nth(0).FillAsync("5");
        await boxes.Nth(0).PressAsync("Tab");
        await boxes.Nth(1).FillAsync("7");
        await boxes.Nth(1).PressAsync("Tab");
        await SetAsync("#stock-note-all", "Delivery e2e");
        await Expect(Page.Locator("#stock-save")).ToContainTextAsync("2 changes");
        await Page.Locator("#stock-save").ClickAsync();

        // The save's own answer, before asking the API — the row text alone proves nothing, since
        // the product's name can contain a 5.
        await Expect(Page.GetByText("2 variants updated.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.Locator("#stock-refusal")).ToHaveCountAsync(0);

        var fresh = await api.SendAsync(HttpMethod.Get, $"/api/admin/store/products/{product.GetProperty("id").GetGuid()}");
        var onHand = fresh.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("stockOnHand").GetInt32()).Order().ToList();
        Assert.That(onHand, Is.EqualTo(new[] { 5, 7 }));
    }

    [Test]
    [Description("Export CSV downloads the stock list as a file with the header row.")]
    public async Task Stock_export_downloads_a_csv()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/store/stock");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#stock-export")).ToBeEnabledAsync(new() { Timeout = 20_000 });

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.Locator("#stock-export").ClickAsync());
        Assert.That(download.SuggestedFilename, Does.EndWith(".csv"));
        var path = await download.PathAsync();
        var text = await File.ReadAllTextAsync(path!);
        Assert.That(text, Does.Contain("Sku,Product,Variant,OnHand,Reserved,Available,Low"));
    }

    [Test]
    [Description("Store settings save, read back, and a bad state is refused on the page with nothing saved.")]
    public async Task Settings_save_and_read_back()
    {
        using var api = await StoreTestApi.OpenAsync();
        var before = await api.SendAsync(HttpMethod.Get, "/api/admin/store/settings");
        try
        {
            await Page.GotoAsync($"{BaseUrl}/admin/store/settings");
            await WaitForTheCircuitAsync();
            await Expect(Page.Locator("#store-settings-save")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            // The box takes two letters, so "Tennessee" cannot even be typed; two that name no
            // state are refused by the server, on the page, with nothing saved.
            await SetAsync("#store-ship-state", "ZZ");
            await Page.Locator("#store-settings-save").ClickAsync();
            await Expect(Page.Locator("#store-settings-refusal")).ToContainTextAsync("two letters, like TN");

            await SetAsync("#store-ship-state", "tn");
            await SetAsync("#store-shipping", "7.5");
            await Page.Locator("#store-settings-save").ClickAsync();
            // The refusal clears as the save STARTS, so its absence proves nothing — reloading then
            // read the old flat rate back (09/24). The toast comes only once the server has it.
            await Expect(Page.GetByText(new Regex(@"^Saved\."))).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(Page.Locator("#store-settings-refusal")).ToHaveCountAsync(0);

            await Page.ReloadAsync();
            await WaitForTheCircuitAsync();
            await Expect(Page.Locator("#store-ship-state")).ToHaveValueAsync("TN", new() { Timeout = 30_000 });
            await Expect(Page.Locator("#store-shipping")).ToHaveValueAsync(new Regex(@"^7\.50?$"));
            await Expect(Page.Locator("#ready-to-sell")).ToBeVisibleAsync();
        }
        finally
        {
            decimal? Money(string p) => before.GetProperty(p).ValueKind == JsonValueKind.Null ? null : before.GetProperty(p).GetDecimal();
            int? Whole(string p) => before.GetProperty(p).ValueKind == JsonValueKind.Null ? null : before.GetProperty(p).GetInt32();
            string? Text(string p) => before.GetProperty(p).ValueKind == JsonValueKind.Null ? null : before.GetProperty(p).GetString();
            await api.SendAsync(HttpMethod.Put, "/api/admin/store/settings", new
            {
                checkoutEnabled = before.GetProperty("checkoutEnabled").GetBoolean(),
                shippingFlatRate = Money("shippingFlatRate"), freeShippingThreshold = Money("freeShippingThreshold"),
                lowStockThreshold = Whole("lowStockThreshold"),
                shipFromStreet = Text("shipFromStreet"), shipFromCity = Text("shipFromCity"),
                shipFromState = Text("shipFromState"), shipFromZip = Text("shipFromZip"), supportEmail = Text("supportEmail"),
                returnsWindowDays = Whole("returnsWindowDays"), reservationMinutes = Whole("reservationMinutes"),
                linkEnabled = before.GetProperty("linkEnabled").GetBoolean(),
            });
        }
    }

    [Test]
    [Description("With the shop switched off the dashboard says so and links to the switch.")]
    public async Task Dashboard_says_the_store_is_off_and_links_to_the_switch()
    {
        var wasOn = await SetTheStoreAsync(on: false);
        Assert.That(wasOn, Is.Not.Null, "Could not switch the store off.");
        try
        {
            await Page.GotoAsync($"{BaseUrl}/admin/store");
            await WaitForTheCircuitAsync();
            var warning = Page.Locator("#store-off");
            await Expect(warning).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(warning.GetByRole(AriaRole.Link)).ToHaveAttributeAsync("href", "/admin/site-settings?setting=features.store");
            await Expect(Page.Locator("#store-sales")).ToBeVisibleAsync();
        }
        finally
        {
            await PutTheStoreBackAsync(wasOn);
        }
    }

    [Test]
    [Description("A new discount code shows as Live, and one nobody used can be deleted.")]
    public async Task Coupons_create_shows_live_badge()
    {
        var code = $"E2E{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        await Page.GotoAsync($"{BaseUrl}/admin/store/coupons");
        await WaitForTheCircuitAsync();
        await ClickUntilAsync(Page.Locator("#coupon-new"), Page.Locator("#coupon-code"));
        await SetAsync("#coupon-code", code.ToLowerInvariant());
        await SetAsync("#coupon-percent", "15");
        await Page.Locator("#coupon-save").ClickAsync();

        var row = Page.Locator("[data-testid=coupon-row]").Filter(new() { HasText = code });
        await Expect(row).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Expect(row.Locator("[data-testid=coupon-live]")).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Expect(row).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    [Test]
    [Description("An item's History tab lists who changed what, newest first — the price line included for the store's staff.")]
    public async Task The_history_tab_says_who_changed_what()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.ProductAsync(await api.CategoryAsync(Unique("Trigger Objects")), Unique("REM Pod"), live: true);

        await Page.GotoAsync($"{BaseUrl}/admin/store/products/{product.GetProperty("id").GetGuid()}/edit?tab=history");
        await WaitForTheCircuitAsync();

        var lines = Page.Locator("[data-testid=product-history-line]");
        await Expect(lines.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(lines.First.Locator("[data-testid=product-history-summary]")).ToHaveTextAsync("Put it on sale.");
        await Expect(lines.Last.Locator("[data-testid=product-history-summary]")).ToHaveTextAsync("Created it.");
        await Expect(Page.Locator("[data-testid=product-history-line][data-area=Price]")).ToContainTextAsync("$39.00");
        await Expect(lines.First.Locator("[data-testid=product-history-actor]")).Not.ToHaveTextAsync("The store");
    }

    [Test]
    [Description("Hiding a category says how many live products go with it BEFORE saving, then hides them.")]
    public async Task Category_deactivate_warns_about_live_products()
    {
        using var api = await StoreTestApi.OpenAsync();
        var categoryName = Unique("Trigger Objects");
        var categoryId = await api.CategoryAsync(categoryName);
        await api.ProductAsync(categoryId, Unique("REM Pod"), live: true);

        await Page.GotoAsync($"{BaseUrl}/admin/store/categories");
        await WaitForTheCircuitAsync();
        var row = Page.Locator("[data-testid=category-row]").Filter(new() { HasText = categoryName });
        await Expect(row).ToContainTextAsync("1 live", new() { Timeout = 20_000 });

        await ClickUntilAsync(row.GetByRole(AriaRole.Button, new() { Name = "Edit" }), Page.Locator("#category-active"));
        await Page.Locator("#category-active").UncheckAsync();
        await Page.Locator("#category-save").ClickAsync();

        // The warning comes first, from the list's own count — nothing has been saved yet.
        await Expect(Page.Locator("#category-hide-warning")).ToContainTextAsync("1 live product");
        var still = await api.SendAsync(HttpMethod.Get, "/api/admin/store/categories");
        Assert.That(still.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == categoryId).GetProperty("isActive").GetBoolean(), Is.True);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Hide it" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Hidden", new() { Timeout = 15_000 });
        await Expect(row).ToContainTextAsync("0 live");
    }
}
