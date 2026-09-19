using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The pages a stranger meets before any account exists — pricing and the password reset door.
/// </summary>
/// <remarks>
/// Both earned their place the hard way. The pricing page's admin sibling killed its circuit on
/// production because only a real browser runs a page's lifecycle (item 145); this covers the
/// public face of the same feature on the same anonymous path a visitor uses. Forgot-password is
/// how an Entra-born account acquires its first password (item 142) — a broken door there locks
/// people out of their own accounts.
/// </remarks>
[TestFixture]
[Category("PublicSurface")]
public class PublicSurfaceTests : BenTestBase
{
    [Test]
    [Description("The pricing page renders its bands to a signed-out visitor, and the cadence toggle works.")]
    public async Task Pricing_renders_anonymously_and_the_toggle_switches_cadence()
    {
        await Page.GotoAsync($"{BaseUrl}/pricing");

        // The seeded bands, by their headings — an empty pricing page reads as "come back later"
        // by design, so presence of the bands is the assertion that the anonymous fetch worked.
        await Expect(Page.GetByText("Small group", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ANY price, not a specific one. This asserted "$15" and broke the day the ladder moved to
        // whole dollars — a test that pins a number the business is free to change reports a
        // pricing decision as a defect. What is worth guarding is that a price rendered at all,
        // because a blank band is the failure this test exists to catch.
        var anyMonthlyPrice = Page.GetByText(new System.Text.RegularExpressions.Regex(@"\$\d"));
        await Expect(anyMonthlyPrice.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var monthly = await anyMonthlyPrice.First.InnerTextAsync();

        // Yearly shows a DIFFERENT figure. Same reasoning as above: the relationship is the
        // contract, not the number.
        //
        // Waiting on the PRICE to change, not on the word "save" — the saving line is already on
        // screen before the toggle is touched, so a click-until-"save" returns instantly and reads
        // the monthly figure back as though nothing happened. Not.ToHaveTextAsync retries, which
        // is what makes this wait for the render rather than sample once.
        // One click, once, is lost if it lands before the circuit has attached - which on a loaded
        // machine it did, twice, on 2026-09-03, while the toggle itself worked fine on the live
        // site. ClickUntilAsync presses again until the Yearly button is the selected one.
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Yearly", Exact = false }).First,
            Page.Locator("button.btn-primary", new() { HasText = "Yearly" }).First);

        await Expect(anyMonthlyPrice.First)
            .Not.ToHaveTextAsync(monthly, new() { Timeout = 10_000 });

        await Expect(Page.GetByText("save", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("The forgot-password door opens, takes an address, and answers without disclosing accounts.")]
    public async Task Forgot_password_accepts_a_request_and_says_check_your_email()
    {
        await Page.GotoAsync($"{BaseUrl}/forgot-password");
        await Expect(Page.Locator("#forgot-email")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // A made-up address on purpose: the endpoint answers identically whether or not the
        // account exists, and this asserts exactly that non-disclosure from the outside.
        await FillAndConfirmAsync("#forgot-email", $"nobody-{Guid.NewGuid():N}@example.com");
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Send reset link", Exact = false }),
            Page.GetByText("reset link is on its way", new() { Exact = false }));
    }
}
