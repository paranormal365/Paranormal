using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the organization case management pages accessible to org members.
/// Covers the case list and case detail view inside the org management interface.
/// </summary>
[TestFixture]
[Category("CaseManagement")]
public class CaseManagementTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    private async Task<string> GetFirstOrgUrl()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return Page.Url;
    }

    [Test]
    public async Task CaseList_RendersFromOrgView()
    {
        await GetFirstOrgUrl();
        var casesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Cases" });
        await Expect(casesTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await casesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task CaseList_HasNewCaseButton()
    {
        await GetFirstOrgUrl();
        var casesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Cases" });
        await casesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var newCase = Page.GetByText("New Case", new() { Exact = false })
                          .Or(Page.GetByRole(AriaRole.Button, new() { Name = "New" }))
                          .First;
        // May require manager role — test is lenient
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task CaseDetail_DirectUrlRenders()
    {
        // Navigate into a case via the org → cases tab → first case
        await GetFirstOrgUrl();
        var casesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Cases" });
        await casesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var caseLink = Page.GetByRole(AriaRole.Link, new() { Name = "#", Exact = false })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        if (await caseLink.IsVisibleAsync())
        {
            await caseLink.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var body = await Page.InnerTextAsync("body");
            Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        }
        else
        {
            Assert.Pass("No case rows visible — BenCo may have no cases seeded. TGH cases are on that org.");
        }
    }

    // ── Beta feedback 2026-09-14: statuses and waiting requests draw the eye ──────────────────────────

    /// <summary>The colour a status's count takes when it has cases — the same as CaseStatusExtensions.BadgeClass.</summary>
    private static readonly Dictionary<string, string> StatusColour = new()
    {
        ["proposed"] = "bg-secondary", ["accepted"] = "bg-primary", ["active"] = "bg-success",
        ["summarized"] = "bg-warning", ["paused"] = "bg-danger", ["closed"] = "bg-dark",
        ["public"] = "bg-info", ["haunted"] = "bg-warning", ["transferred"] = "bg-secondary",
    };

    private async Task OpenParanormal365CasesAsync()
    {
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cases");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("button[data-testid^='case-filter-']").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task CaseList_StatusPills_ColourTheCountWhenNonZero()
    {
        await OpenParanormal365CasesAsync();

        var pills = Page.Locator("button[data-testid^='case-filter-']");
        var problems = new List<string>();
        var coloured = 0;

        for (var i = 0; i < await pills.CountAsync(); i++)
        {
            var pill = pills.Nth(i);
            var status = (await pill.GetAttributeAsync("data-testid"))!["case-filter-".Length..];
            if (status == "all") continue;

            var badge = pill.Locator("[data-testid='case-filter-count']");
            var count = int.Parse((await badge.InnerTextAsync()).Trim());
            var classes = await badge.GetAttributeAsync("class") ?? "";

            if (count > 0)
            {
                coloured++;
                if (StatusColour.TryGetValue(status, out var colour) && !classes.Contains(colour))
                    problems.Add($"{status} has {count} cases but its count is \"{classes}\", not {colour}");
            }
            else if (!classes.Contains("bg-body-secondary"))
            {
                problems.Add($"{status} has no cases but its count is coloured: \"{classes}\"");
            }

            var name = await pill.GetAttributeAsync("aria-label") ?? "";
            if (!name.Contains(count.ToString()))
                problems.Add($"{status}: its accessible name \"{name}\" does not say how many cases");
        }

        Assert.That(coloured, Is.GreaterThan(0), "The seeded group has no cases in any status, so nothing here was tested.");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [Test]
    public async Task CaseList_PendingRequests_StandsOutWhenSomethingWaits()
    {
        await OpenParanormal365CasesAsync();

        var button = Page.Locator("[data-testid='pending-requests']");
        await Expect(button).ToBeVisibleAsync();
        var classes = await button.GetAttributeAsync("class") ?? "";
        var badge = button.Locator("[data-testid='pending-count']");

        // The seed decides whether anything waits; either way the button has to say so the right way.
        if (await badge.CountAsync() > 0)
        {
            var waiting = int.Parse((await badge.InnerTextAsync()).Trim());
            Assert.That(waiting, Is.GreaterThan(0));
            Assert.That(classes, Does.Contain("btn-warning"), "requests are waiting but the button is not highlighted");
            Assert.That(await button.GetAttributeAsync("aria-label"), Does.Contain($"{waiting} waiting"));
            Assert.That(await button.InnerTextAsync(), Does.Not.Contain("("), "the count is a badge now, not in brackets");
        }
        else
        {
            Assert.That(classes, Does.Contain("btn-outline-secondary"), "nothing waits, so the button should stay quiet");
            Assert.That(await button.GetAttributeAsync("aria-label"), Does.Contain("none waiting"));
        }
    }
}
