using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The store on a phone, a tablet either way up, and a desktop (storefront S7.3, the plan's
/// device rule): nothing scrolls sideways, the page stacks or sits side by side where the plan
/// says it does, and the control a buyer came for is on screen.
/// </summary>
/// <remarks>
/// <para><b>Four sizes</b> — 375×812, 768×1024, 1024×768 and 1280×800. The layout splits at
/// Bootstrap's lg (992): below it the product's gallery sits above the buy box and the checkout's
/// summary below its form, with a bar fixed to the bottom carrying the total and Continue or Place
/// order; from lg they sit side by side and the bar is gone.</para>
///
/// <para><b>"On screen without scrolling"</b> is asserted where the design promises it: Add to
/// cart at every size, and the checkout bar's button below lg. At lg and up Continue sits under
/// the form it submits, which is below the fold on purpose — there the test asserts the bar is
/// hidden and the form's own button is there.</para>
///
/// <para>Each size opens the page fresh at that size: a page loaded wide and then narrowed keeps
/// what it measured on load.</para>
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreDeviceTests : BenTestBase
{
    private const int Lg = 992;

    private async Task OpenAtAsync(string path, int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
    }

    private async Task AssertNoSidewaysScrollAsync(string what)
    {
        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.That(overflow, Is.LessThanOrEqualTo(1), $"{what} scrolls sideways by {overflow}px.");
    }

    /// <summary>The element is drawn and wholly inside the window as it first opens.</summary>
    private async Task AssertOnScreenAsync(ILocator element, int height, string what)
    {
        await Expect(element).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var box = await element.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, $"{what} has no box.");
        Assert.That(box!.Y + box.Height, Is.LessThanOrEqualTo(height + 0.5), $"{what} is below the fold (bottom at {box.Y + box.Height:0}px of {height}).");
    }

    [TestCase(375, 812)]
    [TestCase(768, 1024)]
    [TestCase(1024, 768)]
    [TestCase(1280, 800)]
    [Description("The store's front, list, product and cart fit the width; the product stacks below lg and sits side by side from it, with Add to cart on screen.")]
    public async Task Shop_pages_fit_and_the_product_stacks_where_it_should(int width, int height)
    {
        foreach (var path in new[] { "/store", "/store/products", "/store/cart" })
        {
            await OpenAtAsync(path, width, height);
            await Expect(Page.Locator("h1").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await AssertNoSidewaysScrollAsync($"{path} at {width}×{height}");
        }

        await OpenAtAsync("/store/p/k-ii-emf-meter", width, height);
        var addToCart = Page.Locator("[data-testid=add-to-cart]");
        await AssertOnScreenAsync(addToCart, height, $"Add to cart at {width}×{height}");
        await AssertNoSidewaysScrollAsync($"the product page at {width}×{height}");

        var gallery = await Page.Locator(".ben-buybox").Locator("xpath=ancestor::div[contains(@class,'row')][1]/div[1]").BoundingBoxAsync();
        var buyBox = await Page.Locator(".ben-buybox").BoundingBoxAsync();
        Assert.That(gallery, Is.Not.Null);
        Assert.That(buyBox, Is.Not.Null);
        if (width < Lg)
            Assert.That(buyBox!.Y, Is.GreaterThanOrEqualTo(gallery!.Y + gallery.Height - 1), $"At {width} the buy box should sit under the gallery.");
        else
            Assert.That(buyBox!.X, Is.GreaterThanOrEqualTo(gallery!.X + gallery.Width - 1), $"At {width} the buy box should sit beside the gallery.");
    }

    [TestCase(375, 812)]
    [TestCase(768, 1024)]
    [TestCase(1024, 768)]
    [TestCase(1280, 800)]
    [Description("The slide-out cart is at most 420px wide — never the whole of a tablet — and To checkout is on screen.")]
    public async Task The_cart_drawer_fits(int width, int height)
    {
        await OpenAtAsync("/store/p/investigators-field-bag", width, height);
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await ClickUntilAsync(Page.Locator("[data-testid=add-to-cart]"), Page.Locator("[data-testid=cart-drawer]"));

        var drawer = Page.Locator("[data-testid=cart-drawer]");
        var box = await drawer.BoundingBoxAsync();
        Assert.That(box!.Width, Is.LessThanOrEqualTo(Math.Min(420, width) + 0.5), $"The drawer is {box.Width:0}px wide at {width}.");
        await AssertOnScreenAsync(drawer.GetByText("To checkout"), height, $"the drawer's To checkout at {width}×{height}");
        await AssertNoSidewaysScrollAsync($"the page with the drawer open at {width}×{height}");
    }

    [TestCase(375, 812)]
    [TestCase(768, 1024)]
    [TestCase(1024, 768)]
    [TestCase(1280, 800)]
    [Description("Checkout fits in both steps; below lg the bar carries Continue and then Place order on screen, from lg the form's own buttons do and the bar is gone.")]
    public async Task Checkout_fits_in_both_steps(int width, int height)
    {
        await OpenAtAsync("/store/p/investigators-field-bag", width, height);
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await ClickUntilAsync(Page.Locator("[data-testid=add-to-cart]"), Page.Locator("[data-testid=cart-drawer]"));

        await OpenAtAsync("/store/checkout", width, height);
        await Expect(Page.Locator("#checkout-email")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await AssertNoSidewaysScrollAsync($"checkout at {width}×{height}");

        var narrow = width < Lg;
        var continueButton = Page.Locator(narrow ? "#store-continue-payment-bar" : "#store-continue-payment");
        if (narrow) await AssertOnScreenAsync(continueButton, height, $"the bar's Continue at {width}×{height}");
        else
        {
            await Expect(Page.Locator("[data-testid=checkout-bar]")).ToBeHiddenAsync();
            await Expect(continueButton).ToBeAttachedAsync();
        }

        await FillAndConfirmAsync("#checkout-email", $"device-{width}-{Guid.NewGuid().ToString("N")[..6]}@example.com");
        await FillAndConfirmAsync("#shipping-full-name", "Ada Buyer");
        await FillAndConfirmAsync("#shipping-phone", "615-555-0100");
        await FillAndConfirmAsync("#shipping-street1", "1 Elm St");
        await FillAndConfirmAsync("#shipping-city", "Nashville");
        await Page.SelectOptionAsync("#shipping-state", "TN");
        await FillAndConfirmAsync("#shipping-zip", "37203");
        await Page.Locator("#checkout-terms").CheckAsync();
        await ContinuePastTheLimitAsync(continueButton);
        var orderId = await Page.Locator("[data-testid=checkout-payment]").GetAttributeAsync("data-order-id");
        try
        {
            await AssertNoSidewaysScrollAsync($"the Payment step at {width}×{height}");
            var placeOrder = Page.Locator(narrow ? "#store-place-order-bar" : "#store-place-order");
            if (narrow) await AssertOnScreenAsync(placeOrder, height, $"the bar's Place order at {width}×{height}");
            else await Expect(placeOrder).ToBeAttachedAsync();
        }
        finally
        {
            // Let the checkout go, as the order desk would: every guest here is one address, and an
            // address may hold only three open checkouts — a fourth size would be refused.
            using var api = await StoreTestApi.OpenAsync();
            await api.TrySendAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/release");
        }
    }

    /// <summary>
    /// Every browser here is one guest address, and checkout allows ten tries a minute: when the
    /// page says to wait, wait and press again, as a buyer would (as StoreCheckoutTests does).
    /// </summary>
    private async Task ContinuePastTheLimitAsync(ILocator button)
    {
        var payment = Page.Locator("[data-testid=checkout-payment]");
        var refusal = Page.Locator("#checkout-refusal");
        var limited = refusal.Filter(new() { HasText = "wait a minute" });
        var tooManyOpen = refusal.Filter(new() { HasText = "Too many checkouts are open" });
        await button.ClickAsync();
        await Expect(payment.Or(limited).Or(tooManyOpen)).ToBeVisibleAsync(new() { Timeout = 60_000 });
        if (await payment.IsVisibleAsync()) return;

        if (await tooManyOpen.IsVisibleAsync()) await ReleaseOpenCheckoutsAsync();
        else await Task.Delay(TimeSpan.FromSeconds(61));
        await button.ClickAsync();
        await Expect(payment).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>
    /// Lets go of every checkout still waiting for payment. An address may hold three, and every
    /// browser in the suite is the same address, so checkouts other tests left open (they expire
    /// after fifteen minutes) would otherwise refuse this one.
    /// </summary>
    private static async Task ReleaseOpenCheckoutsAsync()
    {
        using var api = await StoreTestApi.OpenAsync();
        var open = await api.SendAsync(HttpMethod.Get, "/api/admin/store/orders?status=PendingPayment");
        // A 409 is a checkout already let go (the seeded abandoned one) or still going through at Stripe.
        foreach (var order in open.EnumerateArray())
            await api.TrySendAsync(HttpMethod.Post, $"/api/admin/store/orders/{order.GetProperty("id").GetString()}/release");
    }

    [TestCase(375, 812)]
    [TestCase(768, 1024)]
    [TestCase(1024, 768)]
    [TestCase(1280, 800)]
    [Description("An order on the desk fits the width, with its heading on screen, so an order can be worked from a tablet or a phone.")]
    public async Task An_order_on_the_desk_fits(int width, int height)
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await OpenAtAsync("/admin/store/orders/a1000000-0000-0000-0000-000000000101", width, height);

        var heading = Page.Locator("h3").Filter(new() { HasTextRegex = new Regex(@"^Order #\d+") });
        await AssertOnScreenAsync(heading, height, $"the order's heading at {width}×{height}");
        await AssertNoSidewaysScrollAsync($"an admin order at {width}×{height}");
    }
}
