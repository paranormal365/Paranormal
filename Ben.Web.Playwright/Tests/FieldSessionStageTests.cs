using System.IO.Compression;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The web player does what the phone's review does (Ben, 2026-09-27: "I need this also to work
/// the exact same in the website as it does on their phone").
/// </summary>
/// <remarks>
/// <para>The session is <c>Fixtures/field-session-stage.ben</c>, written by the phone's OWN code
/// (BenKit's engine on a manual clock and the exporter behind Export a bundle — see
/// <c>TestSessionBundleBuilder.swift</c>): 18 seconds of sound from Start, an 8-second video from
/// 0:04 to 0:12 whose note is written when it FINISHES at 0:12, photographs at 0:02, 0:08 and 0:15,
/// motion seen at 0:05 and 0:08 with the phone still, and a mark at 0:10.</para>
///
/// <para><b>WebKit, not the suite's Chromium.</b> The phone records H.264 and AAC, which
/// Playwright's own Chromium build cannot decode and every browser a visitor uses can. Testing the
/// stage in a browser that cannot play the phone's video would test the "won't play" badge and
/// nothing else. These ran in real Chrome until 10/28/2026; they run in Playwright's WebKit now,
/// which is Safari's engine (where Ben's own sessions misbehaved) and plays the phone's formats on
/// the Mac. The sign-in happens in the suite's browser and its cookies are carried over.</para>
/// </remarks>
[TestFixture]
[Category("FieldSessionStage")]
public class FieldSessionStageTests : BenTestBase
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben");
    private static string ShotDir => Path.Combine(AppContext.BaseDirectory, "stage-shots");

    private const double Length = 18;

    private async Task<string> UploadAsync()
    {
        var bytes = await File.ReadAllBytesAsync(Fixture);
        string deviceSessionId;
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        using (var seal = zip.GetEntry("seal.json")!.Open())
            deviceSessionId = JsonDocument.Parse(seal).RootElement.GetProperty("session_id").GetString()!;

        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True, "the member must be able to sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "field-session-stage.ben", MimeType = "application/vnd.ishaunted.field-session", Buffer = bytes });
        form.Append("deviceSessionId", deviceSessionId);
        var upload = await api.PostAsync("/api/field-sessions/bundle",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }, Multipart = form });
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        return (await upload.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    /// <summary>Signs in with the suite's browser, then opens the session in WebKit as the same person.</summary>
    private async Task<(IBrowser Browser, IPage Page)> OpenInWebKitAsync(string sessionId, bool dark = false)
    {
        await LoginAsync(MemberEmail, MemberPassword);
        var state = await Context.StorageStateAsync();
        IBrowser webkit;
        try
        {
            webkit = await Playwright.Webkit.LaunchAsync(new() { Headless = true });
        }
        catch (PlaywrightException e) when (e.Message.Contains("Executable doesn't exist"))
        {
            Assert.Ignore("Playwright's WebKit is not installed: pwsh bin/Debug/net10.0/playwright.ps1 install webkit");
            throw;
        }
        var context = await webkit.NewContextAsync(new()
        {
            StorageState = state, ViewportSize = new() { Width = 1280, Height = 1100 },
            // The help screenshots are dark, like every other one on the site.
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, DeviceScaleFactor = dark ? 2 : 1,
        });
        if (dark)
            await context.AddInitScriptAsync("try { localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' })); localStorage.setItem('ben-theme', 'dark'); } catch (e) {}");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(page.GetByText("Stage check").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.Locator("[data-testid='media-stage']")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // The video's length arrives with its metadata, and its start is worked out from it.
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video'); return v && v.duration > 0; }",
                                        null, new() { Timeout = 20_000 });
        // And the engine has the whole session's length (it waits for the recordings' own).
        await WaitForPlayerAsync($"Math.abs(s.duration - {Length * 1000}) < 1000", on: page);
        return (webkit, page);
    }

    /// <summary>
    /// Moves the playhead and waits until the page has drawn it: the engine's frame has run, and every
    /// recording it moved has finished seeking and has a picture to show.
    /// </summary>
    private static async Task ScrubToAsync(IPage page, double seconds)
    {
        var value = (int)Math.Round(seconds / Length * 1000);
        await page.Locator("input[type=range]").FillAsync(value.ToString());
        await page.EvaluateAsync(Frames);
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('[data-fk-media]')].every(m => !m.seeking && (m.offsetParent === null || m.readyState >= 2))",
            null, new() { Timeout = 10_000, PollingInterval = 50 });
        await page.EvaluateAsync(Frames);
    }

    private const string Frames = "() => new Promise(r => { let n = 0; const f = () => ++n >= 3 ? r() : requestAnimationFrame(f); requestAnimationFrame(f); })";

    private static Task<bool> VideoShownAsync(IPage page)
        => page.EvaluateAsync<bool>("() => { const v = document.querySelector('video'); return !!v && v.offsetParent !== null; }");

    private static async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(ShotDir, name + ".png"), FullPage = false });
    }

    [Test]
    public async Task The_video_sits_where_it_was_filmed_and_plays_beside_the_sound()
    {
        var sessionId = await UploadAsync();
        var (browser, page) = await OpenInWebKitAsync(sessionId);
        try
        {
            // 0:02.5 — before the video began (and outside the one second a clip is shown early,
            // which covers the recorder starting a moment after the clock). Its note says 0:12,
            // when it FINISHED; read as a start, the video would sit at 0:12–0:20.
            await ScrubToAsync(page, 2.5);
            Assert.That(await VideoShownAsync(page), Is.False, "no video before 0:04");
            await Expect(page.Locator("[data-testid='media-stage']")).ToContainTextAsync("audio-001.m4a");
            // Drawn from the site's own sprite — an icon class from a font the site never loads drew nothing.
            await Expect(page.Locator("[data-testid='media-stage'] svg use")).ToHaveCountAsync(1);
            await ShotAsync(page, "01-sound-only");

            // 0:06 — two seconds into the video, which the stage shows at that frame.
            await ScrubToAsync(page, 6.5);
            Assert.That(await VideoShownAsync(page), Is.True, "the video covers 0:04 to 0:12");
            var videoTime = await page.EvaluateAsync<double>("() => document.querySelector('video').currentTime");
            Assert.That(videoTime, Is.EqualTo(2.5).Within(0.6), "the stage shows the frame at the playhead");

            // Play: the SOUND is the clock and the video follows it, muted so nothing is heard twice.
            await page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true }).ClickAsync();
            await Expect(page.Locator("[data-testid='media-clock']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            // A second of both playing: the sound past 0:07.5 and the video moving beside it. A timeout
            // is not the failure; the assertions below say which of the two stopped.
            try
            {
                await page.WaitForFunctionAsync(@"() => {
                    const v = document.querySelector('video'), a = document.querySelector('audio');
                    return !a.paused && a.currentTime > 7.5 && !v.paused && v.currentTime > 3.2;
                }", null, new() { Timeout = 15_000, PollingInterval = 100 });
            }
            catch (TimeoutException) { }
            var state = await page.EvaluateAsync<JsonElement>(@"() => {
                const v = document.querySelector('video'), a = document.querySelector('audio');
                return { videoPaused: v.paused, videoMuted: v.muted, videoTime: v.currentTime,
                         audioPaused: a.paused, audioTime: a.currentTime };
            }");
            TestContext.Out.WriteLine(state.ToString());
            Assert.That(state.GetProperty("audioPaused").GetBoolean(), Is.False, "the sound plays");
            Assert.That(state.GetProperty("videoPaused").GetBoolean(), Is.False, "the video plays beside it");
            Assert.That(state.GetProperty("videoMuted").GetBoolean(), Is.True, "and is muted while the sound runs");
            Assert.That(state.GetProperty("videoTime").GetDouble() + 4,
                        Is.EqualTo(state.GetProperty("audioTime").GetDouble()).Within(0.8),
                        "the two stay on one clock");
            await ShotAsync(page, "02-playing");
            await page.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true }).ClickAsync();
        }
        finally
        {
            await browser.CloseAsync();
        }
    }

    [Test]
    public async Task Photos_glow_as_their_moment_passes_and_open_full_size()
    {
        var sessionId = await UploadAsync();
        var (browser, page) = await OpenInWebKitAsync(sessionId);
        try
        {
            await Expect(page.Locator("[data-testid='photo-strip'] button")).ToHaveCountAsync(3);
            await Expect(page.Locator("[data-testid='photo-thumb-glowing']")).ToHaveCountAsync(0);

            // 0:02.5 — half a second after the first photograph: it glows, the others do not.
            await ScrubToAsync(page, 2.5);
            await Expect(page.Locator("[data-testid='photo-thumb-glowing']")).ToHaveCountAsync(1);
            await Expect(page.Locator("[data-testid='photo-thumb-glowing']")).ToContainTextAsync("0:02");
            await ShotAsync(page, "03-photo-glow");

            // Three seconds on, it has stopped.
            await ScrubToAsync(page, 5.6);
            await Expect(page.Locator("[data-testid='photo-thumb-glowing']")).ToHaveCountAsync(0);

            // Click one: playback pauses and the photograph is shown full size; click again, and it goes.
            await page.Locator("[data-testid='photo-strip'] button").Nth(1).ClickAsync();
            await Expect(page.Locator("[data-testid='photo-expanded']")).ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid='photo-expanded']")).ToContainTextAsync("0:08");
            await ShotAsync(page, "04-photo-expanded");
            await page.Locator("[data-testid='photo-expanded']").ClickAsync();
            await Expect(page.Locator("[data-testid='photo-expanded']")).ToHaveCountAsync(0);

            // Opened while playing, closing it carries on playing.
            await ScrubToAsync(page, 1);
            await page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true }).ClickAsync();
            await WaitForPlayerAsync("s.playing && s.playhead > 1500", on: page);
            await page.Locator("[data-testid='photo-strip'] button").First.ClickAsync();
            await Expect(page.Locator("[data-testid='photo-expanded']")).ToContainTextAsync("keep playing");
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true })).ToBeVisibleAsync();
            await page.Locator("[data-testid='photo-expanded']").ClickAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true })).ToBeVisibleAsync(new() { Timeout = 5_000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true }).ClickAsync();
        }
        finally
        {
            await browser.CloseAsync();
        }
    }

    /// <summary>
    /// The playback page for the help (<c>working-a-case</c>): the video with its sound, a photo
    /// glowing, and Motion detected — all at 0:08. Opt-in, like every other capture.
    /// </summary>
    [Test]
    public async Task Capture_the_player_for_the_help()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE") != "1")
            Assert.Ignore("Set BEN_CAPTURE=1 to re-capture the help screenshots.");

        var sessionId = await UploadAsync();
        var (browser, page) = await OpenInWebKitAsync(sessionId, dark: true);
        try
        {
            await ScrubToAsync(page, 8.6);
            await Expect(page.Locator("[data-testid='motion-detected']")).ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid='photo-thumb-glowing']")).ToHaveCountAsync(1);
            await page.EvaluateAsync("() => Promise.all([...document.images].map(i => i.decode().catch(() => {})))");
            await page.EvaluateAsync("() => document.fonts.ready");

            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx"))) root = root.Parent;
            var target = Path.Combine(root!.FullName, "Ben.Web.Website", "wwwroot", "help", "media",
                                      "working-a-case", "field-session-player.png");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // The player card only: the transport, the stage and the strip.
            await page.Locator("[data-testid='media-stage']").Locator("xpath=ancestor::div[contains(@class,'card')][1]")
                      .ScreenshotAsync(new() { Path = target });
            TestContext.Out.WriteLine("wrote " + target);
        }
        finally
        {
            await browser.CloseAsync();
        }
    }

    [Test]
    public async Task Motion_detected_shows_for_a_few_seconds_where_the_camera_saw_it()
    {
        var sessionId = await UploadAsync();
        var (browser, page) = await OpenInWebKitAsync(sessionId);
        try
        {
            await ScrubToAsync(page, 4.4);
            await Expect(page.Locator("[data-testid='motion-detected']")).ToHaveCountAsync(0);

            await ScrubToAsync(page, 5.6);
            await Expect(page.Locator("[data-testid='motion-detected']")).ToBeVisibleAsync();
            await Expect(page.Locator(".fk-stage--motion")).ToHaveCountAsync(1);
            await ShotAsync(page, "05-motion");

            // The second sighting at 0:08 keeps it up until 0:11; past that it is gone.
            await ScrubToAsync(page, 9.5);
            await Expect(page.Locator("[data-testid='motion-detected']")).ToBeVisibleAsync();
            await ScrubToAsync(page, 11.4);
            await Expect(page.Locator("[data-testid='motion-detected']")).ToHaveCountAsync(0);
            await Expect(page.Locator(".fk-stage--motion")).ToHaveCountAsync(0);

            // The marks are named the way the sign names them.
            await Expect(page.GetByText("Motion detected", new() { Exact = true })).ToHaveCountAsync(2);
        }
        finally
        {
            await browser.CloseAsync();
        }
    }
}
