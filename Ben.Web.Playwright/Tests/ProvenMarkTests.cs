using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The one mark for a proved fact, on the screens that state one (item 237).
/// </summary>
/// <remarks>
/// <para>Read-only on purpose: it opens the profile and the security panel and looks. The source
/// guards in <c>BenProvenTests</c> prove that every such screen uses the shared component; this
/// proves the component actually draws, with its tooltip, on a real page — which no source scan
/// can tell you.</para>
///
/// <para>What it does NOT assert is which state each mark is in. That depends on the seeded
/// account and would make this a test about the seed rather than about the mark.</para>
/// </remarks>
public class ProvenMarkTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(UserEmail, UserPassword);

    [Test]
    public async Task The_profile_hero_states_whether_the_account_address_is_proved()
    {
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "About" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // One of the two, never neither: an Apple account says Apple proved it, and an ordinary
        // address says confirmed or not. A hero that said nothing was the state before this.
        var mark = Page.Locator(".badge", new() { HasTextRegex = new System.Text.RegularExpressions.Regex(
            "Email confirmed|Email not confirmed|Signed in with Apple") });

        await Expect(mark.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The tooltip is the whole reason it is a component rather than a tick: it says what was
        // proved, and when.
        var title = await mark.First.GetAttributeAsync("title");
        Assert.That(title, Is.Not.Null.And.Not.Empty,
            "a proved mark must say what it means on hover");
    }

    [Test]
    public async Task A_contact_address_carries_the_same_mark()
    {
        // Adds one rather than relying on the seed. The isolated database starts with no contact
        // addresses on this account, and a test that needs a seeded row is a test about the seed —
        // which the first run of this proved by failing on an empty list.
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "About" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Contact" }).ClickAsync();

        // Scoped by the card's own heading — "Email addresses" — because the phones, addresses
        // and links cards each have an identically named Add button next to it.
        var card = Page.Locator(".card").Filter(new()
        {
            Has = Page.GetByText("Email addresses", new() { Exact = false })
        }).Last;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // ClickUntilAsync, not ClickAsync: on Blazor Server the markup is there before the circuit
        // is, so the first click can land on a page that cannot yet handle it and simply do
        // nothing. The harness has this exactly because of that race.
        var address = Page.Locator("#myemailscard-address-f9a1");
        await ClickUntilAsync(
            card.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First,
            address);

        await address.FillAsync($"mark-{Guid.NewGuid():N}@example.test");

        var mark = Page.Locator(".badge", new() { HasTextRegex = new System.Text.RegularExpressions.Regex(
            "Not confirmed") });
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).First,
            mark.First);

        // A brand-new address is unproved, and the unproved state is drawn because there IS a path
        // — the send-confirmation button beside it. That pairing is the component's whole contract.
        await Expect(mark.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var title = await mark.First.GetAttributeAsync("title");
        Assert.That(title, Does.Contain("confirmation"),
            "an unproved mark must say what to do about it, not merely that it is unproved");
    }

    [Test]
    public async Task Two_factor_states_itself_the_same_way()
    {
        // Security is a tab on the profile, not a page of its own.
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "About" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Security" }).ClickAsync();

        var mark = Page.Locator(".badge", new() { HasTextRegex = new System.Text.RegularExpressions.Regex(
            "Two-factor on|Two-factor off") });

        await Expect(mark.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var title = await mark.First.GetAttributeAsync("title");
        Assert.That(title, Is.Not.Null.And.Not.Empty);
    }
}
