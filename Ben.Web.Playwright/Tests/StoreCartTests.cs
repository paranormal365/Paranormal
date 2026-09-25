using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The cart (storefront S3): the header's badge and menu, the slide-out drawer and the cart page,
/// for a visitor with no account — whose cart is the browser's cookie — and for a member.
/// </summary>
/// <remarks>
/// Every test makes its own products with names nobody else uses, and a fresh browser has a fresh
/// cart, so no test reads another's numbers. Store settings a test changes are put back.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreCartTests : BenTestBase
{
    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    private static string Slug(JsonElement product) => product.GetProperty("slug").GetString()!;

    private ILocator Badge => Page.Locator("#nav-cart [data-testid=cart-badge]");

    private async Task AddFromProductPageAsync(JsonElement product, int quantity = 1)
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/{Slug(product)}");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        if (quantity != 1) await FillAndConfirmAsync("#product-quantity", quantity.ToString());
        await ClickUntilAsync(Page.Locator("[data-testid=add-to-cart]"), Page.Locator("[data-testid=cart-drawer]"));
    }

    private async Task<string> RowAsync(ILocator surface, string testId)
        => (await surface.Locator($"[data-testid={testId}]").First.InnerTextAsync()).Replace("\n", " ").Trim();

    [Test]
    [Description("A visitor with no account adds to the cart: the badge counts it, the drawer shows it, the page does not move.")]
    public async Task AddingToCart_UpdatesTheHeaderBadge_WithoutNavigating_SignedOut()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("Cat Ball"), 19.5m);

        await AddFromProductPageAsync(product, quantity: 2);

        Assert.That(Page.Url, Does.Contain($"/store/p/{Slug(product)}"));
        await Expect(Badge).ToHaveTextAsync("2", new() { Timeout = 15_000 });
        await Expect(Page.Locator("[data-testid=cart-drawer] [data-testid=cart-line]")).ToHaveCountAsync(1);

        // The cookie holds the cart across a reload: it is the browser's, not the page's.
        await Page.ReloadAsync();
        await WaitForTheCircuitAsync();
        await Expect(Badge).ToHaveTextAsync("2", new() { Timeout = 15_000 });
    }

    [Test]
    [Description("A member adds to the cart the same way; the badge rises by what was added.")]
    public async Task AddingToCart_UpdatesTheHeaderBadge_WithoutNavigating_SignedIn()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("REM Pod"), 12m);
        await LoginAsync(MemberEmail, MemberPassword);

        await Page.GotoAsync($"{BaseUrl}/store");
        await WaitForTheCircuitAsync();
        var before = await Badge.CountAsync() == 0 ? 0 : int.Parse(await Badge.InnerTextAsync());

        await AddFromProductPageAsync(product);

        await Expect(Badge).ToHaveTextAsync((before + 1).ToString(), new() { Timeout = 15_000 });
        Assert.That(Page.Url, Does.Contain($"/store/p/{Slug(product)}"));

        // Leave the member's cart as it was.
        await Page.GotoAsync($"{BaseUrl}/store/cart");
        var line = Page.Locator("[data-testid=cart-line]").Filter(new() { HasText = product.GetProperty("name").GetString()! });
        await ClickUntilAsync(line.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Remove") }), Page.GetByText("removed from your cart"));
    }

    [Test]
    [Description("Products, Shipping and Total before tax read the same on the cart page, the drawer and the header menu.")]
    public async Task The_four_surfaces_agree()
    {
        using var api = await StoreTestApi.OpenAsync();
        var restore = await api.WithSettingsAsync(flatRate: 7.95m, freeOver: 500m);
        try
        {
            var a = await api.BuyableAsync(Unique("Field Bag"), 39m);
            var b = await api.BuyableAsync(Unique("Trigger Box"), 12.25m);
            await AddFromProductPageAsync(a, quantity: 2);
            await AddFromProductPageAsync(b);

            var drawer = Page.Locator("[data-testid=cart-drawer]");
            var fromDrawer = (await RowAsync(drawer, "summary-products"), await RowAsync(drawer, "summary-shipping"), await RowAsync(drawer, "summary-total"));
            Assert.That(fromDrawer, Is.EqualTo(("Products $90.25", "Shipping $7.95", "Total before tax $98.20")));

            await Page.Keyboard.PressAsync("Escape");
            await ClickUntilAsync(Page.Locator("#nav-cart"), Page.Locator("[data-testid=cart-menu-money]"));
            var menu = Page.Locator(".ben-cart-dd");
            var fromMenu = (await RowAsync(menu, "summary-products"), await RowAsync(menu, "summary-shipping"), await RowAsync(menu, "summary-total"));

            await Page.GotoAsync($"{BaseUrl}/store/cart");
            var summary = Page.Locator("[data-testid=order-summary]");
            await Expect(summary).ToBeVisibleAsync(new() { Timeout = 30_000 });
            var fromPage = (await RowAsync(summary, "summary-products"), await RowAsync(summary, "summary-shipping"), await RowAsync(summary, "summary-total"));

            Assert.That(fromMenu, Is.EqualTo(fromDrawer));
            Assert.That(fromPage, Is.EqualTo(fromDrawer));
        }
        finally { await restore(); }
    }

    [Test]
    [Description("Shipping reads '–' with nothing in the cart, the flat rate below the threshold, 'Free' at it.")]
    public async Task Shipping_reads_dash_flat_then_Free()
    {
        using var api = await StoreTestApi.OpenAsync();
        var restore = await api.WithSettingsAsync(flatRate: 7.95m, freeOver: 50m);
        try
        {
            var product = await api.BuyableAsync(Unique("Spirit Box"), 25m);

            await Page.GotoAsync($"{BaseUrl}/store");
            await WaitForTheCircuitAsync();
            await ClickUntilAsync(Page.Locator("#nav-cart"), Page.Locator("[data-testid=cart-menu-empty]"));
            await Page.Keyboard.PressAsync("Escape");

            await AddFromProductPageAsync(product);
            await Expect(Page.Locator("[data-testid=cart-drawer] [data-testid=summary-shipping]")).ToContainTextAsync("$7.95");

            await Page.GotoAsync($"{BaseUrl}/store/cart");
            var plus = Page.Locator("[data-testid=cart-line]").First.GetByRole(AriaRole.Button, new() { Name = "One more" });
            await ClickUntilAsync(plus, Page.Locator("[data-testid=order-summary] [data-testid=summary-shipping]", new() { HasTextString = "Free" }));
            await Expect(Page.Locator("[data-testid=order-summary] [data-testid=summary-products]")).ToContainTextAsync("$50.00");
        }
        finally { await restore(); }
    }

    [Test]
    [Description("The cart page: + raises the line and Products; + beyond the stock stops there; a bad code is refused in words; a good one takes its discount; removing the last line shows the empty cart.")]
    public async Task CartPage_QuantityAndCoupon()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("K-II Meter"), 60m, onHand: 2);
        await AddFromProductPageAsync(product);

        await Page.GotoAsync($"{BaseUrl}/store/cart");
        await WaitForTheCircuitAsync();
        var line = Page.Locator("[data-testid=cart-line]").First;
        await Expect(line.Locator("[data-testid=cart-line-total]")).ToHaveTextAsync("$60.00", new() { Timeout = 30_000 });

        await ClickUntilAsync(line.GetByRole(AriaRole.Button, new() { Name = "One more" }),
            line.Locator("[data-testid=cart-line-total]", new() { HasTextString = "$120.00" }));
        await Expect(Page.Locator("[data-testid=order-summary] [data-testid=summary-products]")).ToContainTextAsync("$120.00");
        await Expect(line.GetByRole(AriaRole.Button, new() { Name = "One more" })).ToBeDisabledAsync();   // two on hand
        await Expect(line.Locator("[data-testid=cart-line-stock]")).ToContainTextAsync("Only 2 left");

        await ClickUntilAsync(Page.Locator("#open-discount"), Page.Locator("#discount_code"));
        await Page.Locator("#store-apply-coupon").ClickAsync();
        await Expect(Page.Locator("[data-testid=coupon-error]")).ToHaveTextAsync("Enter a code.");
        await FillAndConfirmAsync("#discount_code", "NOT-A-REAL-CODE");
        await Page.Locator("#store-apply-coupon").ClickAsync();
        await Expect(Page.Locator("[data-testid=coupon-error]")).ToHaveTextAsync("That code isn't one we recognise.", new() { Timeout = 15_000 });

        await FillAndConfirmAsync("#discount_code", "ghost10");
        await ClickUntilAsync(Page.Locator("#store-apply-coupon"), Page.Locator("[data-testid=applied-code]"));
        await Expect(Page.Locator("[data-testid=applied-code]")).ToContainTextAsync("GHOST10");
        await Expect(Page.Locator("[data-testid=summary-discount]")).ToContainTextAsync("$12.00");

        await ClickUntilAsync(line.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Remove") }),
            Page.GetByText("Your cart is empty"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Back to shopping" })).ToBeVisibleAsync();
        await Expect(Badge).ToHaveCountAsync(0);
    }

    [Test]
    [Description("On a phone: the header menu opens without widening the page, the drawer's close is in reach, and the cart page does not scroll sideways.")]
    public async Task Cart_fits_a_phone()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("Recorder"), 99.99m);
        await Page.SetViewportSizeAsync(375, 812);

        await AddFromProductPageAsync(product);
        var close = Page.Locator("[data-testid=cart-drawer] .btn-close");
        var box = await close.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.X + box.Width, Is.LessThanOrEqualTo(375));
        await close.ClickAsync();
        await Expect(Page.Locator("[data-testid=cart-drawer]")).ToHaveCountAsync(0);

        await ClickUntilAsync(Page.Locator("#nav-cart"), Page.Locator(".ben-cart-dd.show"));
        Assert.That(await Page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth"), Is.LessThanOrEqualTo(0),
            "Opening the cart menu widened the page.");
        await Page.Locator(".ben-cart-dd.show").GetByRole(AriaRole.Link, new() { Name = "Go to Cart" }).ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/store/cart"), new() { Timeout = 15_000 });
        await Expect(Page.Locator("[data-testid=order-summary]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await Page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth"), Is.LessThanOrEqualTo(0),
            "The cart page scrolls sideways on a phone.");
    }

    [Test]
    [Description("With orders paused the cart is kept, and every surface says why nothing can be ordered.")]
    public async Task Paused_checkout_is_a_sentence_on_the_cart()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("Boo Buddy"), 30m);
        var restore = await api.WithSettingsAsync(checkoutEnabled: false);
        try
        {
            await Page.GotoAsync($"{BaseUrl}/store/p/{Slug(product)}");
            await Expect(Page.Locator("[data-testid=store-paused]")).ToHaveTextAsync("The store isn't taking orders at the moment.", new() { Timeout = 30_000 });

            await AddFromProductPageAsync(product);   // the button stays: a cart can be kept
            await Expect(Page.Locator("[data-testid=cart-drawer] [data-testid=summary-why-not]"))
                .ToHaveTextAsync("The store isn't taking orders at the moment.");

            await Page.GotoAsync($"{BaseUrl}/store/cart");
            await Expect(Page.Locator("[data-testid=order-summary] [data-testid=summary-why-not]"))
                .ToHaveTextAsync("The store isn't taking orders at the moment.", new() { Timeout = 30_000 });
        }
        finally { await restore(); }
    }
}
