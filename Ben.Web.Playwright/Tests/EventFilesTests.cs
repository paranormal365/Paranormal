using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// An event's files, added by the host and seen by exactly who they are for (item 235 phase 11).
/// </summary>
/// <remarks>
/// Against the seeded rooms weekend. The file this run adds has a name no earlier run used, and is
/// removed at the end whatever happened.
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventFilesTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private string _orgId = "";
    private readonly string _fileName = $"poster-{Guid.NewGuid():N}"[..15] + ".txt";

    [SetUp]
    public async Task Start()
    {
        _orgId = await OrgIdBySlugAsync("paranormal365");

        // The seed leaves this event in Draft on purpose, and the public endpoints below
        // answer only for a published one. This fixture used to inherit a publish from
        // whichever earlier run happened to do it (item 243); it does its own now.
        await PublishSeededEventAsync(_orgId, RoomsEventId);
    }

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
    public async Task A_public_file_reaches_a_visitor_and_a_staff_file_does_not()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/files");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#file-input")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#file-input").SetInputFilesAsync(new FilePayload
        {
            Name = _fileName, MimeType = "text/plain", Buffer = "Doors open at seven."u8.ToArray(),
        });
        await Page.Locator("#file-folder").FillAsync("Posters");
        await Page.Locator("#file-audience").SelectOptionAsync(new SelectOptionValue { Label = "Anybody" });
        await ClickUntilAsync(Page.Locator("#file-upload"), Page.Locator("#files-note"));

        var row = Page.Locator(".event-file", new() { HasTextString = _fileName });
        await Expect(row).ToBeVisibleAsync();

        // Signed out, the event page offers it.
        var slug = await SlugAsync();
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{slug}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator(".public-event-file", new() { HasTextString = _fileName })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Made staff-only, it is gone for the same visitor.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/files");
        await WaitUntilLoadedAsync();
        await Page.Locator(".event-file", new() { HasTextString = _fileName }).Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = "The event's people only" });
        await Expect(Page.Locator("#files-note")).ToContainTextAsync("people only", new() { Timeout = 15_000 });

        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{slug}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#event-title, h1").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(1500);
        await Expect(Page.Locator(".public-event-file", new() { HasTextString = _fileName })).ToHaveCountAsync(0);
    }

    private async Task<string> SlugAsync()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var ev = await api.GetAsync($"/api/public/hosted-events/{RoomsEventId}");
        Assert.That(ev.Ok, Is.True, "the seeded rooms weekend is not on the public site");
        return (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString()!;
    }
}
