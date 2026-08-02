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
        // File type select — Telerik renders as a custom dropdown
        var fileTypeEl = Page.GetByText("File Type", new() { Exact = false })
                             .Or(Page.Locator("[aria-label*='file type' i]"))
                             .First;
        await Expect(fileTypeEl).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
