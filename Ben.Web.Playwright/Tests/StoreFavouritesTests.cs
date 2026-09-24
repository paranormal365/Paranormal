using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Favourites (storefront S6.3): the heart on a product and on its card, the header's count, the
/// Favourites page with Remove, and a guest's heart asking for a sign-in.
/// </summary>
/// <remarks>
/// James (the seeded member) starts with none and puts back what he adds; Sarah keeps the two
/// StoreDemoSeeder gives her (S6.4), the REM-Pod and the H1n.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreFavouritesTests : BenTestBase
{
    private ILocator BuyBoxHeart => Page.Locator(".ben-buybox [data-testid=store-heart]");

    private async Task OpenAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
    }

    [Test]
    [Description("A member hearts a product, finds it in Favourites with the header's count, and removes it after being asked.")]
    public async Task A_member_keeps_a_favourite_and_removes_it()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await OpenAsync("/store/p/k-ii-emf-meter");
        // A still heart until the sign-in is known; then a button that says whether it's kept.
        await Expect(BuyBoxHeart).ToHaveAttributeAsync("aria-pressed", new Regex("^(true|false)$"), new() { Timeout = 30_000 });
        if (await BuyBoxHeart.GetAttributeAsync("aria-pressed") == "true")   // a run that stopped half way
        {
            await BuyBoxHeart.ClickAsync();
            await Expect(BuyBoxHeart).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 15_000 });
        }

        await BuyBoxHeart.ClickAsync();
        await Expect(BuyBoxHeart).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 15_000 });
        await Expect(BuyBoxHeart).ToHaveAttributeAsync("aria-label", "Remove K-II EMF Meter from your favourites");
        await Expect(Page.Locator("[data-testid=favourites-badge]")).ToBeVisibleAsync();

        await Page.Locator("#nav-favourites").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/store/favourites$"), new() { Timeout = 15_000 });
        var card = Page.Locator("[data-testid=store-card][data-slug=k-ii-emf-meter]");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(card.Locator("[data-testid=store-heart]")).ToHaveCountAsync(0);   // Remove takes its place

        await card.Locator("[data-testid=favourite-remove]").ClickAsync();
        await Expect(Page.GetByText("Remove K-II EMF Meter from your favourites?")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();
        await Expect(card).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(Page.GetByText("No favourites yet")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=favourites-badge]")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("Sarah's seeded favourites are on her Favourites page, with their hearts filled wherever the cards appear.")]
    public async Task Seeded_favourites_are_listed()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync("/store/favourites");

        await Expect(Page.Locator("[data-testid=favourites-grid] [data-testid=store-card][data-slug=rem-pod]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=favourites-grid] [data-testid=store-card][data-slug=h1n-handy-recorder]")).ToBeVisibleAsync();
        // One entry lit in the menu — "Browse the Store" (/store) covers this address too, and was
        // lit beside Favourites until the nav learned to light only the most specific (09/24).
        await Expect(Page.Locator("#nav-menu li.nav-item.active:not(.has-ul) > a")).ToHaveAttributeAsync("href", "/store/favourites");

        await OpenAsync("/store/products");
        await Expect(Page.Locator("[data-testid=store-card][data-slug=rem-pod] [data-testid=store-heart]"))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 30_000 });
    }

    [Test]
    [Description("A guest's heart is a sign-in link that comes back to the page; the header has no favourites heart.")]
    public async Task A_guest_heart_asks_for_a_sign_in()
    {
        await OpenAsync("/store/p/k-ii-emf-meter");

        await Expect(BuyBoxHeart).ToHaveAttributeAsync("href", new Regex(@"/login\?returnUrl=%2Fstore%2Fp%2Fk-ii-emf-meter"), new() { Timeout = 30_000 });
        await Expect(Page.Locator("#nav-favourites")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("Signed out, the Favourites page asks for a sign-in and comes back.")]
    public async Task Signed_out_favourites_go_to_sign_in()
    {
        await OpenAsync("/store/favourites");
        await Page.WaitForURLAsync(new Regex(@"/login\?returnUrl=%2Fstore%2Ffavourites$"), new() { Timeout = 30_000 });
    }
}
