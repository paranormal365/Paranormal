using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The Selling workspace (store sellers, backlog 251): a seller finds it in the menu and sees their
/// own items and where each stands; somebody without the Seller role has no menu entry and is
/// turned away from the address.
/// </summary>
/// <remarks>
/// Hazel Marsh is the seeded demo seller (StoreDemoSeeder): the Hand-Built REM Pod on sale at
/// $149.00 and the Pocket EMF Logger, a draft the store hasn't priced. Read-only here; later phases
/// add the steps that change them.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreSellerWorkspaceTests : BenTestBase
{
    private ILocator SellingLink => Page.Locator("#nav-menu a[href='/store/selling']");
    private ILocator Item(string slug) => Page.Locator($"[data-testid=seller-item][data-slug={slug}]");

    [Test]
    [Description("The demo seller opens Selling → My Items from the menu and sees her item on sale and her unpriced draft.")]
    public async Task A_seller_sees_their_own_items()
    {
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("#nav-menu").GetByText("Selling", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.GotoAsync($"{BaseUrl}/store/selling");
        await WaitForTheCircuitAsync();

        var onSale = Item("hand-built-rem-pod");
        await Expect(onSale).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(onSale.Locator("[data-testid=seller-item-status]")).ToHaveTextAsync("On sale");
        await Expect(onSale).ToContainTextAsync("$149.00");

        var draft = Item("pocket-emf-logger");
        await Expect(draft.Locator("[data-testid=seller-item-status]")).ToHaveTextAsync("Draft");
        await Expect(draft).ToContainTextAsync("Not priced yet");

        // Hers alone: none of the store's own stock is listed.
        await Expect(Page.Locator("[data-testid=seller-item][data-slug=rem-pod]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#seller-items-refusal")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#nav-menu li.nav-item.active:not(.has-ul) > a")).ToHaveAttributeAsync("href", "/store/selling");
    }

    [Test]
    [Description("Hazel makes a draft with a picture and asks for it to go on sale; the store prices it and approves; she takes it off sale.")]
    public async Task A_draft_goes_on_sale_by_asking_and_comes_off_by_the_seller()
    {
        var name = $"Spirit Lantern {Guid.NewGuid().ToString("N")[..6]}";

        // The seller: a draft, a picture, and the ask.
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling");
        await WaitForTheCircuitAsync();
        await ClickUntilAsync(Page.Locator("#seller-add"), Page.Locator("#new-item-name"));
        await FillAndConfirmAsync("#new-item-name", name);
        await Page.Locator("#new-item-save").ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/store/selling/items/[0-9a-f-]{36}$"), new() { Timeout = 30_000 });
        var itemUrl = Page.Url;
        var productId = itemUrl[^36..];
        await Expect(Page.Locator("#seller-item-status")).ToHaveTextAsync("Draft", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=seller-item-price]")).ToHaveTextAsync("not set yet");

        await Page.GotoAsync($"{itemUrl}?tab=pictures");
        await WaitForTheCircuitAsync();
        await Page.Locator("#product-image-input").SetInputFilesAsync(StoreTestApi.FixturePhoto);
        await Expect(Page.Locator("[data-testid=picture-card]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        await ClickUntilAsync(Page.Locator("#seller-ask"), Page.Locator("#ask-price"));
        await FillAndConfirmAsync("#ask-price", "95");
        await Page.Locator("#ask-save").ClickAsync();
        await Expect(Page.Locator("[data-testid=seller-request-open]")).ToContainTextAsync("$95.00", new() { Timeout = 15_000 });

        // The store: the queue says it needs a price; price it, then approve.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store/sale-requests");
        await WaitForTheCircuitAsync();
        var request = Page.Locator($"[data-testid=sale-request][data-product='{productId}']");
        await Expect(request).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(request.Locator("[data-testid=sale-request-needs]")).ToContainTextAsync("Price the default variant first");
        await Expect(request.Locator("[data-testid=sale-request-approve]")).ToBeDisabledAsync();

        await Page.GotoAsync($"{BaseUrl}/admin/store/products/{productId}/edit?tab=variants");
        await WaitForTheCircuitAsync();
        var price = Page.Locator("[data-testid=variant-row] input[id^=variant-price-]");
        await Expect(price).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await FillAndConfirmAsync($"#{await price.GetAttributeAsync("id")}", "189");
        await Page.Locator("[data-testid=variant-save]").ClickAsync();
        await Expect(Page.Locator("[data-testid=product-sale-request]")).ToContainTextAsync("asks to put this on sale");
        await Expect(Page.Locator("#product-activate")).ToHaveCountAsync(0);   // not around the request

        await Page.GotoAsync($"{BaseUrl}/admin/store/sale-requests");
        await WaitForTheCircuitAsync();
        await Expect(request.Locator("[data-testid=sale-request-price]")).ToHaveTextAsync("$189.00", new() { Timeout = 30_000 });
        await request.Locator("[data-testid=sale-request-approve]").ClickAsync();
        await Expect(request).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        // The seller: on sale at the store's price, paid her ask; then off sale by her own hand.
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync(itemUrl);
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#seller-item-status")).ToHaveTextAsync("On sale", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=seller-item-price]")).ToHaveTextAsync("$189.00");
        await Expect(Page.GetByText("you're paid $95.00 a unit")).ToBeVisibleAsync();

        await ClickUntilAsync(Page.Locator("#seller-off-sale"), Page.GetByText("will disappear from the store"));
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Take off sale", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#seller-item-status")).ToHaveTextAsync("Off sale", new() { Timeout = 15_000 });
        await Expect(Page.Locator("#seller-delete")).ToHaveCountAsync(0);   // it has been on sale, so it stays
    }

    [Test]
    [Description("Hazel's REM pod's Parts & cost tab says what a unit costs to make and how many she can build, and follows her typing.")]
    public async Task The_parts_tab_says_what_a_unit_costs_to_make()
    {
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling");
        await WaitForTheCircuitAsync();
        await Item("hand-built-rem-pod").Locator("[data-testid=seller-item-link]").ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/store/selling/items/[0-9a-f-]{36}$"), new() { Timeout = 30_000 });
        await Page.GotoAsync($"{Page.Url}?tab=parts");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("[data-testid=part-row]")).ToHaveCountAsync(3, new() { Timeout = 30_000 });
        await Expect(Page.Locator("#parts-cost-basis")).ToHaveTextAsync("$34.50");
        await Expect(Page.Locator("#parts-buildable")).ToHaveTextAsync("4 units");

        // Worked out as she types, by the same arithmetic the save uses — nothing saved here.
        await FillAndConfirmAsync("#parts-other", "4.5");
        await Expect(Page.Locator("#parts-cost-basis")).ToHaveTextAsync("$35.50");
    }

    [Test]
    [Description("A member without the Seller role has no Selling entry and is sent home from the address.")]
    public async Task Somebody_who_does_not_sell_is_turned_away()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling");
        await WaitForTheCircuitAsync();

        await Page.WaitForURLAsync(new Regex(@"^[^?#]*//[^/]+/?$"), new() { Timeout = 30_000 });
        await Expect(SellingLink).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=seller-item]")).ToHaveCountAsync(0);
    }
}
