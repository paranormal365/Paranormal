using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the file upload page (<c>/upload-files</c>).
/// Requires authentication.
/// </summary>
[TestFixture]
[Category("UploadFiles")]
public class UploadFilesTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(UserEmail, UserPassword);

    [Test]
    public async Task Page_RendersFileList()
    {
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Is.Not.Empty);
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task Page_HasUploadButton()
    {
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var uploadBtn = Page.GetByText("Upload", new() { Exact = false })
                            .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Upload" }))
                            .First;
        await Expect(uploadBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task Page_AnonymousRedirectsToLogin()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("Sign", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected redirect to login for unauthenticated file upload access.");
    }

    [Test]
    public async Task Page_HasFileTypeDropdown()
    {
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // The upload form is collapsed until "Upload New File" is pressed, so the File Type control
        // does not exist on arrival. The test asserted on it without opening the panel, which is
        // why it reported the control missing on a page that renders it perfectly well.
        var openPanel = Page.GetByRole(AriaRole.Button, new() { Name = "Upload New File" });
        await ClickUntilAsync(openPanel, Main.GetByText("New Upload", new() { Exact = false }));

        // File type select — Telerik renders as a custom dropdown
        var fileTypeEl = Main.GetByText("File Type", new() { Exact = false })
                             .Or(Main.Locator("[aria-label*='file type' i]"))
                             .First;
        await Expect(fileTypeEl).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── Delete (item 180 Phase B) ─────────────────────────────────────────────

    /// <summary>
    /// A file nobody else is using gets the plain confirm and goes. The two-question dialog for
    /// a file a group is using needs a share and a group, which this account's fixture does not
    /// carry; that path is covered by the controller tests, and this one proves the page's
    /// delete button still reaches the server through the new flow.
    /// </summary>
    [Test]
    public async Task Delete_AFileNobodyElseUses_AsksOnce_ThenRemovesIt()
    {
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var openPanel = Page.GetByRole(AriaRole.Button, new() { Name = "Upload New File" });
        await ClickUntilAsync(openPanel, Main.GetByText("New Upload", new() { Exact = false }));

        var name = $"e2e-delete-{Guid.NewGuid():N}.txt";
        await Page.Locator("input[type='file']").First.SetInputFilesAsync(new FilePayload
        {
            Name = name, MimeType = "text/plain", Buffer = System.Text.Encoding.UTF8.GetBytes("delete me"),
        });
        var uploadButton = Main.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true })
                               .Or(Main.GetByRole(AriaRole.Button, new() { Name = "Upload Files" })).First;
        await uploadButton.ClickAsync();
        await Expect(Main.GetByText(name)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The row's Delete is a Telerik command button; click until the confirm shows.
        var row = Page.Locator("tr", new() { HasTextString = name }).First;
        var deleteButton = row.GetByRole(AriaRole.Button, new() { Name = "Delete" })
                              .Or(row.Locator("button[title='Delete'], button:has(.k-i-trash), button:has(.k-svg-i-trash)")).First;
        await ClickUntilAsync(deleteButton, Page.GetByText("Delete File", new() { Exact = false }));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).Last.ClickAsync();
        await Expect(Main.GetByText(name)).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
