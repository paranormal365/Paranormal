using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The session player with the browser keeping time (10/14/2026).
/// </summary>
/// <remarks>
/// <para>What these pin is what the engine exists for: a drag during playback lands and stays; the
/// server hears nothing while somebody watches; the map follows the walk; tapping a photo goes to
/// its moment; the row for the mark being passed is lit; the keyboard moves the playhead.</para>
///
/// <para>The engine's own state is read through its module's <c>state</c> export, the same module
/// instance the page holds — a test reading what the engine believes, rather than inferring it
/// from a label that is only redrawn when it changes.</para>
/// </remarks>
[TestFixture]
[Category("FieldSessionEngine")]
public class FieldSessionEngineTests : BenTestBase
{
    private static readonly DateTime Start = new(2026, 9, 27, 1, 0, 0, DateTimeKind.Utc);
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
    /// Thirty seconds walking north at about 5 m every 2 s, facing east, with the session's sound
    /// from 0:00, a photograph at 0:10 and marks at 0:12 and 0:20.
    /// </summary>
    private static string Walk(string label)
    {
        var readings = new StringBuilder();
        for (var t = 0; t <= 30; t += 2)
        {
            if (readings.Length > 0) readings.Append(',');
            var lat = 36.16 + t / 2 * 0.000045;   // ~5 m a step
            var extra = t switch
            {
                4 => ",\"audio_ref\":{\"filename\":\"media/audio-001.wav\",\"start_offset_seconds\":4}",
                10 => ",\"note\":\"photo: media/photo-001.jpg\"",
                _ => "",
            };
            var marker = t switch
            {
                10 => ",\"marker\":{\"value\":\"photo\"}",
                12 => ",\"marker\":{\"value\":\"manual_marker\"}",
                20 => ",\"marker\":{\"value\":\"sentry_emf\"}",
                _ => "",
            };
            readings.Append("{\"at\":\"" + Stamp(Start.AddSeconds(t)) + "\",\"triggered_by\":\"interval\","
                + "\"measurements\":{\"emf\":{\"value\":" + (48 + t % 5) + ",\"unit\":\"uT\",\"baseline\":48},"
                + "\"room\":{\"value\":\"" + (t < 16 ? "Hall" : "Cellar") + "\"}" + marker + "},"
                + "\"position\":{\"latitude\":" + lat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"longitude\":-86.78,\"accuracy_meters\":8},\"motion\":{\"heading_degrees\":90}" + extra + "}");
        }
        return "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone17,1\"},"
             + "\"session\":{\"started_at\":\"" + Stamp(Start) + "\",\"ended_at\":\"" + Stamp(Start.AddSeconds(30))
             + "\",\"location_label\":\"" + label + "\"},\"readings\":[" + readings + "]}";
    }

    private async Task<string> UploadWalkAsync(string label)
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True);
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(Walk(label)) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var upload = await api.PostAsync("/api/field-sessions/document", new() { Headers = auth, Multipart = form });
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString()!;

        async Task AttachAsync(string path, string type, byte[] bytes)
        {
            var files = Context.APIRequest.CreateFormData();
            files.Append("file", new FilePayload { Name = Path.GetFileName(path), MimeType = type, Buffer = bytes });
            files.Append("relativePath", path);
            files.Append("sha256", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var attach = await api.PostAsync($"/api/field-sessions/{sessionId}/files", new() { Headers = auth, Multipart = files });
            Assert.That(attach.Ok, Is.True, await attach.TextAsync());
        }
        await AttachAsync("media/audio-001.wav", "audio/wav", Wav(30));
        await AttachAsync("media/photo-001.jpg", "image/jpeg",
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "room-photo-1.jpg")));
        return sessionId;
    }

    private async Task OpenAsync(string sessionId, string label)
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText(label).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("input[type=range]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.WaitForFunctionAsync("() => { const a = document.querySelector('audio'); return !!a && a.duration > 0; }",
                                        null, new() { Timeout = 15_000 });
    }

    private Task<JsonElement> EngineAsync()
        => Page.EvaluateAsync<JsonElement>(@"async () => {
            const m = await import('/_content/Ben.Web.Website.Library/js/fieldKitPlayer.js');
            return m.state(document.querySelector('[data-testid=field-player]'));
        }");

    private async Task<double> PlayheadAsync() => (await EngineAsync()).GetProperty("playhead").GetDouble();

    // ── Real time, never backwards (Ben, 10/14/2026: "No excuses for there to be lag or errors in
    //    playback after deploying this branch to production.") ─────────────────────────────────
    // 8 s of playing once showed 4.9 s: the clock stopped while the recording buffered. These hold
    // the player to the time on the wall, on this machine and on a connection as slow as a phone's.

    [Test]
    public async Task Playback_keeps_real_time_and_never_goes_backwards()
        => await PlaysInRealTimeAsync("Engine real time", mediaDelayMs: 0);

    [Test]
    public async Task On_a_slow_connection_playback_still_keeps_real_time()
        => await PlaysInRealTimeAsync("Engine slow connection", mediaDelayMs: 800);

    private async Task PlaysInRealTimeAsync(string label, int mediaDelayMs)
    {
        var sessionId = await UploadWalkAsync(label);
        if (mediaDelayMs > 0)
        {
            // Every request for a recording waits, the way production's do across the internet.
            await Page.RouteAsync("**/media/field-sessions/**", async route =>
            {
                await Task.Delay(mediaDelayMs);
                await route.ContinueAsync();
            });
        }
        await OpenAsync(sessionId, label);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true }).ClickAsync();
        // Sampled in the page every 100 ms for 8 s: the wall clock beside the playhead.
        var samples = await Page.EvaluateAsync<JsonElement>(@"async () => {
            const m = await import('/_content/Ben.Web.Website.Library/js/fieldKitPlayer.js');
            const root = document.querySelector('[data-testid=field-player]');
            const out = [];
            const t0 = performance.now();
            while (performance.now() - t0 < 8000) {
                out.push([performance.now() - t0, m.state(root).playhead, m.state(root).playing]);
                await new Promise(r => setTimeout(r, 100));
            }
            return out;
        }");

        var rows = samples.EnumerateArray().Select(r => (Wall: r[0].GetDouble(), Playhead: r[1].GetDouble(), Playing: r[2].GetBoolean())).ToList();
        TestContext.Out.WriteLine($"first {rows[0].Playhead:0} ms, last {rows[^1].Playhead:0} ms over {rows[^1].Wall:0} ms");

        Assert.That(rows.All(r => r.Playing), Is.True, "playback stopped by itself");
        for (var i = 1; i < rows.Count; i++)
            Assert.That(rows[i].Playhead, Is.GreaterThanOrEqualTo(rows[i - 1].Playhead - 1),
                $"the playhead went backwards at {rows[i].Wall:0} ms: {rows[i - 1].Playhead:0} → {rows[i].Playhead:0}");

        var advanced = rows[^1].Playhead - rows[0].Playhead;
        var wall = rows[^1].Wall - rows[0].Wall;
        Assert.That(advanced, Is.InRange(wall - 1000, wall + 500),
            $"{advanced:0} ms of session in {wall:0} ms of real time — playback must keep time");

        // And it moved from the start: no frozen first second while the recording loaded.
        var firstSecond = rows.Where(r => r.Wall <= 1000).ToList();
        Assert.That(firstSecond[^1].Playhead - firstSecond[0].Playhead, Is.GreaterThan(500),
            "the first second of playing should move the playhead");
    }

    [Test]
    public async Task A_drag_while_playing_lands_where_it_was_dragged_and_carries_on_from_there()
    {
        const string label = "Engine drag while playing";
        await OpenAsync(await UploadWalkAsync(label), label);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-testid='media-clock']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(1500);

        // A real drag: press on the thumb's track, move back, then forward to two-thirds (0:20).
        var box = (await Page.Locator("[data-testid='scrubber']").BoundingBoxAsync())!;
        var y = box.Y + box.Height / 2;
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * 0.05), y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * 0.2), y, new() { Steps = 4 });
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * (20.0 / 30)), y, new() { Steps = 6 });
        await Page.Mouse.UpAsync();

        // Sampled for a second and a half: it carries on FROM the drag, never back to where the
        // sound had been (the first player's snap-back).
        var samples = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            await Page.WaitForTimeoutAsync(250);
            samples.Add(await PlayheadAsync());
        }
        TestContext.Out.WriteLine(string.Join(", ", samples.Select(s => s.ToString("0"))));
        Assert.That(samples.First(), Is.EqualTo(20_000).Within(1_200), "the drag should land where it was let go");
        Assert.That(samples, Is.Ordered, "and play on from there");
        Assert.That(samples.Last() - samples.First(), Is.GreaterThan(500), "still playing");

        var audio = await Page.EvaluateAsync<double>("() => document.querySelector('audio').currentTime");
        Assert.That(audio * 1000, Is.EqualTo(samples.Last()).Within(1_000), "the sound went with it");
    }

    [Test]
    public async Task Nothing_crosses_the_server_connection_while_a_session_plays()
    {
        // The first player re-rendered on the server four times a second per viewer. The engine
        // keeps time in the browser: while playing, the server should hear nothing at all.
        const string label = "Engine quiet server";
        var sessionId = await UploadWalkAsync(label);

        var received = 0;
        var sent = 0;
        var counting = false;
        Page.WebSocket += (_, socket) =>
        {
            if (!socket.Url.Contains("_blazor")) return;
            socket.FrameReceived += (_, _) => { if (counting) received++; };
            socket.FrameSent += (_, _) => { if (counting) sent++; };
        };

        await OpenAsync(sessionId, label);
        await Page.WaitForTimeoutAsync(1500);   // anything the first render set going has settled

        counting = true;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(8000);
        counting = false;

        TestContext.Out.WriteLine($"during 8 s of playback: {received} frames from the server, {sent} to it");
        Assert.That(await PlayheadAsync(), Is.GreaterThan(5_000), "it really was playing");
        // A keep-alive or two is the connection, not the player.
        Assert.That(received, Is.LessThanOrEqualTo(3), "the server should draw nothing while somebody watches");
        Assert.That(sent, Is.LessThanOrEqualTo(3), "and the browser should tell it nothing");
    }

    [Test]
    public async Task Tapping_a_photo_goes_to_its_moment()
    {
        const string label = "Engine photo seek";
        await OpenAsync(await UploadWalkAsync(label), label);

        await Page.Locator("[data-testid='photo-strip'] button").First.ClickAsync();
        await Expect(Page.Locator("[data-testid='photo-expanded']")).ToBeVisibleAsync();
        Assert.That(await PlayheadAsync(), Is.EqualTo(10_000).Within(5), "the photo was taken at 0:10");
        await Expect(Page.Locator("[data-testid='elapsed']")).ToHaveTextAsync("00:00:10");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("[data-testid='photo-expanded']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task The_mark_being_passed_is_lit_in_the_list()
    {
        const string label = "Engine marker highlight";
        await OpenAsync(await UploadWalkAsync(label), label);

        // Next mark: the first is at 0:12, so the playhead parks three seconds before it.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Next mark" }).ClickAsync();
        Assert.That(await PlayheadAsync(), Is.EqualTo(9_000).Within(5));
        await Expect(Page.Locator("[data-testid='marker-row'].fk-marker--now")).ToHaveCountAsync(0);

        // Within a second of it, its row is lit — and only its row.
        await Page.Locator("[data-testid='scrubber']").FillAsync("413");   // 0:12.39
        await Expect(Page.Locator("[data-testid='marker-row'].fk-marker--now")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid='marker-row'].fk-marker--now")).ToContainTextAsync("Marked");
    }

    [Test]
    public async Task The_keyboard_moves_the_playhead_five_seconds_at_a_time()
    {
        const string label = "Engine keyboard";
        await OpenAsync(await UploadWalkAsync(label), label);

        await Page.Locator("[data-testid='scrubber']").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("ArrowRight");
        Assert.That(await PlayheadAsync(), Is.EqualTo(10_000).Within(5));
        await Page.Keyboard.PressAsync("ArrowLeft");
        Assert.That(await PlayheadAsync(), Is.EqualTo(5_000).Within(5));
        await Page.Keyboard.PressAsync("End");
        Assert.That(await PlayheadAsync(), Is.EqualTo(30_000).Within(5));
        await Page.Keyboard.PressAsync("Home");
        Assert.That(await PlayheadAsync(), Is.EqualTo(0).Within(5));
    }

    [Test]
    public async Task The_map_marker_is_where_the_walk_was_and_points_the_way_they_faced()
    {
        const string label = "Engine map follows";
        await OpenAsync(await UploadWalkAsync(label), label);

        var mapState = @"async () => {
            const m = await import('/_content/Ben.Web.Website.Library/Kit/Maps/BenMap.razor.js');
            const id = document.querySelector('.ben-map')?.id;
            return id ? m.playerState(id) : null;
        }";
        // MapKit needs a key and Apple's script; a host without them shows "could not be loaded",
        // which is a different test's business.
        // Polled here rather than with WaitForFunction: the check imports a module, so it is async,
        // and a promise is truthy — WaitForFunction returned at once, before the map existed.
        var ready = false;
        for (var waited = 0; waited < 15_000 && !ready; waited += 250)
        {
            ready = (await Page.EvaluateAsync<JsonElement>(mapState)).ValueKind == JsonValueKind.Object;
            if (!ready) await Page.WaitForTimeoutAsync(250);
        }
        if (!ready) Assert.Ignore("The map could not be loaded on this host (no Maps key, or Apple's script unreachable).");

        // About 0:05, between the fixes at 0:04 and 0:06. The walk is steady, so where they were is
        // a straight line in time: half a step north for every second.
        await Page.Locator("[data-testid='scrubber']").FillAsync("167");
        await Page.WaitForTimeoutAsync(400);
        var at = await PlayheadAsync();
        Assert.That(at, Is.EqualTo(5_000).Within(50));
        var state = await Page.EvaluateAsync<JsonElement>(mapState);
        Assert.That(state.GetProperty("lat").GetDouble(), Is.EqualTo(36.16 + at / 2000 * 0.000045).Within(1e-7), "glides between fixes");
        Assert.That(state.GetProperty("bearing").GetDouble(), Is.EqualTo(90).Within(0.5), "the arrow points where they faced");
        Assert.That(state.GetProperty("accuracy").GetDouble(), Is.EqualTo(8).Within(0.01), "the circle is the fix's accuracy");
        Assert.That(state.GetProperty("trackPoints").GetInt32(), Is.GreaterThan(1), "the walk is drawn");
        Assert.That(state.GetProperty("following").GetBoolean(), Is.True);

        // A person dragging the map takes the view; Recenter gives it back.
        var map = Page.Locator(".ben-map");
        // In view first: the box is in viewport coordinates, and a mouse sent below the fold lands nowhere.
        await map.ScrollIntoViewIfNeededAsync();
        var box = (await map.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width / 2 + 60, box.Y + box.Height / 2 + 40, new() { Steps = 5 });
        await Page.Mouse.UpAsync();
        Assert.That((await Page.EvaluateAsync<JsonElement>(mapState)).GetProperty("following").GetBoolean(), Is.False);
        await Page.Locator("[data-testid='map-recenter']").ClickAsync();
        Assert.That((await Page.EvaluateAsync<JsonElement>(mapState)).GetProperty("following").GetBoolean(), Is.True);
        await Expect(Page.Locator("[data-testid='map-recenter']")).ToHaveCountAsync(0);
    }
}
