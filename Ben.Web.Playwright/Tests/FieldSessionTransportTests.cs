using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The session player's transport: Pause is a pause, from the button or from outside the page,
/// and nothing about the engine starting, the speed, the slider or the keyboard can undo it.
/// </summary>
/// <remarks>
/// <para>Ben, 10/28/2026: on his 10/23 session — a photo and the whole-session video, no sound
/// file — Pause did nothing in Safari, and later worked. A session with no sound was the one
/// case the engine's "paused from outside" rule never covered, and a second start of the engine
/// on the same page left the buttons on a stopped engine while a live one drove the video.</para>
///
/// <para>The video-only session uses <c>Fixtures/video-only-vp9.mp4</c>: 20 s, no sound track,
/// VP9 so that Playwright's Chromium (which has no H.264) decodes it. Made from the stage
/// fixture's own video; see <c>Fixtures/README.md</c>.</para>
/// </remarks>
[TestFixture]
[Category("FieldSessionEngine")]
public class FieldSessionTransportTests : BenTestBase
{
    private static readonly DateTime Start = new(2026, 10, 23, 12, 48, 0, DateTimeKind.Utc);
    private static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static byte[] Wav(int seconds, int rate = 8000)
    {
        var data = new byte[seconds * rate * 2];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Thirty seconds with marks at 0:12 and 0:20. With sound: one recording from 0:00. Video
    /// only: the 20 s video, noted when it ended at 0:20 (rule M4), and no recording.
    /// </summary>
    private static string Document(string label, bool videoOnly)
    {
        var readings = new StringBuilder();
        for (var t = 0; t <= 30; t += 2)
        {
            if (readings.Length > 0) readings.Append(',');
            var marker = t switch
            {
                12 => ",\"marker\":{\"value\":\"manual_marker\"}",
                20 when videoOnly => ",\"marker\":{\"value\":\"video\"}",
                20 => ",\"marker\":{\"value\":\"sentry_emf\"}",
                _ => "",
            };
            var extra = (t, videoOnly) switch
            {
                (0, false) => ",\"audio_ref\":{\"filename\":\"media/audio-001.wav\",\"start_offset_seconds\":0}",
                (20, true) => ",\"note\":\"video: media/video-001.mp4\"",
                _ => "",
            };
            readings.Append("{\"at\":\"" + Stamp(Start.AddSeconds(t)) + "\",\"triggered_by\":\"interval\","
                + "\"measurements\":{\"emf\":{\"value\":" + (48 + t % 5) + ",\"unit\":\"uT\",\"baseline\":48}" + marker + "}"
                + extra + "}");
        }
        return "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone16,2\"},"
             + "\"session\":{\"started_at\":\"" + Stamp(Start) + "\",\"ended_at\":\"" + Stamp(Start.AddSeconds(30))
             + "\",\"location_label\":\"" + label + "\"},\"readings\":[" + readings + "]}";
    }

    private async Task<string> UploadAsync(string label, bool videoOnly)
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True);
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(Document(label, videoOnly)) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var upload = await api.PostAsync("/api/field-sessions/document", new() { Headers = auth, Multipart = form });
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var (path, type, bytes) = videoOnly
            ? ("media/video-001.mp4", "video/mp4",
               await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-only-vp9.mp4")))
            : ("media/audio-001.wav", "audio/wav", Wav(30));
        var files = Context.APIRequest.CreateFormData();
        files.Append("file", new FilePayload { Name = Path.GetFileName(path), MimeType = type, Buffer = bytes });
        files.Append("relativePath", path);
        files.Append("sha256", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var attach = await api.PostAsync($"/api/field-sessions/{sessionId}/files", new() { Headers = auth, Multipart = files });
        Assert.That(attach.Ok, Is.True, await attach.TextAsync());
        return sessionId;
    }

    /// <summary>Opens the session once its recording or video has told the browser its length.</summary>
    private async Task OpenAsync(bool videoOnly)
    {
        var label = $"Transport {(videoOnly ? "video" : "sound")} {Guid.NewGuid():N}"[..28];
        var sessionId = await UploadAsync(label, videoOnly);
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText(label).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("[data-testid=play-pause]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var media = videoOnly ? "video" : "audio";
        await Page.WaitForFunctionAsync($"() => {{ const m = document.querySelector('{media}[data-fk-media]'); return !!m && m.duration > 0; }}",
                                        null, new() { Timeout = 20_000 });
    }

    private const string Module = "/_content/Ben.Web.Website.Library/js/fieldKitPlayer.js";

    private Task<JsonElement> EngineAsync()
        => Page.EvaluateAsync<JsonElement>($@"async () => {{
            const m = await import('{Module}');
            return m.state(document.querySelector('[data-testid=field-player]'));
        }}");

    private async Task<bool> PlayingAsync() => (await EngineAsync()).GetProperty("playing").GetBoolean();
    private async Task<double> PlayheadAsync() => (await EngineAsync()).GetProperty("playhead").GetDouble();

    /// <summary>Waits until the playhead has passed <paramref name="ms"/>: a condition, not a sleep.</summary>
    /// <remarks>This check imported the module inside the wait, which made it async; a promise is truthy,
    /// so it returned at once and waited for nothing (fixed 10/28/2026).</remarks>
    private Task PlayheadPastAsync(double ms) => WaitForPlayerAsync($"s.playhead > {ms}");

    private Task<bool> EveryRecordingPausedAsync()
        => Page.EvaluateAsync<bool>("() => [...document.querySelectorAll('[data-fk-media]')].every(m => m.paused)");

    /// <summary>After a pause: stopped now, and still stopped a moment later, recordings and all.</summary>
    private async Task AssertHeldAsync(string how)
    {
        await WaitForPlayerAsync("s.playing === false", 3_000);
        var at = await PlayheadAsync();
        // The thing being proved is that nothing starts it again: give the engine time to try.
        await Page.WaitForTimeoutAsync(1_500);
        Assert.Multiple(async () =>
        {
            Assert.That(await PlayingAsync(), Is.False, $"{how}: the session started playing again");
            Assert.That(await PlayheadAsync(), Is.EqualTo(at).Within(1), $"{how}: the clock kept running");
            Assert.That(await EveryRecordingPausedAsync(), Is.True, $"{how}: a recording kept playing");
            Assert.That(await Page.Locator("[data-testid=play-pause]").InnerTextAsync(), Does.Contain("Play"),
                        $"{how}: the button still says Pause");
        });
    }

    [TestCase(false, TestName = "Pause_stops_the_clock_and_the_recording")]
    [TestCase(true, TestName = "Pause_stops_the_clock_and_the_video_on_a_session_with_no_sound")]
    public async Task Pause_stops_everything(bool videoOnly)
    {
        await OpenAsync(videoOnly);
        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(1_500);

        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await AssertHeldAsync("the Pause button");
    }

    /// <summary>
    /// Now Playing, a keyboard's media key, AirPods: they pause the element, not the page. On a
    /// session with no sound the video was ignored and the engine played it straight back.
    /// </summary>
    [Test]
    public async Task A_pause_from_outside_the_page_pauses_a_session_with_no_sound()
    {
        await OpenAsync(videoOnly: true);
        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(1_500);

        await Page.EvaluateAsync("() => document.querySelector('video[data-fk-media]').pause()");
        await AssertHeldAsync("a pause from the system");
    }

    /// <summary>
    /// A pause from outside that lands just before the player's next frame. The frame saw a paused
    /// recording it had started and started it again, then ignored the pause when it arrived: the
    /// recording played on and the button stayed on Pause. Intermittent in a full run (Chromium,
    /// 10/28/2026); forced here by running the frame between the pause and its event.
    /// </summary>
    [Test]
    public async Task A_pause_from_outside_wins_the_race_with_the_next_frame()
    {
        await OpenAsync(videoOnly: false);
        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(1_500);

        await Page.EvaluateAsync($@"async () => {{
            const m = await import('{Module}');
            const root = document.querySelector('[data-testid=field-player]');
            document.querySelector('audio[data-fk-media]').pause();
            m.frameNow(root);   // before the browser has said 'pause'
        }}");
        await AssertHeldAsync("a pause that met the next frame");
    }

    /// <summary>
    /// The host starting the engine a second time on the same page used to leave the buttons on
    /// the first engine: Pause paused that one and the live one carried on.
    /// </summary>
    [Test]
    public async Task Starting_the_engine_twice_keeps_one_working_transport()
    {
        await OpenAsync(videoOnly: true);
        // The same call the host makes, made a second time on the same element with the same timeline.
        await Page.EvaluateAsync($@"async () => {{
            const m = await import('{Module}');
            const root = document.querySelector('[data-testid=field-player]');
            m.init(root, {{ invokeMethodAsync: () => Promise.resolve() }}, m.timelineJson(root), {{ urls: {{}} }});
        }}");
        Assert.That(await Page.Locator("[data-testid=play-pause]").CountAsync(), Is.EqualTo(1), "one Play button");

        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(1_500);
        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await AssertHeldAsync("Pause after a second start");
    }

    /// <summary>
    /// Above 2× the sound goes quiet and the engine pauses the recording itself. That pause was
    /// told apart from a person's by a timer, which could lose the race and stop the session.
    /// </summary>
    [Test]
    public async Task Four_times_speed_keeps_going()
    {
        await OpenAsync(videoOnly: false);
        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(500);
        await Page.GetByRole(AriaRole.Button, new() { Name = "4×", Exact = true }).ClickAsync();
        await PlayheadPastAsync(10_000);
        Assert.That(await PlayingAsync(), Is.True, "the session stopped itself at 4×");
    }

    /// <summary>Letting go of the slider anywhere frees it, so it follows the playhead again.</summary>
    [Test]
    public async Task Releasing_the_scrubber_off_the_slider_frees_it()
    {
        await OpenAsync(videoOnly: false);
        await Page.EvaluateAsync(@"() => {
            const s = document.querySelector('[data-testid=scrubber]');
            s.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
            document.body.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
        }");
        Assert.That((await EngineAsync()).GetProperty("dragging").GetBoolean(), Is.False, "the slider is still being dragged");

        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(2_000);
        await Page.WaitForFunctionAsync("() => Number(document.querySelector('[data-testid=scrubber]').value) > 0",
                                        null, new() { Timeout = 5_000 });
    }

    /// <summary>Clicking the readings, the stage or the chart gives the player the keyboard.</summary>
    [Test]
    public async Task Space_works_after_clicking_the_readings()
    {
        await OpenAsync(videoOnly: false);
        await Page.Locator("[data-testid=session-readout]").ClickAsync();
        await Page.Keyboard.PressAsync("Space");
        await PlayheadPastAsync(500);
        Assert.That(await PlayingAsync(), Is.True);

        await Page.Keyboard.PressAsync("Space");
        await AssertHeldAsync("Space");
    }

    /// <summary>
    /// A recording whose first fetch fails is tried again with a fresh link and plays (10/28/2026).
    /// It used to be marked "won't play in this browser" for the rest of the visit.
    /// </summary>
    [Test]
    public async Task A_recording_that_fails_to_load_once_is_tried_again_and_plays()
    {
        var label = $"Transport retry {Guid.NewGuid():N}"[..26];
        var sessionId = await UploadAsync(label, videoOnly: false);
        await LoginAsync(MemberEmail, MemberPassword);

        var cut = 0;
        await Page.RouteAsync("**/media/field-sessions/**", async route =>
        {
            if (Interlocked.Increment(ref cut) == 1) await route.AbortAsync("failed");
            else await route.ContinueAsync();
        });

        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText(label).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.WaitForFunctionAsync("() => { const m = document.querySelector('audio[data-fk-media]'); return !!m && m.duration > 0; }",
                                        null, new() { Timeout = 20_000 });

        Assert.That(cut, Is.GreaterThanOrEqualTo(2), "the recording was not asked for again");
        await Expect(Page.Locator("[data-testid=undecodable]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=unreachable]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=media-notice]")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await PlayheadPastAsync(1_500);
        Assert.That(await Page.EvaluateAsync<bool>("() => !document.querySelector('audio[data-fk-media]').paused"), Is.True,
                    "the recording isn't playing");
    }

    /// <summary>
    /// Playing 1.5 s before a mark, Next takes that mark (rule T4, 10/28/2026). It used to skip
    /// any mark under three seconds ahead.
    /// </summary>
    [Test]
    public async Task Next_mark_from_a_moving_playhead_takes_the_next_one()
    {
        await OpenAsync(videoOnly: false);
        await Page.EvaluateAsync(@"() => {
            const s = document.querySelector('[data-testid=scrubber]');
            s.value = String(10500 / 30000 * 1000);
            s.dispatchEvent(new Event('input', { bubbles: true }));
            s.dispatchEvent(new Event('change', { bubbles: true }));
        }");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Next mark" }).ClickAsync();
        Assert.That(await PlayheadAsync(), Is.EqualTo(9_000).Within(5), "Next skipped the mark at 0:12");

        // Parked before 0:12 now: Next goes on to 0:20.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Next mark" }).ClickAsync();
        Assert.That(await PlayheadAsync(), Is.EqualTo(17_000).Within(5));
    }

    /// <summary>
    /// Two recordings back to back — <c>audio-001.wav</c> 0:04–0:14, <c>audio-002.wav</c> from 0:14, the
    /// way the phone splits a long night — play across the join: the clock never steps back, and
    /// the second recording is sounding within half a second of the playhead reaching it.
    /// </summary>
    [Test]
    public async Task Two_recordings_hand_over_without_a_gap()
    {
        var label = $"Transport hand-over {Guid.NewGuid():N}"[..28];
        var readings = new StringBuilder();
        for (var t = 0; t <= 30; t += 2)
        {
            if (readings.Length > 0) readings.Append(',');
            var reference = t switch
            {
                4 => ",\"audio_ref\":{\"filename\":\"media/audio-001.wav\",\"start_offset_seconds\":0}",
                14 => ",\"audio_ref\":{\"filename\":\"media/audio-002.wav\",\"start_offset_seconds\":0}",
                _ => "",
            };
            readings.Append("{\"at\":\"" + Stamp(Start.AddSeconds(t)) + "\",\"triggered_by\":\"interval\","
                + "\"measurements\":{\"emf\":{\"value\":48,\"unit\":\"uT\",\"baseline\":48}}" + reference + "}");
        }
        var document = "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone16,2\"},"
            + "\"session\":{\"started_at\":\"" + Stamp(Start) + "\",\"ended_at\":\"" + Stamp(Start.AddSeconds(30))
            + "\",\"location_label\":\"" + label + "\"},\"readings\":[" + readings + "]}";

        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True);
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(document) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var upload = await api.PostAsync("/api/field-sessions/document", new() { Headers = auth, Multipart = form });
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString()!;
        foreach (var (path, seconds) in new[] { ("media/audio-001.wav", 10), ("media/audio-002.wav", 16) })
        {
            var bytes = Wav(seconds);
            var files = Context.APIRequest.CreateFormData();
            files.Append("file", new FilePayload { Name = Path.GetFileName(path), MimeType = "audio/wav", Buffer = bytes });
            files.Append("relativePath", path);
            files.Append("sha256", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var attach = await api.PostAsync($"/api/field-sessions/{sessionId}/files", new() { Headers = auth, Multipart = files });
            Assert.That(attach.Ok, Is.True, await attach.TextAsync());
        }

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText(label).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // The engine builds the transport after the page draws; the recordings can know their lengths first.
        await Expect(Page.Locator("[data-testid=scrubber]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.WaitForFunctionAsync("() => { const a = [...document.querySelectorAll('audio[data-fk-media]')]; return a.length === 2 && a.every(m => m.duration > 0); }",
                                        null, new() { Timeout = 20_000 });

        // From 0:12, two seconds before the join.
        await Page.EvaluateAsync(@"() => {
            const s = document.querySelector('[data-testid=scrubber]');
            s.value = String(12000 / 30000 * 1000);
            s.dispatchEvent(new Event('input', { bubbles: true }));
            s.dispatchEvent(new Event('change', { bubbles: true }));
        }");
        await ExposePlayerAsync();
        await Page.Locator("[data-testid=play-pause]").ClickAsync();

        // Every frame for five seconds: the playhead, and whether the second recording is sounding.
        var rows = await Page.EvaluateAsync<JsonElement>($@"async () => {{
            const m = await import('{Module}');
            const root = document.querySelector('[data-testid=field-player]');
            const clips = JSON.parse(m.timelineJson(root)).clips ?? [];
            const second = clips.find(c => (c.path ?? c.Path) === 'media/audio-002.wav');
            const id = second.fileId ?? second.FileId;
            const el = document.querySelector(`audio[data-fk-media='${{id}}']`);
            const rows = [], t0 = performance.now();
            await new Promise(done => {{
                const f = () => {{
                    const s = window.__fkState();
                    rows.push({{ wall: performance.now() - t0, playhead: s.playhead,
                                 sounding: !el.paused && !el.muted && el.currentTime > 0.05 }});
                    performance.now() - t0 < 5000 ? requestAnimationFrame(f) : done();
                }};
                requestAnimationFrame(f);
            }});
            return rows;
        }}");
        await Page.Locator("[data-testid=play-pause]").ClickAsync();

        var samples = rows.EnumerateArray()
            .Select(r => (Wall: r.GetProperty("wall").GetDouble(), Playhead: r.GetProperty("playhead").GetDouble(),
                          Sounding: r.GetProperty("sounding").GetBoolean()))
            .ToList();
        for (var i = 1; i < samples.Count; i++)
            Assert.That(samples[i].Playhead, Is.GreaterThanOrEqualTo(samples[i - 1].Playhead - 1),
                $"the clock stepped back at {samples[i].Wall:0} ms: {samples[i - 1].Playhead:0} → {samples[i].Playhead:0}");

        var reached = samples.FirstOrDefault(r => r.Playhead >= 14_000);
        Assert.That(reached.Wall, Is.GreaterThan(0), $"the playhead never reached the join (ended at {samples[^1].Playhead:0} ms)");
        var sounding = samples.FirstOrDefault(r => r.Sounding && r.Wall >= reached.Wall);
        TestContext.Out.WriteLine($"join reached at {reached.Wall:0} ms; second recording sounding at {sounding.Wall:0} ms; ended at {samples[^1].Playhead:0}");
        Assert.That(sounding.Wall, Is.GreaterThan(0), "the second recording never sounded");
        Assert.That(sounding.Wall - reached.Wall, Is.LessThan(500), "a gap in the sound at the hand-over");
        Assert.That(samples[^1].Playhead, Is.GreaterThan(15_500), "it played on past the join");
    }
}
