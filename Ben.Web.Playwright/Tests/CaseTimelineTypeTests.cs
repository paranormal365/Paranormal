using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Research is not offered as a kind of new timeline entry; entries already written as research still show and can
/// still be picked out, and instrument readings can be picked out at last.
/// </summary>
/// <remarks>
/// Beta feedback, 2026-09-14: research has its own tab, and its pages carry their own date and time. The seeded demo
/// cases each have one legacy research entry on their timeline, which is what proves the old rows survive.
/// </remarks>
[TestFixture]
[Category("CaseManagement")]
public class CaseTimelineTypeTests : BenTestBase
{
    private async Task OpenFirstSeededCaseTimelineAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var orgId = await OrgIdBySlugAsync("benco");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cases");
        await WaitForTheCircuitAsync();

        var open = Main.Locator(".card").GetByRole(AriaRole.Button, new() { Name = "Open" }).First;
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilUrlAsync(open, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");

        await Page.GotoAsync(Page.Url.Split('?')[0] + "?tab=timeline");
        await WaitForTheCircuitAsync();
        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Add Entry" })).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task AddEntry_DoesNotOfferResearch()
    {
        await OpenFirstSeededCaseTimelineAsync();

        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Add Entry" }),
            Page.Locator(".modal").Filter(new() { HasTextString = "Add Timeline Entry" }));

        var modal = Page.Locator(".modal").Filter(new() { HasTextString = "Add Timeline Entry" });
        var options = await modal.Locator("select").First.Locator("option").AllInnerTextsAsync();
        var names = options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();

        Assert.That(names, Does.Not.Contain("Research"), "Research is still offered for a new timeline entry");
        Assert.That(names, Does.Contain("Investigator note"));
        Assert.That(names, Does.Contain("Instrument reading"));
    }

    [Test]
    public async Task FilterRow_KeepsResearch_AndAddsInstrumentReading()
    {
        await OpenFirstSeededCaseTimelineAsync();

        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Research", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Instrument reading", Exact = true }))
            .ToBeVisibleAsync();
    }
}
