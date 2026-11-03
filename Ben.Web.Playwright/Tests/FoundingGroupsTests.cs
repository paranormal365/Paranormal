using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The founding groups promotion, as visitors and a group's owner see it (Ben, 11/02/2026): the home page's
/// shout-out, the pricing notice and the rules while it's open, and claiming a spot for a group.
/// </summary>
/// <remarks>
/// The promotion is opened for the test and switched off again after it, so nothing else in the run sees the
/// notices. The week, the free plan and the prizes are proved in <c>FoundingGroupsTests</c> on the server.
/// </remarks>
[TestFixture]
[Category("FoundingGroups")]
public class FoundingGroupsTests : BenTestBase
{
    private IAPIRequestContext? _admin;
    private JsonElement _settings;

    private async Task<IAPIRequestContext> ApiAsync(string email, string password)
    {
        var anon = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await anon.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in to the API");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    private async Task SetOpenAsync(bool open)
    {
        var s = JsonSerializer.Deserialize<Dictionary<string, object>>(_settings.GetRawText())!;
        s["isOpen"] = open;
        var saved = await _admin!.PutAsync("/api/admin/founding-groups", new() { DataObject = s });
        Assert.That(saved.Ok, Is.True, "the promotion could not be switched: " + await saved.TextAsync());
    }

    [SetUp]
    public async Task OpenThePromotion()
    {
        _admin = await ApiAsync(SuperAdminEmail, SuperAdminPassword);
        var got = await _admin.GetAsync("/api/admin/founding-groups");
        Assert.That(got.Ok, Is.True, await got.TextAsync());
        _settings = (await got.JsonAsync())!.Value.GetProperty("settings");
        await SetOpenAsync(true);
    }

    [TearDown]
    public async Task CloseThePromotion()
    {
        if (_admin is not null) await SetOpenAsync(false);
    }

    [TestCase(1280, 800)]
    [TestCase(375, 812)]
    public async Task Visitors_see_the_shout_out_the_pricing_notice_and_the_rules(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);

        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();
        var shout = Page.Locator("[data-testid='founding-shout']");
        await Expect(shout).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(shout.Locator("[data-testid='founding-shout-left']")).ToContainTextAsync("spots left");
        await Expect(shout.Locator("[data-testid='founding-lanterns'] svg").First).ToBeVisibleAsync();
        Assert.That(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1"), Is.False,
            "the home page scrolled sideways");

        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("[data-testid='founding-notice']")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Page.GotoAsync($"{BaseUrl}/founding-groups/rules");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("[data-testid='founding-rules']")).ToContainTextAsync("confirmed their email address",
            new() { Timeout = 30_000 });

        await Page.GotoAsync($"{BaseUrl}/founding-groups");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("[data-testid='founding-plans']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1"), Is.False,
            "the promotion page scrolled sideways");
    }

    [Test]
    public async Task A_group_owner_claims_a_spot_and_sees_who_counts()
    {
        var orgId = await OrgIdBySlugAsync("benco");
        var owner = await ApiAsync(UserEmail, UserPassword);
        var claimed = await owner.PostAsync($"/api/organizations/{orgId}/founding-group/claim");
        // A database a previous run used may already have the claim.
        Assert.That(claimed.Ok || (await claimed.TextAsync()).Contains("already taken part"), Is.True, await claimed.TextAsync());

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/founding-group");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#founding-gathering, #founding-qualified, #founding-released").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        // Sarah also runs another group, so the page says why she doesn't count rather than leaving her off.
        await Expect(Page.Locator("#founding-members")).ToContainTextAsync("is also a member of another group");
        await Expect(Page.Locator("[data-testid='group-referral']")).ToBeVisibleAsync();

        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/billing");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("[data-testid='group-referral']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
