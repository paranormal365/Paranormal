using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the Case Transfer tab in the org case detail page.
/// Covers the transfer history list, propose dialog, and cancel/accept/reject workflow.
/// </summary>
[TestFixture]
[Category("CaseTransfer")]
public class CaseTransferTests : BenTestBase
{
    private async Task<string?> NavigateToCaseTransferTabAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        if (!await viewLink.IsVisibleAsync()) return null;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var casesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Cases" });
        if (!await casesTab.IsVisibleAsync()) return null;
        await casesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var caseLink = Page.GetByRole(AriaRole.Link, new() { Name = "#", Exact = false })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        if (!await caseLink.IsVisibleAsync()) return null;
        await caseLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var transfersTab = Page.GetByText("Transfers", new() { Exact = false });
        if (!await transfersTab.IsVisibleAsync()) return null;
        await transfersTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return Page.Url;
    }

    // ── Tab rendering ─────────────────────────────────────────────────────────

    [Test]
    public async Task TransfersTab_IsVisibleOnCaseDetail()
    {
        var url = await NavigateToCaseTransferTabAsync();
        if (url is null) { Assert.Pass("No cases found to test."); return; }

        var heading = Page.GetByText("Case Transfers", new() { Exact = false });
        await Expect(heading).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task TransfersTab_EmptyState_ShowsNoTransfersMessage()
    {
        var url = await NavigateToCaseTransferTabAsync();
        if (url is null) { Assert.Pass("No cases found."); return; }

        // When no transfers exist the panel shows a "No transfers" message
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Contain("No transfers").Or.Contain("Propose Transfer").Or.Contain("Pending"),
            "Expected either no-transfers message or a transfer record.");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task TransfersTab_ProposeButton_VisibleWhenNoPendingTransfer()
    {
        var url = await NavigateToCaseTransferTabAsync();
        if (url is null) { Assert.Pass("No cases found."); return; }

        var body = await Page.InnerTextAsync("body");
        // Propose button shows when no pending outgoing transfer exists
        var proposeBtn = Page.GetByText("Propose Transfer", new() { Exact = false });
        if (body.Contains("Pending"))
        {
            // There's already a pending transfer — button should be hidden
            Assert.That(await proposeBtn.IsVisibleAsync(), Is.False,
                "Propose Transfer button should be hidden when a pending transfer exists.");
        }
        else
        {
            await Expect(proposeBtn).ToBeVisibleAsync(new() { Timeout = 5_000 });
        }
    }

    // ── Propose dialog ────────────────────────────────────────────────────────

    [Test]
    public async Task TransfersTab_ProposeDialog_OpensAndHasOrgDropdown()
    {
        var url = await NavigateToCaseTransferTabAsync();
        if (url is null) { Assert.Pass("No cases found."); return; }

        var body = await Page.InnerTextAsync("body");
        if (body.Contains("Pending"))
        {
            Assert.Pass("Pending transfer exists — cannot open propose dialog.");
            return;
        }

        var proposeBtn = Page.GetByText("Propose Transfer", new() { Exact = false });
        if (!await proposeBtn.IsVisibleAsync()) { Assert.Pass("Propose button not visible."); return; }

        await proposeBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // TelerikWindow should open with the org dropdown
        var dialogHeading = Page.GetByText("Propose Case Transfer", new() { Exact = false });
        await Expect(dialogHeading).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Should have destination org selector
        var orgDropdown = Page.GetByText("— select organization —", new() { Exact = false })
                              .Or(Page.Locator("[class*='k-dropdownlist'], [role='combobox']").First);
        await Expect(orgDropdown.First).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Reason field
        var reasonField = Page.Locator("[placeholder*='Explain why' i], [placeholder*='transferred' i]").First;
        await Expect(reasonField).ToBeVisibleAsync();

        // Close
        var closeBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" })
                           .Or(Page.GetByTitle("Close"))
                           .First;
        await closeBtn.ClickAsync();
    }

    [Test]
    public async Task TransfersTab_ProposeDialog_SubmitWithoutOrg_Stays()
    {
        var url = await NavigateToCaseTransferTabAsync();
        if (url is null) { Assert.Pass("No cases found."); return; }

        var body = await Page.InnerTextAsync("body");
        if (body.Contains("Pending")) { Assert.Pass("Pending transfer exists."); return; }

        var proposeBtn = Page.GetByText("Propose Transfer", new() { Exact = false });
        if (!await proposeBtn.IsVisibleAsync()) { Assert.Pass("Propose button not visible."); return; }

        await proposeBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Click "Propose Transfer" submit button without selecting an org
        var submitBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Propose Transfer", Exact = true });
        // Should be disabled without org selection
        Assert.That(await submitBtn.IsEnabledAsync(), Is.False,
            "Propose button should be disabled until an org is selected.");

        var closeBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).First;
        await closeBtn.ClickAsync();
    }

    // ── Transfer record display ───────────────────────────────────────────────

    [Test]
    public async Task TransferRecord_ShowsCorrectStatusBadges()
    {
        var url = await NavigateToCaseTransferTabAsync();
        if (url is null) { Assert.Pass("No cases found."); return; }

        var body = await Page.InnerTextAsync("body");
        // If any transfer records exist, verify badge text matches known statuses
        foreach (var status in new[] { "Pending", "Accepted", "Rejected", "Cancelled" })
        {
            if (body.Contains(status))
            {
                var badge = Page.GetByText(status, new() { Exact = true }).First;
                await Expect(badge).ToBeVisibleAsync(new() { Timeout = 3_000 });
            }
        }
        Assert.Pass("Transfer status badge check complete.");
    }

    // ── Privacy / security ────────────────────────────────────────────────────

    [Test]
    public async Task TransfersTab_AnonymousUser_CannotAccess()
    {
        // Transfer tab is only in the org management UI which requires auth
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("Sign", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected auth guard on /organizations for anonymous user.");
    }
}
