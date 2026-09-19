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

        // Retried, not clicked once. Blazor Server attaches its handlers when the circuit connects,
        // which happens AFTER NetworkIdle, and a click landing in that window is silently dropped —
        // the tab simply never opens and every assertion below then times out looking for a card
        // that was never rendered. This test had never passed for that reason.
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Tab, new() { Name = "Contact" }),
            Page.Locator(".card", new() { HasText = "Email addresses" }).First);
    }

    [Test]
    public async Task Adding_an_address_says_so_and_cannot_make_it_primary()
    {
        // The emails card's own Add button. Scoped to the card so the phones and addresses
        // cards' identical buttons cannot be the one that is clicked.
        var card = Page.Locator(".card", new() { HasText = "Email addresses" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // Retried, for the same reason the tab click is: a click that lands before the circuit
        // attaches is dropped in silence, and the form simply never opens.
        var primary = Page.Locator("#email-primary");
        await ClickUntilAsync(
            card.GetByRole(AriaRole.Button, new() { Name = "Add" }).First, primary);

        // ── Only a real screen shows this: the tick is UNAVAILABLE, not merely refused on save.
        await Expect(primary).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(primary).ToBeDisabledAsync();
        await Expect(Page.GetByText("Confirm this address first", new() { Exact = false }).First)
            .ToBeVisibleAsync();

        await Page.Locator("#myemailscard-address-f9a1").FillAsync(_address);
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).First,
            Page.GetByText("added", new() { Exact = false }).First);

        // ── And the save says what it did. Ben's complaint was that it said nothing at all.
        await Expect(Page.GetByText(_address, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("added", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // ── The row lands unconfirmed, and the confirmation has already been issued.
        await Expect(Page.GetByText("Not confirmed", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // How the confirmation reaches somebody depends on the machine, and the test must not
        // depend on which: with a mail server the card says the link is on its way, and without
        // one it prints the link, which is the only way this flow is walkable at all.
        //
        // The first version looked for the link as page TEXT. It is the value of a read-only
        // input, which GetByText cannot see, so the assertion could never pass — on any machine,
        // in any configuration. The feature had been working the whole time.
        var link = Page.Locator("#email-validation-link");
        var posted = Page.GetByText("on its way", new() { Exact = false });

        await Expect(link.Or(posted).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        if (await link.CountAsync() > 0)
            await Expect(link).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("/validate-email/"));
    }
}
