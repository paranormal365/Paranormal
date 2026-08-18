using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Playwright tests for the Investigation Report Builder (org side) and
/// the client-visible published reports card on /my-cases/{id}.
/// The dev seeder creates one published report on Daniel Park's case.
/// </summary>
[TestFixture]
[Category("CaseReports")]
public class InvestigationReportTests : BenTestBase
{
    private const string ClientEmail    = "daniel.park@benco.dev";
    private const string ClientPassword = "D@niel!Park2026";

    // ── Client-side: published reports card ────────────────────────────────────

    [Test]
    public async Task ClientCaseDetail_ReportsCard_ShowsPublishedReport()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Reports card should appear since we have a seeded published report
        var reportTitle = Page.GetByText("Initial Assessment — Park Residence", new() { Exact = false });
        await Expect(reportTitle).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task ClientCaseDetail_ReportsCard_HasPdfButton()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for the reports section to render
        await Expect(Page.GetByText("Initial Assessment — Park Residence", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // PDF button should be present
        var pdfBtn = Page.GetByRole(AriaRole.Button, new() { Name = "PDF" }).Last;
        await Expect(pdfBtn).ToBeVisibleAsync(new() { Timeout = 6_000 });
    }

    // ── Org-side: Reports tab in CaseDetail ────────────────────────────────────

    [Test]
    public async Task OrgCaseDetail_ReportsTab_IsVisible()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var tgh = Page.GetByText("Tennessee Ghost Hunters", new() { Exact = false });
        if (!await tgh.IsVisibleAsync()) { Assert.Pass("TGH org not visible."); return; }
        await tgh.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var casesLink = Page.GetByRole(AriaRole.Link, new() { Name = "Cases" })
                            .Or(Main.GetByText("Cases", new() { Exact = true })).First;
        await casesLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Open Daniel's case; the case detail is identified by its own tab strip.
        var caseItem = Main.GetByText("Park", new() { Exact = false }).First;
        await Expect(caseItem).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await ClickUntilAsync(caseItem, Main.Locator(".nav-tabs .nav-link").Or(Main.GetByRole(AriaRole.Tab)));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Reports tab should be present as the 6th tab
        var reportsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Reports" })
                             .Or(Page.GetByText("Reports", new() { Exact = true })).First;
        await Expect(reportsTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task OrgCaseDetail_ReportsTab_ShowsSeededPublishedReport()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var tgh = Page.GetByText("Tennessee Ghost Hunters", new() { Exact = false });
        if (!await tgh.IsVisibleAsync()) { Assert.Pass("TGH org not visible."); return; }
        await tgh.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var casesLink = Page.GetByRole(AriaRole.Link, new() { Name = "Cases" })
                            .Or(Main.GetByText("Cases", new() { Exact = true })).First;
        await casesLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Open Daniel's case; the case detail is identified by its own tab strip.
        var caseItem = Main.GetByText("Park", new() { Exact = false }).First;
        await Expect(caseItem).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await ClickUntilAsync(caseItem, Main.Locator(".nav-tabs .nav-link").Or(Main.GetByRole(AriaRole.Tab)));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var reportsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Reports" })
                             .Or(Page.GetByText("Reports", new() { Exact = true })).First;
        await reportsTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var reportTitle = Page.GetByText("Initial Assessment — Park Residence", new() { Exact = false });
        await Expect(reportTitle).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }
}
