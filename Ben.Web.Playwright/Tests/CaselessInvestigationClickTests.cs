using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A click on an investigation must always show something (Ben, 2026-08-22). Case-bound rows open
/// their case page; case-less rows — internal visits with no client case — used to hit a silent
/// <c>return;</c> and do nothing at all. They now open the group's Investigations tab with the
/// row highlighted, scrolled to, and its team panel already open.
/// Uses the seeded "Bell Witch Cave — follow-up (internal)" visit (DevelopmentDataSeeder), which
/// the SuperAdmin attends and which has CaseId = null.
/// </summary>
[TestFixture]
[Category("CaselessInvestigationClick")]
public class CaselessInvestigationClickTests : BenTestBase
{
    private const string CaselessTitle = "Bell Witch Cave — follow-up (internal)";

    private static readonly Regex OrgInvestigationsUrl = new(
        @"/organizations/[0-9a-f-]{36}\?tab=investigations&inv=[0-9a-f-]{36}",
        RegexOptions.IgnoreCase);

    [Test]
    public async Task MyInvestigations_CaselessCard_OpensGroupInvestigationsTab()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/my-investigations");
        await WaitUntilLoadedAsync();

        var card = Page.Locator(".card", new() { HasText = CaselessTitle }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The title, not the card: a card's centre can sit on its RSVP strip, which swallows
        // clicks by design (stopPropagation), and that would read as the very bug under test.
        await ClickUntilUrlAsync(card.GetByText(CaselessTitle), @"/organizations/[0-9a-f-]{36}\?tab=investigations&inv=");
        Assert.That(OrgInvestigationsUrl.IsMatch(Page.Url),
            $"Expected the group's Investigations tab with a focused row, got {Page.Url}");

        // The tab is actually selected, and the clicked visit is on it, highlighted, with its
        // team open — "show something" means the row is unmissable, not merely present.
        var invTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Investigations" });
        await Expect(invTab).ToHaveClassAsync(new Regex("active"), new() { Timeout = 15_000 });

        var focusedRow = Page.Locator("tr.table-active", new() { HasText = CaselessTitle });
        await Expect(focusedRow).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(focusedRow).ToBeInViewportAsync(new() { Timeout = 10_000 });

        // The auto-opened roster names the SuperAdmin, who attended this visit.
        await Expect(Page.Locator("tr", new() { HasText = "Lead" }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task OrgInvestigationsTab_DeepLink_SurvivesColdNavigation()
    {
        // The URL the card navigates to must also work pasted into a fresh tab — a hard
        // navigation renders the page before the circuit exists, which is exactly when a
        // tab-selection race would lose the ?tab= parameter.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/my-investigations");
        await WaitUntilLoadedAsync();

        var card = Page.Locator(".card", new() { HasText = CaselessTitle }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ClickUntilUrlAsync(card.GetByText(CaselessTitle), @"\?tab=investigations&inv=");
        var deepLink = Page.Url;

        await Page.GotoAsync(deepLink);
        await WaitUntilLoadedAsync();

        var focusedRow = Page.Locator("tr.table-active", new() { HasText = CaselessTitle });
        await Expect(focusedRow).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task MyInvestigations_EveryCard_NavigatesSomewhere()
    {
        // The general rule behind the specific fix: no investigation card is a dead end. Click
        // each card on the page and require the URL to change every time.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/my-investigations");
        await WaitUntilLoadedAsync();

        var cards = Page.Locator("div.card[style*='cursor:pointer']");
        var count = await cards.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "The seeded SuperAdmin should have investigation cards.");

        for (var i = 0; i < count; i++)
        {
            var before = Page.Url;
            await ClickUntilUrlAsync(cards.Nth(i).Locator(".fw-semibold").First, @"/organizations/[0-9a-f-]{36}");
            Assert.That(Page.Url, Is.Not.EqualTo(before),
                $"Card {i} did not navigate — a click must always show something.");

            await Page.GotoAsync($"{BaseUrl}/my-investigations");
            await WaitUntilLoadedAsync();
            cards = Page.Locator("div.card[style*='cursor:pointer']");
        }
    }
}
