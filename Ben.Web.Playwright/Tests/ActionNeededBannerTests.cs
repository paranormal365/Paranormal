using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 161: a waiting membership application shows an action-needed banner under the
/// site-wide announcement — to the people who can open the queue, and nobody else.
/// </summary>
/// <remarks>
/// Daniel (the client seat, member of nothing) applies to TGH through the same API the
/// apply-to-join box uses; Sarah (TGH administrator) sees the banner and can dismiss it for
/// the session; Victor (viewer, no role grants) sees no banner at all. The application is
/// withdrawn in finally — shared database — which also removes the banner's basis.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("ActionNeededBanner")]
public class ActionNeededBannerTests : BenTestBase
{
    private const string TghId = "881ea0f6-8c0d-475e-9065-c6ed15e3302f";

    [Test]
    public async Task A_waiting_application_banners_the_reviewer_and_not_the_viewer()
    {
        var daniel = await ApiLoginAsync(ClientEmail, ClientPassword);
        await WithdrawMyRequestIfAnyAsync(daniel);   // ENSURE a clean start, not assert one

        var created = await Page.APIRequest.PostAsync(
            $"http://localhost:5252/api/organizations/{TghId}/membership-requests",
            new() { Headers = daniel, DataObject = new { Message = "e2e action-needed banner" } });
        Assert.That(created.Ok, "Could not create the membership application.");

        try
        {
            // The reviewer's seat: the banner is there, names the bucket, and links to it.
            await LoginAsync(UserEmail, UserPassword);   // Sarah — TGH Administrator
            await Page.GotoAsync(BaseUrl);
            await WaitUntilLoadedAsync();

            var banner = Main.Locator(".action-needed-banner",
                new() { HasTextString = "membership application" }).First;
            await Expect(banner).ToBeVisibleAsync(new() { Timeout = 45_000 });

            // Exactly ONE banner per group's bucket — the double-trigger race once rendered
            // the whole list twice (Ben's report: every "1 investigation request" shown as
            // two identical rows).
            await Expect(Main.Locator(".action-needed-banner",
                new() { HasTextString = "membership application" })).ToHaveCountAsync(1);
            await Expect(banner.Locator($"a[href*='{TghId}'][href*='tab=members']"))
                .ToBeVisibleAsync(new() { Timeout = 45_000 });

            // Dismissed for the session: gone now, and still gone after a reload.
            await banner.Locator("button.btn-close").ClickAsync();
            await Expect(Main.Locator(".action-needed-banner",
                new() { HasTextString = "membership application" })).ToHaveCountAsync(0);

            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();
            await Expect(Main.Locator(".action-needed-banner",
                new() { HasTextString = "membership application" })).ToHaveCountAsync(0);

            // The seat without the gate: Victor belongs to the same group and hears nothing.
            await LoginAsync(ViewerEmail, ViewerPassword);
            await Page.GotoAsync(BaseUrl);
            await WaitUntilLoadedAsync();
            await Expect(Main.GetByText("has work waiting", new() { Exact = false }))
                .ToHaveCountAsync(0);
        }
        finally
        {
            await WithdrawMyRequestIfAnyAsync(daniel);
        }
    }

    private async Task<Dictionary<string, string>> ApiLoginAsync(string email, string password)
    {
        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email, password } });
        Assert.That(login.Ok, $"API login failed for {email}.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }

    private async Task WithdrawMyRequestIfAnyAsync(Dictionary<string, string> auth)
    {
        var mine = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/organizations/{TghId}/membership-requests/my",
            new() { Headers = auth });
        if (!mine.Ok) return;

        var json = await mine.JsonAsync();
        if (json is null || json.Value.ValueKind != System.Text.Json.JsonValueKind.Object) return;
        if (!json.Value.TryGetProperty("id", out var id)) return;

        await Page.APIRequest.DeleteAsync(
            $"http://localhost:5252/api/organizations/{TghId}/membership-requests/{id.GetString()}",
            new() { Headers = auth });
    }
}
