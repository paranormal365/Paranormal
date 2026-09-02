using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The page that finally lists what the phone sent up.
/// </summary>
/// <remarks>
/// Before this, <c>GetFieldSessionsAsync</c> had no Razor consumer at all: the only routes to a
/// session were Report Builder's "Play back" and a URL you already held, so a member who recorded
/// and uploaded from the app had nowhere on the website to find it.
/// </remarks>
[TestFixture]
[Category("MyFieldSessions")]
public class MyFieldSessionsTests : BenTestBase
{
    [Test]
    public async Task Someone_with_sessions_sees_them_and_can_play_one_back()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");

        // Either the list, the empty state, or a refusal — never a silent nothing.
        await Page.WaitForSelectorAsync(
            "[data-testid='session-row'], [data-testid='no-sessions'], [data-testid='sessions-refusal']",
            new() { Timeout = 30_000 });

        await Expect(Page.Locator("[data-testid='sessions-refusal']")).ToHaveCountAsync(0);

        var rows = await Page.Locator("[data-testid='session-row']").CountAsync();
        if (rows == 0)
        {
            await Expect(Page.Locator("[data-testid='no-sessions']")).ToBeVisibleAsync();
            Assert.Ignore("this account has uploaded nothing — the empty state was checked instead");
        }

        // Every row offers a way in. A list you cannot open is the problem this page exists to fix.
        Assert.That(await Page.Locator("[data-testid='play-back']").CountAsync(), Is.EqualTo(rows));

        if (Environment.GetEnvironmentVariable("BEN_SESSIONS_SHOT") is { Length: > 0 } shot)
        {
            await Page.WaitForTimeoutAsync(2500);   // let the map's tiles arrive
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
        }

        await Page.Locator("[data-testid='play-back']").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/field-sessions/"));
    }

    [Test]
    public async Task A_visitor_is_sent_to_sign_in()
    {
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Nobody's sessions are shown to nobody.
        await Expect(Page.Locator("[data-testid='session-row']")).ToHaveCountAsync(0);
    }
}
