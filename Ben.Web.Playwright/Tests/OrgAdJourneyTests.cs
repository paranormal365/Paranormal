using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 166 W3: the whole ad life — an administrator writes the card through the teaching
/// wizard, a SuperAdmin approves it, and an ANONYMOUS visitor sees it on /find marked
/// Promoted (the authors-see-what-visitors-cannot rule: the check that matters is the
/// signed-out one). The ad is deleted in finally; the shared database keeps nothing.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("OrgAdJourney")]
public class OrgAdJourneyTests : BenTestBase
{
    private const string TghId = "881ea0f6-8c0d-475e-9065-c6ed15e3302f";

    [Test]
    public async Task An_ad_travels_wizard_review_and_lands_on_the_anonymous_find_page()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var headline = $"E2E Night Watch {suffix}";
        var auth = await SuperAdminAuthAsync();

        await CleanupAdsAsync(auth);   // ENSURE a clean start; a dead run may have left one

        try
        {
            // ── The group's administrator writes the card ─────────────────────
            await LoginAsync(UserEmail, UserPassword);   // Sarah — TGH administrator
            await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}/promote");
            await WaitUntilLoadedAsync();

            var headlineInput = Main.Locator("#ad-headline");
            await Expect(headlineInput).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await headlineInput.FillAsync(headline);
            await ClickUntilAsync(
                Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
                Main.Locator("#ad-body"));

            await Main.Locator("#ad-body").FillAsync("Free investigations across middle Tennessee. Ask us over.");
            await Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();

            await Expect(Main.Locator("#ad-choose-image")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();

            await Expect(Main.Locator("#ad-target")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();

            await Expect(Main.Locator("#ad-review")).ToContainTextAsync(headline);
            await Main.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();

            await Expect(Main.Locator("#promote-status")).ToContainTextAsync("In review",
                new() { Timeout = 45_000 });

            // ── Not approved yet: the anonymous placements must not know it ───
            var before = await Page.APIRequest.GetAsync("http://localhost:5252/api/public/promoted-groups?take=10");
            Assert.That((await before.TextAsync()).Contains(headline), Is.False,
                "An unapproved ad reached the public endpoint.");

            // ── The SuperAdmin approves ───────────────────────────────────────
            var ads = await Page.APIRequest.GetAsync("http://localhost:5252/api/admin/organization-ads",
                new() { Headers = auth });
            string? adId = null;
            foreach (var ad in (await ads.JsonAsync())!.Value.EnumerateArray())
                if (ad.GetProperty("headline").GetString() == headline)
                    adId = ad.GetProperty("id").GetString();
            Assert.That(adId, Is.Not.Null, "The submitted ad never reached the review queue.");

            var approved = await Page.APIRequest.PostAsync(
                $"http://localhost:5252/api/admin/organization-ads/{adId}/approve",
                new() { Headers = auth, DataObject = new { } });
            Assert.That(approved.Ok, "Approving failed.");

            // ── The anonymous visitor sees it on /find, marked Promoted ───────
            await LogoutAsync();
            await Page.GotoAsync($"{BaseUrl}/find");
            await WaitUntilLoadedAsync();
            var card = Page.Locator(".promoted-group-card");
            await Expect(card).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Expect(card).ToContainTextAsync(headline);
            await Expect(card.GetByText("Promoted", new() { Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            await CleanupAdsAsync(auth);
        }
    }

    private async Task<Dictionary<string, string>> SuperAdminAuthAsync()
    {
        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, "SuperAdmin API login failed.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }

    private async Task CleanupAdsAsync(Dictionary<string, string> auth)
    {
        var ads = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/organizations/{TghId}/ads", new() { Headers = auth });
        if (!ads.Ok) return;
        foreach (var ad in (await ads.JsonAsync())!.Value.EnumerateArray())
            await Page.APIRequest.DeleteAsync(
                $"http://localhost:5252/api/organizations/{TghId}/ads/{ad.GetProperty("id").GetString()}",
                new() { Headers = auth });
    }
}
