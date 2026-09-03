using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A session recorded by the real iPhone app replays in the web player with every channel the
/// app sent: room, markers, note, the audio recording as the clock, and who recorded it.
/// </summary>
/// <remarks>
/// Opt-in: the session is made by the app on a simulator (the FieldKitUploadProbe UI test), so
/// this needs its id in <c>BEN_PROBE_SESSION_ID</c>, uploaded by the member the suite signs in as.
/// <c>BEN_PROBE_SHOT</c> saves a full-page screenshot mid-playback.
/// </remarks>
[TestFixture]
[Category("RealAppSessionReplay")]
public class RealAppSessionReplayTests : BenTestBase
{
    [Test]
    public async Task A_session_from_the_real_app_replays_with_everything_it_recorded()
    {
        var id = Environment.GetEnvironmentVariable("BEN_PROBE_SESSION_ID");
        if (string.IsNullOrWhiteSpace(id)) Assert.Ignore("set BEN_PROBE_SESSION_ID to a session the app uploaded");

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{id}");
        await Expect(Page.GetByText("Probe: app upload").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Credited, not "nobody".
        await Expect(Page.Locator("text=/recorded by/i").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("nobody signed in when recorded")).ToHaveCountAsync(0);

        // The marks the app made, with their room.
        Assert.That(await Page.Locator("[data-testid='marker-room']").CountAsync(), Is.GreaterThan(0));
        await Expect(Page.Locator("[data-testid='marker-room']").First).ToHaveTextAsync("Cellar");

        // The app records AAC in an .m4a. Playwright's stock Chromium ships without that
        // decoder, so under it the page's honest answer is the "won't play" badge in place of a
        // control — the undecodable path, not a fault. The recording-as-clock half is proved
        // with a WAV in FieldSessionMediaClockTests and, for this file, in a browser with the
        // system decoders (BROWSER=webkit on a Mac).
        var canDecode = await Page.EvaluateAsync<string>("() => document.createElement('audio').canPlayType('audio/mp4; codecs=\"mp4a.40.2\"')");
        TestContext.Out.WriteLine($"canPlayType(aac) = '{canDecode}'");
        if (canDecode == "")
        {
            await Expect(Page.Locator("[data-testid='undecodable']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(Page.Locator("audio")).ToHaveCountAsync(0);
            await Expect(Page.GetByText("arrived damaged")).ToHaveCountAsync(0);
            Assert.Ignore("this browser cannot decode AAC; it showed the 'won't play' badge honestly. Run with BROWSER=webkit for the clock.");
        }

        await Expect(Page.Locator("audio")).ToHaveCountAsync(1);
        await Expect(Page.GetByText("arrived damaged")).ToHaveCountAsync(0);

        // Play: the recording becomes the clock and the readings follow it.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Play" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='media-clock']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(3000);
        var audioTime = await Page.EvaluateAsync<double>("() => document.querySelector('audio').currentTime");
        Assert.That(audioTime, Is.GreaterThan(1.0), "the app's recording should actually be playing");
        await Expect(Page.Locator("[data-testid='current-room']")).ToContainTextAsync("Cellar");

        if (Environment.GetEnvironmentVariable("BEN_PROBE_SHOT") is { Length: > 0 } shot)
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
    }
}
