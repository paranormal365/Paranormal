using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Adding an email address on your own profile confirms it, and cannot make it primary (item 237).
/// </summary>
/// <remarks>
/// <para><b>What Ben hit on 2026-09-12.</b> He added an address, marked it primary, and nothing
/// had ever checked that he could read it — and on returning to the page, nothing said the save had
/// happened at all. The controller tests pin both rules; this pins the two things only a real
/// screen can show: that the Primary tick is unavailable rather than merely refused on save, and
/// that the card says what it did.</para>
///
/// <para>A unique address per run, because this account accumulates rows across runs and a fixed
/// one would pass the first time and collide afterwards.</para>
/// </remarks>
public class ProfileEmailConfirmationTests : BenTestBase
{
    private string _address = string.Empty;

    [SetUp]
    public async Task OpenTheContactTab()
    {
        _address = $"confirm-{Guid.NewGuid():N}@example.test";

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "About" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Page.GetByRole(AriaRole.Tab, new() { Name = "Contact" }).ClickAsync();
    }

    [Test]
    public async Task Adding_an_address_says_so_and_cannot_make_it_primary()
    {
        // The emails card's own Add button. Scoped to the card so the phones and addresses
        // cards' identical buttons cannot be the one that is clicked.
        var card = Page.Locator(".card", new() { HasText = "Email" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await card.GetByRole(AriaRole.Button, new() { Name = "Add" }).First.ClickAsync();

        // ── Only a real screen shows this: the tick is UNAVAILABLE, not merely refused on save.
        var primary = Page.Locator("#email-primary");
        await Expect(primary).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(primary).ToBeDisabledAsync();
        await Expect(Page.GetByText("Confirm this address first", new() { Exact = false }).First)
            .ToBeVisibleAsync();

        await Page.Locator("#myemailscard-address-f9a1").FillAsync(_address);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).First.ClickAsync();

        // ── And the save says what it did. Ben's complaint was that it said nothing at all.
        await Expect(Page.GetByText(_address, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("added", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // ── The row lands unconfirmed, and the confirmation has already been issued: on a machine
        //    with no mail server the link is shown, which is how this flow is walkable at all.
        await Expect(Page.GetByText("Not confirmed", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("/validate-email/", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }
}
