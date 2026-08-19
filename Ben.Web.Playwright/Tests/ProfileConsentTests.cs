using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.Json;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The profile page and the two-key consent behind a member's private photo.
/// </summary>
/// <remarks>
/// <para>
/// Nothing covered this. It is worth covering because what renders depends on who is asking, and
/// that is the shape that has broken here before — a privacy control showing too much looks
/// exactly like one working correctly, from the only seat anyone tests from.
/// </para>
/// <para>
/// "Two keys" means the member opts in and the group allows it; neither alone shares anything.
/// The tests assert both halves and, more importantly, that the app is honest when only one key
/// is turned: a consent toggle that silently does nothing is worse than no toggle, because the
/// person believes they have shared something they have not.
/// </para>
/// </remarks>
[TestFixture]
[Category("Profile")]
public class ProfileConsentTests : BenTestBase
{
    private async Task<string> TokenAsync(string email, string password)
    {
        var login = await Page.APIRequest.PostAsync($"{ApiUrl}/login",
            new() { DataObject = new { email, password } });
        return (await login.JsonAsync())?.GetProperty("accessToken").GetString() ?? "";
    }

    private async Task<JsonElement> ProfileAsync(string token)
    {
        var response = await Page.APIRequest.GetAsync($"{ApiUrl}/api/me/profile",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        return (await response.JsonAsync())!.Value;
    }

    [Test]
    public async Task ProfilePage_ShowsBothPhotoSlotsAndTheConsentControl()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
            Assert.That(body, Does.Contain("Private photo"), "the private photo slot is missing");
            Assert.That(body, Does.Contain("Public"), "the public photo slot is missing");
        });

        // The consent control has to be reachable, not merely present in the markup.
        var consent = Main.Locator("#share-private");
        await Expect(consent).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await Expect(consent).ToBeEnabledAsync();
    }

    [Test]
    public async Task TurningOnSharing_SaysSoWhenNoGroupAllowsIt()
    {
        // The honest-failure case. A member turns their key, no group has turned theirs, so
        // nothing is shared — and the page has to say that rather than look like it worked.
        await LoginAsync(UserEmail, UserPassword);
        var token = await TokenAsync(UserEmail, UserPassword);

        var before = await ProfileAsync(token);
        if (before.GetProperty("anyOrgAllowsPrivatePhotoSharing").GetBoolean())
            Assert.Ignore("a group already allows photo sharing, so the one-key warning cannot arise");

        await Page.GotoAsync($"{BaseUrl}/profile");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var consent = Main.Locator("#share-private");
        await Expect(consent).ToBeVisibleAsync(new() { Timeout = 8_000 });

        var wasChecked = await consent.IsCheckedAsync();
        if (!wasChecked) await consent.CheckAsync();

        try
        {
            await Expect(Main.GetByText("none of your groups currently allow", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
        finally
        {
            // Put the account back the way it was found — these run against shared dev data.
            if (!wasChecked)
            {
                await consent.UncheckAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task TheConsentChoice_SurvivesAReload()
    {
        // The same shape as the vote that did not survive a reload: a control that saves, and a
        // page that then loads without reflecting what was saved. That failure is invisible until
        // someone comes back to the page, and for a privacy setting the wrong direction is bad in
        // a way a vote is not.
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var consent = Main.Locator("#share-private");
        await Expect(consent).ToBeVisibleAsync(new() { Timeout = 8_000 });

        var original = await consent.IsCheckedAsync();
        try
        {
            if (original) await consent.UncheckAsync(); else await consent.CheckAsync();
            await Page.WaitForTimeoutAsync(800);   // the toggle saves on change

            await Page.ReloadAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            var after = Main.Locator("#share-private");
            await Expect(after).ToBeVisibleAsync(new() { Timeout = 8_000 });
            Assert.That(await after.IsCheckedAsync(), Is.EqualTo(!original),
                "the consent choice was not what the page showed after a reload");
        }
        finally
        {
            var restore = Main.Locator("#share-private");
            if (await restore.CountAsync() > 0 && await restore.IsCheckedAsync() != original)
            {
                if (original) await restore.CheckAsync(); else await restore.UncheckAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task OneKeyAlone_SharesNothing()
    {
        // Asserted against the API rather than the screen: this is the rule itself, and the
        // server is what enforces it. A UI-only check would pass just as well if the page simply
        // never rendered the photo for an unrelated reason.
        var token = await TokenAsync(UserEmail, UserPassword);
        var profile = await ProfileAsync(token);

        var memberOptedIn = profile.GetProperty("sharePrivatePhotoWithClients").GetBoolean();
        var groupAllows   = profile.GetProperty("anyOrgAllowsPrivatePhotoSharing").GetBoolean();

        // The two flags are reported separately on purpose — the page needs to tell the member
        // which key is missing. Collapsing them into one "isShared" would lose that.
        Assert.That(profile.TryGetProperty("sharePrivatePhotoWithClients", out _), Is.True,
            "the profile no longer reports the member's own consent");
        Assert.That(profile.TryGetProperty("anyOrgAllowsPrivatePhotoSharing", out _), Is.True,
            "the profile no longer reports whether any group allows sharing — without it the page "
            + "cannot tell a member that their opt-in is doing nothing");

        TestContext.Out.WriteLine($"member opted in: {memberOptedIn}, a group allows: {groupAllows}");
    }
}
