using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The store as a visitor with no account finds it (storefront S2.7–S2.8): the home page, search,
/// the listing's filters, sorting and pages in the address, and what a sold-out or hidden product
/// looks like. Reads the demo store (StoreDemoSeeder) by slug; writes nothing another fixture reads.
/// </summary>
[TestFixture]
[Category("Store")]
public class StoreBrowseTests : BenTestBase
{
    private ILocator Card(string slug) => Page.Locator($"[data-testid=store-card][data-slug={slug}]");

    /// <summary>
    /// The server draws the page, then the live connection takes over. It must take over what is
    /// already there: before 09/24 it built each store page again from nothing, so the shop turned
    /// into "Opening the store…" and back in front of every visitor.
    /// </summary>
    [TestCase("/store", "Opening the store", "[data-testid=store-hero]")]
    [TestCase("/store/products", "Finding products", "[data-testid=listing-grid]")]
    [TestCase("/store/p/k-ii-emf-meter", "Loading the product", "[data-testid=product-price]")]
    public async Task The_page_does_not_blink_when_it_comes_alive(string path, string loading, string content)
    {
        // Watches the whole load: any moment the loading words appear AFTER the content has.
        await Page.AddInitScriptAsync($$"""
            window.__blinked = false;
            new MutationObserver(() => {
                if (document.querySelector('{{content}}')) window.__seenContent = true;
                if (window.__seenContent && document.body && document.body.innerText.includes('{{loading}}')) window.__blinked = true;
            }).observe(document, { childList: true, subtree: true, characterData: true });
            """);
        // A page that never comes alive never blinks either — the first version of this test passed
        // against pages whose connection the server had refused. So it also listens for that.
        var dropped = new List<string>();
        Page.Console += (_, m) => { if (m.Type == "error" && m.Text.Contains("Connection closed with an error")) dropped.Add(m.Text); };

        await Page.GotoAsync($"{BaseUrl}{path}");
        await Expect(Page.Locator(content).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await WaitForTheCircuitAsync();
        await Page.WaitForTimeoutAsync(1500);   // the live page's first render, and anything it fetches

        Assert.That(dropped, Is.Empty, $"{path}: the server closed the live connection — the page is drawn but dead.");
        Assert.That(await Page.EvaluateAsync<bool>("window.__blinked === true"), Is.False,
            $"{path} went back to \"{loading}…\" after it had drawn — the live page rebuilt it from nothing.");

        // And it is live: the header's cart opens its menu, which only the circuit can do.
        await ClickUntilAsync(Page.Locator("#nav-cart"), Page.Locator(".ben-cart-dd.show"));
    }

    /// <summary>
    /// What a store page carries from its server render into the live page is sent back to the
    /// server in the connection's first message, and a message over the circuit's limit (128 KB,
    /// Ben.Web.Website/Program.cs) makes the server hang up. At 32 KB, the default, the listing and
    /// the front page were already over it. Each page keeps under half the limit, so a catalogue
    /// that grows is caught here before a shopper finds a dead page.
    /// </summary>
    [TestCase("/store")]
    [TestCase("/store/products")]
    [TestCase("/store/p/k-ii-emf-meter")]
    public async Task The_carried_state_fits_the_connection(string path)
    {
        var html = await (await Page.APIRequest.GetAsync($"{BaseUrl}{path}")).TextAsync();
        var state = Regex.Match(html, "<!--Blazor-Server-Component-State:(.*?)-->", RegexOptions.Singleline);
        var bytes = state.Success ? state.Groups[1].Value.Length : 0;
        Assert.That(bytes, Is.LessThan(64 * 1024), $"{path} carries {bytes:N0} bytes into its live page; the circuit takes 128 KB at most.");
    }

    [Test]
    [Description("A visitor with no account sees the hero, the promises, the shelves and the rails.")]
    public async Task Home_RendersForAVisitor()
    {
        await Page.GotoAsync($"{BaseUrl}/store");
        await Expect(Page.Locator("[data-testid=store-hero]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await WaitForTheCircuitAsync();   // count what the live page shows, not the server render's copy
        await Expect(Page.Locator("[data-testid=category-tile]").First).ToBeVisibleAsync();
        // At least the five demo shelves — other fixtures add shelves of their own to this database.
        Assert.That(await Page.Locator("[data-testid=category-tile]").CountAsync(), Is.GreaterThanOrEqualTo(5));
        await Expect(Page.Locator("[data-testid=category-tile]", new() { HasTextString = "EMF Meters" })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=rail-featured]")).ToContainTextAsync("K-II EMF Meter");
        await Expect(Page.Locator(".ben-promise")).ToContainTextAsync("Secure payment");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Shop now" }).First).ToBeVisibleAsync();
    }

    [Test]
    [Description("With the store switched off, /store is the ordinary 'Page not found'.")]
    public async Task Home_IsNotFound_WhenTheStoreIsOff()
    {
        var wasOn = await SetTheStoreAsync(on: false);
        Assert.That(wasOn, Is.Not.Null, "Could not switch the store off.");
        try
        {
            await Page.GotoAsync($"{BaseUrl}/store");
            await Expect(Page.GetByText("There is nothing at this address.")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.Locator("[data-testid=store-hero]")).ToHaveCountAsync(0);
        }
        finally { await PutTheStoreBackAsync(wasOn); }
    }

    [Test]
    [Description("Typing a name and pressing Enter lists what matches.")]
    public async Task Search_finds_a_product_by_name()
    {
        await Page.GotoAsync($"{BaseUrl}/store");
        await WaitForTheCircuitAsync();
        var box = Page.Locator("[data-testid=store-search]").First;
        // "P-SB7", not "spirit": other fixtures make spirit boxes of their own in this database.
        await box.FillAsync("P-SB7");
        await box.PressAsync("Enter");

        await Page.WaitForURLAsync("**/store/products?q=P-SB7*");
        await Expect(Card("p-sb7-spirit-box")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("[data-testid=listing-count]")).ToHaveTextAsync("1 product");
    }

    [Test]
    [Description("Two characters or more offer products as links while typing.")]
    public async Task Search_suggestions_appear_after_two_characters()
    {
        await Page.GotoAsync($"{BaseUrl}/store/products");
        await WaitForTheCircuitAsync();
        var box = Page.Locator("[data-testid=store-search]").First;

        await box.PressSequentiallyAsync("r");
        await Page.WaitForTimeoutAsync(800);
        await Expect(Page.Locator("[data-testid=store-search-suggestions]")).ToHaveCountAsync(0);

        await box.PressSequentiallyAsync("e");
        var list = Page.Locator("[data-testid=store-search-suggestions]");
        await Expect(list).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Which products "re" suggests depends on what else the e2e database has gathered — the
        // admin fixtures leave "Recorder …" products that fill the list ahead of the REM Pod. A
        // third letter narrows it to the product this test knows.
        await box.PressSequentiallyAsync("m");
        await Expect(list).ToContainTextAsync("REM Pod", new() { Timeout = 10_000 });
    }

    [Test]
    [Description("Filters, sorting and pages are the address: an option filter, its chip, and the sort.")]
    public async Task Listing_FiltersAndPagesThroughTheQueryString()
    {
        await Page.GotoAsync($"{BaseUrl}/store/products?opt=Colour%3AOlive");
        await Expect(Page.Locator("[data-testid=listing-count]")).ToHaveTextAsync("1 product", new() { Timeout = 30_000 });
        await Expect(Card("investigators-field-bag")).ToBeVisibleAsync();

        // The chip takes the filter off again.
        await Page.Locator("[data-testid=filter-chips] a").First.ClickAsync();
        await Page.WaitForURLAsync(url => !url.Contains("opt="));
        await Expect(Page.Locator("[data-testid=listing-count]")).Not.ToHaveTextAsync("1 product", new() { Timeout = 15_000 });

        // Sorting from a page of its own: after the chip, the listing is swapped in place, and the
        // circuit check can pass on the markup being replaced — the choice then lands on a select
        // nothing is listening to yet (it began failing once the e2e database held ~465 products).
        await Page.GotoAsync($"{BaseUrl}/store/products");
        await WaitForTheCircuitAsync();
        await Page.Locator("#listing-sort").SelectOptionAsync("price-desc");
        await Page.WaitForURLAsync("**/store/products?sort=price-desc");
        await Expect(Page.Locator("[data-testid=store-card]").First).ToHaveAttributeAsync("data-slug", "rem-pod", new() { Timeout = 15_000 });

        await Page.GotoAsync($"{BaseUrl}/store/c/spirit-boxes");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Spirit Boxes", new() { Timeout = 30_000 });
        await Expect(Card("p-sb7-spirit-box")).ToBeVisibleAsync();
    }

    [Test]
    [Description("A product with an in-stock variant is not marked sold out; one with none left is, and still listed.")]
    public async Task Sold_out_is_listed_with_the_marker()
    {
        using var api = await StoreTestApi.OpenAsync();
        var shelf = $"Sold Out Shelf {Guid.NewGuid().ToString("N")[..6]}";
        var product = await api.ProductAsync(await api.CategoryAsync(shelf), $"Empty Box {Guid.NewGuid().ToString("N")[..6]}", live: true);
        await api.SetStockAsync(product, 0);
        var slug = product.GetProperty("slug").GetString()!;

        // Found by name, not by its place on page one: "Popular" ranks by units sold, and since the
        // product's own count is kept (store sellers P0) whatever earlier runs bought outranks it.
        await Page.GotoAsync($"{BaseUrl}/store/products?q={Uri.EscapeDataString("P-SB7 Spirit Box")}");
        await Expect(Card("p-sb7-spirit-box")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Card("p-sb7-spirit-box")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("ben-card--soldout"));

        await Page.GotoAsync($"{BaseUrl}/store/products?q={Uri.EscapeDataString(product.GetProperty("name").GetString()!)}");
        await Expect(Card(slug)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("ben-card--soldout"), new() { Timeout = 30_000 });
        await Expect(Card(slug)).ToContainTextAsync("Sold out");
    }

    [Test]
    [Description("A hidden product answers 'Page not found' by its address and is nowhere in the grid.")]
    public async Task Inactive_product_is_404_by_url_and_absent_from_the_grid()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/boo-buddy");
        await Expect(Page.Locator("[data-testid=store-not-found]")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Page.GotoAsync($"{BaseUrl}/store/products");
        await Expect(Card("k-ii-emf-meter")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Card("boo-buddy")).ToHaveCountAsync(0);
    }
}
