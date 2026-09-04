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

    /// <summary>
    /// Moving the map asks the server for what is in view — once per gesture, after a pause, and
    /// never for a viewport the map has already left.
    /// </summary>
    /// <remarks>
    /// The debounce is the whole point of the change: a person exploring pans several times a
    /// second, and one request per pan for viewports they have already left is the overload this
    /// was written to avoid. So the test drags three times in quick succession and expects ONE
    /// bounded request, not three.
    /// </remarks>
    [Test]
    public async Task Panning_the_map_asks_for_the_viewport_once_not_once_per_gesture()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Page.WaitForSelectorAsync("[data-testid='session-row'], [data-testid='no-sessions']",
                                        new() { Timeout = 30_000 });

        var map = Page.Locator(".k-map").First;
        if (await map.CountAsync() == 0)
            Assert.Ignore("this account has no session with a fix, so there is no map to pan");

        // Blazor Server calls the API from the server, so the browser never sees the request.
        // The page counts its own bounded loads into a hidden element for exactly this reason.
        // The footer is visible and renders with the map card; the counter beside it is hidden,
        // so it is read with TextContent (innerText of a hidden element is always empty).
        await Page.Locator("[data-testid='map-footer']").WaitForAsync(new() { Timeout = 30_000 });
        // Read through the DOM directly: Playwright's locator methods decline to act on an element
        // carrying the `hidden` attribute, and this one is hidden on purpose.
        const string readCounter =
            "() => document.querySelector(\"[data-testid='map-bounded-loads']\")?.textContent?.trim() ?? 'missing'";
        Assert.That(await Page.EvaluateAsync<string>(readCounter), Is.EqualTo("0"), "nothing bounded before any gesture");

        var box = (await map.BoundingBoxAsync())!;
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;

        // Three quick drags: each is a gesture the map reports; only the last should be asked about.
        for (var i = 0; i < 3; i++)
        {
            await Page.Mouse.MoveAsync(cx, cy);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(cx + 60, cy + 40, new() { Steps = 5 });
            await Page.Mouse.UpAsync();
            await Page.WaitForTimeoutAsync(80);      // well inside the 350 ms debounce
        }

        await Page.WaitForTimeoutAsync(1500);        // let the debounce fire and the request land

        // What the map actually reported, so a failure here says which corner was which.
        TestContext.Out.WriteLine("last bounds N,S,E,W = " + await Page.EvaluateAsync<string>(
            "() => document.querySelector(\"[data-testid='map-last-bounds']\")?.textContent?.trim() ?? 'missing'"));
        TestContext.Out.WriteLine("pins after pan = " + await Page.Locator(".k-marker").CountAsync());

        Assert.That(await Page.EvaluateAsync<string>(readCounter), Is.EqualTo("1"),
            "three gestures inside the debounce window should produce exactly one bounded request");
    }

    [Test]
    public async Task A_visitor_is_sent_to_sign_in()
    {
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Nobody's sessions are shown to nobody.
        await Expect(Page.Locator("[data-testid='session-row']")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Deleting your own session (item 218). <b>Nothing here presses the button</b> — the suite
    /// has more than once been pointed at a database somebody cares about, and a test that
    /// actually deleted a night's recording would be indistinguishable from the accident the
    /// confirmation exists to prevent. What is driven is the row's control and the dialog it
    /// opens; the delete itself is covered by FieldSessionDeleteTests against a real database.
    /// </summary>
    [Test]
    public async Task A_session_of_your_own_offers_a_delete_that_asks_first()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Page.WaitForSelectorAsync(
            "[data-testid='session-row'], [data-testid='no-sessions'], [data-testid='sessions-refusal']",
            new() { Timeout = 30_000 });

        var deleteButton = Page.Locator("[data-testid='delete-session']").First;
        if (await deleteButton.CountAsync() == 0)
        {
            // Every session this account has belongs to a group, or it has none. Both are valid
            // states of the page, and the row then has to say so rather than show a dead button.
            var rows = await Page.Locator("[data-testid='session-row']").CountAsync();
            if (rows > 0)
            {
                await Expect(Page.Locator("[data-testid='belongs-to-group']").First)
                    .ToBeVisibleAsync(new() { Timeout = 10_000 });
            }
            Assert.Ignore("No personal sessions in this database to offer a delete for.");
            return;
        }

        await deleteButton.ClickAsync();

        // The confirmation names what goes, and cancelling leaves the row alone.
        var dialog = Page.GetByText("There is no undo", new() { Exact = false }).First;
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).First.ClickAsync();
        await Expect(Page.Locator("[data-testid='delete-session']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
