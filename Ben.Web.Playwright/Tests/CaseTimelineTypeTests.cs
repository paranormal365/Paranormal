using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Every kind of timeline entry can be added, research included, and every kind can be filtered for.
/// </summary>
/// <remarks>
/// Research came off the list on 2026-09-14, when research pages carried their own dates, and went back on
/// 2026-09-16 when those pages were retired for boards. A board is not a dated moment, so a note about what the
/// deeds said needs the timeline again.
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
    public async Task AddEntry_OffersEveryKindIncludingResearch()
    {
        await OpenFirstSeededCaseTimelineAsync();

        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Add Entry" }),
            Page.Locator(".modal").Filter(new() { HasTextString = "Add Timeline Entry" }));

        var modal = Page.Locator(".modal").Filter(new() { HasTextString = "Add Timeline Entry" });
        var options = await modal.Locator("select").First.Locator("option").AllInnerTextsAsync();
        var names = options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();

        Assert.That(names, Does.Contain("Research"), "Research is no longer offered for a new timeline entry");
        Assert.That(names, Does.Contain("Investigator note"));
        Assert.That(names, Does.Contain("Instrument reading"));
    }

    [Test]
    public async Task FilterRow_OffersResearchAndInstrumentReading()
    {
        await OpenFirstSeededCaseTimelineAsync();

        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Research", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Instrument reading", Exact = true }))
            .ToBeVisibleAsync();
    }
}
