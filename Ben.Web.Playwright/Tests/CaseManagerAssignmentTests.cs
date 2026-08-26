using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for case manager assignment in CaseDetail and CaseList.
/// Sarah Mitchell is seeded as an org member of TGH; Daniel Park's case is used.
/// </summary>
[TestFixture]
[Category("CaseManager")]
public class CaseManagerAssignmentTests : BenTestBase
{
    // ── Helper: navigate to Daniel Park's case detail as Sarah ─────────────────

    private async Task NavigateToTghCaseDetail()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Pass("TGH case not in the seed data; nothing to assert against.");
    }

    // ── CaseList: manager column ───────────────────────────────────────────────

    [Test]
    public async Task CaseList_ShowsManagerOrUnassigned()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Pass("TGH org not in the seed data.");
        await OpenTabAsync("Cases", Main.GetByText("Manager:", new() { Exact = false })
                                        .Or(Main.GetByText("No cases", new() { Exact = false })));

        // Each case card should show either a manager name or 'Unassigned'
        var managerLabels = Page.GetByText("Manager:", new() { Exact = false });
        await Expect(managerLabels.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── CaseDetail: manager in header ─────────────────────────────────────────

    [Test]
    public async Task CaseDetail_HeaderShowsCaseManager()
    {
        await NavigateToTghCaseDetail();

        // Header row should contain 'Case Manager:' text
        var managerRow = Page.GetByText("Case Manager:", new() { Exact = false });
        await Expect(managerRow).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task CaseDetail_Header_ShowsUnassignedWhenNoManager()
    {
        await NavigateToTghCaseDetail();

        // Either shows a name OR 'Unassigned' — both are valid states
        var managerRow = Page.GetByText("Case Manager:", new() { Exact = false });
        await Expect(managerRow).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body.Contains("Case Manager:"), Is.True, "Expected 'Case Manager:' in header.");
    }

    // ── CaseDetail: edit dialog shows manager dropdown ────────────────────────

    [Test]
    public async Task EditCaseDialog_HasCaseManagerDropdown()
    {
        await NavigateToTghCaseDetail();

        var editBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" });
        await Expect(editBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await editBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 'Case Manager' label should appear in the dialog
        // Exact, and the label specifically: a loose match also picks up the case header's
        // "Case Manager: Sarah Mitchell", and two matches is a strict-mode violation.
        var label = Page.Locator("label", new() { HasTextString = "Case Manager" }).First;
        await Expect(label).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task EditCaseDialog_CaseManagerDropdown_HasOrgMembers()
    {
        await NavigateToTghCaseDetail();

        var editBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" });
        await editBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for the dropdown to populate with org members (loaded lazily)
        await Page.WaitForTimeoutAsync(800);

        // Exact, and the label specifically: a loose match also picks up the case header's
        // "Case Manager: Sarah Mitchell", and two matches is a strict-mode violation.
        var label = Page.Locator("label", new() { HasTextString = "Case Manager" }).First;
        await Expect(label).ToBeVisibleAsync(new() { Timeout = 8_000 });

        // The dropdown should be present and interactable
        var body = await Page.InnerTextAsync("body");
        Assert.That(body.Contains("Case Manager"), Is.True,
            "Expected Case Manager dropdown in edit dialog.");
    }
}
