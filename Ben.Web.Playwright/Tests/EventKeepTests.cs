using System.IO.Compression;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Picking an event's files and taking them away as one zip (item 235 phase 12).
/// </summary>
[TestFixture]
[Category("HostedEvents")]
public class EventKeepTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private string _orgId = string.Empty;
    private readonly string _fileName = $"keep-{Guid.NewGuid():N}"[..13] + ".txt";

    [SetUp]
    public async Task Start() => _orgId = await OrgIdBySlugAsync("paranormal365");

    [TearDown]
    public async Task RemoveWhatThisRunAdded()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        if (!login.Ok) return;
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var list = await api.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/files", new() { Headers = headers });
        if (!list.Ok) return;
        foreach (var file in (await list.JsonAsync())!.Value.EnumerateArray())
            if (file.GetProperty("fileName").GetString() == _fileName)
                await api.DeleteAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/files/{file.GetProperty("id").GetString()}",
                    new() { Headers = headers });
    }

    [Test]
    public async Task A_picked_file_comes_back_inside_the_zip()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/files");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#file-input")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#file-input").SetInputFilesAsync(new FilePayload
        {
            Name = _fileName, MimeType = "text/plain", Buffer = "Doors open at seven."u8.ToArray(),
        });
        await ClickUntilAsync(Page.Locator("#file-upload"), Page.Locator("#files-note"));

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/keep");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#keep-files")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var item = Page.Locator("#keep-files li", new() { HasTextString = _fileName }).Locator("input");
        await ClickUntilAsync(item, Page.Locator("a#keep-download"));

        var download = await Page.RunAndWaitForDownloadAsync(
            () => Page.Locator("a#keep-download").ClickAsync(), new() { Timeout = 30_000 });
        Assert.That(download.SuggestedFilename, Does.EndWith(".zip"));

        using var zip = ZipFile.OpenRead((await download.PathAsync())!);
        var entry = zip.Entries.SingleOrDefault(e => e.FullName == $"Files/{_fileName}");
        Assert.That(entry, Is.Not.Null, string.Join(", ", zip.Entries.Select(e => e.FullName)));
        using var reader = new StreamReader(entry!.Open());
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("Doors open at seven."));
    }
}
