using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The profile page's tabbed layout, and that nothing was lost in adopting it.
/// </summary>
/// <remarks>
/// <para>The page went from one long column to a hero band plus three tabs. The risk in that kind
/// of change is not that the new layout looks wrong — that is visible — but that a control ends up
/// in a tab nobody thought to open again, still present, still working, effectively gone. These
/// tests name the things that must remain reachable.</para>
///
/// <para>The consent checkbox's own behaviour is covered by ProfileConsentTests; this only asserts
/// that it is still findable, in a tab, on a page it used to sit halfway down.</para>
/// </remarks>
[TestFixture]
[Category("Profile")]
public class ProfileLayoutTests : BenTestBase
{
    [SetUp]
    public async Task OpenProfile()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "About" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task TheHeroBandCarriesTheAccountsIdentity()
    {
        // The name and the sign-in address, which is what the template's hero shows and what
        // someone opening their own profile is checking they are looking at.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sarah Mitchell" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByText(UserEmail).First).ToBeVisibleAsync();
    }

    [Test]
    public async Task AllThreeTabsArePresent()
    {
        foreach (var name in new[] { "About", "Contact", "Where you've been" })
        {
            await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = name }))
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
    }

    [Test]
    public async Task TheAboutTabStillCarriesTheNameAndTheConsentCheckbox()
    {
        // Both were on the old single column. The consent checkbox in particular is a two-key
        // control someone may go looking for months later.
        await Expect(Page.Locator("input[placeholder='Your name']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.Locator("#share-private")).ToBeVisibleAsync();
    }

    [Test]
    public async Task TheContactTabStillCarriesAllFourKindsOfDetail()
    {
        // ClickUntilAsync, not a bare click: the tab strip renders during SSR, well before the
        // circuit is live, and a click in that window simply evaporates — the page then sits on
        // About while the test waits for Contact's content. Retrying against the first heading
        // is the standard cure for the interactivity race.
        // Substring matches, because each heading now ends in a HelpLink's "?" — the h6's text
        // is "Email addresses?", so an exact match on "Email addresses" finds nothing. The
        // locator went stale when the help pass added the links; the cards never moved.
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Tab, new() { Name = "Contact" }),
            Main.GetByText("Email addresses", new() { Exact = false }).First);

        foreach (var heading in new[] { "Email addresses", "Phone numbers", "Addresses", "Web links" })
        {
            await Expect(Main.GetByText(heading, new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
    }

    /// <summary>
    /// The map tab draws a map — the reason the whole endpoint behind it got fixed.
    /// </summary>
    /// <remarks>
    /// <c>/api/my-investigations/attended</c> was returning 500 for every caller: it ordered by a
    /// property of the record it was projecting into, which EF cannot translate and reports at
    /// runtime. Both pages that call it wrap it in a catch that falls back to an empty list, so
    /// the failure surfaced as "you haven't attended an investigation yet" and the map had been
    /// quietly empty for everyone. Asserting on the map itself is what makes that visible.
    /// </remarks>
    [Test]
    public async Task TheMapTabDrawsTheMap()
    {
        // Retried for the same interactivity race as the Contact tab above.
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Tab, new() { Name = "Where you've been" }),
            Page.Locator(".k-map").First);

        // Sarah has attended investigations with coordinates in the seed data. An empty state here
        // means either the seed changed or the endpoint is failing again.
        await Expect(Page.Locator(".k-map").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var tiles = await Page.Locator(".k-map img").CountAsync();
        Assert.That(tiles, Is.GreaterThan(0), "The map rendered no tiles.");
    }
}
