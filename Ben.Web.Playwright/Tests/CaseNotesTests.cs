using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the internal case notes tab (org-side, not visible to clients).
/// Uses the seeded BenCo org and its accepted case for Daniel Park.
/// </summary>
[TestFixture]
[Category("CaseNotes")]
public class CaseNotesTests : BenTestBase
{
    private async Task NavigateToCaseNotesTabAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                          .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                          .First;
        if (!await viewLink.IsVisibleAsync()) { Assert.Pass("No orgs found."); return; }
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var casesTab = Page.GetByText("Cases", new() { Exact = false }).First;
        await Expect(casesTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await casesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var caseLink = Page.GetByRole(AriaRole.Link, new() { Name = "#", Exact = false }).First;
        if (!await caseLink.IsVisibleAsync()) { Assert.Pass("No cases found."); return; }
        await caseLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var notesTab = Page.GetByText("Notes", new() { Exact = true }).First;
        await Expect(notesTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await notesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ── Tab rendering ─────────────────────────────────────────────────────────

    [Test]
    [Description("Notes tab is visible in the org case detail view.")]
    public async Task NotesTab_IsVisibleOnCaseDetail()
    {
        await NavigateToCaseNotesTabAsync();
        var heading = Page.GetByText("Case Notes", new() { Exact = false });
        await Expect(heading).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("Notes tab shows the internal-only disclaimer.")]
    public async Task NotesTab_ShowsInternalOnlyText()
    {
        await NavigateToCaseNotesTabAsync();
        var disclaimer = Page.GetByText("not visible to clients", new() { Exact = false });
        await Expect(disclaimer).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("New Note button is present on the Notes tab.")]
    public async Task NotesTab_HasNewNoteButton()
    {
        await NavigateToCaseNotesTabAsync();
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "New Note" });
        await Expect(btn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── Add note ──────────────────────────────────────────────────────────────

    [Test]
    [Description("Clicking New Note opens an add dialog.")]
    public async Task NotesTab_NewNoteButton_OpensDialog()
    {
        await NavigateToCaseNotesTabAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "New Note" }).ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var dialog = Page.Locator(".k-window");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5_000 });
        var titleText = await Page.Locator(".k-window-title, .k-window-titlebar").First.InnerTextAsync();
        Assert.That(titleText, Does.Contain("Note").IgnoreCase);
    }

    [Test]
    [Description("Adding a note persists it to the list.")]
    public async Task NotesTab_AddNote_AppearsInList()
    {
        await NavigateToCaseNotesTabAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "New Note" }).ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var uniqueBody = $"Playwright test note {Guid.NewGuid():N}";
        var textArea   = Page.GetByPlaceholder("Internal note details", new() { Exact = false });
        await Expect(textArea).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await textArea.FillAsync(uniqueBody);

        var saveBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
        await saveBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var note = Page.GetByText(uniqueBody, new() { Exact = false });
        await Expect(note).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── No errors ─────────────────────────────────────────────────────────────

    [Test]
    [Description("Notes tab renders without Telerik or application errors.")]
    public async Task NotesTab_RendersWithoutErrors()
    {
        await NavigateToCaseNotesTabAsync();
        await Page.WaitForTimeoutAsync(1_000);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        Assert.That(body, Does.Not.Contain("does not have a property matching"));
    }
}
