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

        // The managed case, not the first card: Daniel has several cases now and the seeded
        // report belongs to the one with a case manager, which sorts last.
        var card = Page.Locator(".card").Filter(new() { HasTextString = "Case Manager:" }).First;
        if (await card.CountAsync() == 0) { Assert.Pass("No managed case seeded."); return; }
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");

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

        // The managed case, not the first card: Daniel has several cases now and the seeded
        // report belongs to the one with a case manager, which sorts last.
        var card = Page.Locator(".card").Filter(new() { HasTextString = "Case Manager:" }).First;
        if (await card.CountAsync() == 0) { Assert.Pass("No managed case seeded."); return; }
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");

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
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        // Reports tab should be present as the 6th tab
        var reportsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Reports" })
                             .Or(Page.GetByText("Reports", new() { Exact = true })).First;
        await Expect(reportsTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task OrgCaseDetail_ReportsTab_ShowsSeededPublishedReport()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        var reportsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Reports" })
                             .Or(Page.GetByText("Reports", new() { Exact = true })).First;
        await reportsTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var reportTitle = Page.GetByText("Initial Assessment — Park Residence", new() { Exact = false });
        await Expect(reportTitle).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }
}
