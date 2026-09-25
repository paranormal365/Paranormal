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
