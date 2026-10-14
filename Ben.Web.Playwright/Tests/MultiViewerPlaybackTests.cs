using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Ten people watching one session at once (10/14/2026).
/// </summary>
/// <remarks>
/// <para>Ben asked whether the player "can hold up being used by more than one person at a time".
/// The first player kept time on the server — four renders a second per viewer, plus calls back
/// into each browser — so the honest answer depended on how many were watching. The engine keeps
/// time in the browser; this opens ten viewers on one session, plays, scrubs, and checks every one
/// of them kept going while the server heard next to nothing from any of them.</para>
///
/// <para><b>Opt-in</b> (<c>BEN_MULTI_VIEWER=1</c>): ten browsers is a heavy test for an ordinary
/// run. Apple's map script is blocked for every viewer, so the run spends none of the site's daily
/// map allowance and does not depend on Apple's CDN; the map is not what is being measured.</para>
/// </remarks>
[TestFixture]
[Category("MultiViewer")]
[NonParallelizable]
public class MultiViewerPlaybackTests : BenTestBase
{
    private const int Viewers = 10;

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

    /// <summary>A minute of readings every second, with the session's sound from 0:00.</summary>
    private static string Document(string label)
    {
        var start = new DateTime(2026, 9, 27, 2, 0, 0, DateTimeKind.Utc);
        var readings = new StringBuilder();
        for (var t = 0; t <= 60; t++)
        {
            if (readings.Length > 0) readings.Append(',');
            var reference = t == 2 ? ",\"audio_ref\":{\"filename\":\"media/audio-001.wav\",\"start_offset_seconds\":2}" : "";
            readings.Append("{\"at\":\"" + start.AddSeconds(t).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
                + "\",\"measurements\":{\"emf\":{\"value\":" + (48 + t % 7) + ",\"baseline\":48}},"
                + "\"position\":{\"latitude\":" + (36.16 + t * 0.00002).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"longitude\":-86.78,\"accuracy_meters\":10}" + reference + "}");
        }
        return "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone17,1\"},"
             + "\"session\":{\"started_at\":\"" + start.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"ended_at\":\""
             + start.AddSeconds(60).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"location_label\":\"" + label + "\"},"
             + "\"readings\":[" + readings + "]}";
    }

    [Test]
    public async Task Ten_people_watching_one_session_all_play_and_the_server_stays_quiet()
    {
        if (Environment.GetEnvironmentVariable("BEN_MULTI_VIEWER") != "1")
            Assert.Ignore("Set BEN_MULTI_VIEWER=1 to open ten viewers on one session.");

        const string label = "Ten viewers";
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True);
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(Document(label)) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var upload = await api.PostAsync("/api/field-sessions/document", new() { Headers = auth, Multipart = form });
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString();
        var wav = Wav(60);
        var files = Context.APIRequest.CreateFormData();
        files.Append("file", new FilePayload { Name = "audio-001.wav", MimeType = "audio/wav", Buffer = wav });
        files.Append("relativePath", "media/audio-001.wav");
        files.Append("sha256", Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant());
        Assert.That((await api.PostAsync($"/api/field-sessions/{sessionId}/files", new() { Headers = auth, Multipart = files })).Ok, Is.True);

        // Signed in once; every viewer carries the same cookies, as ten tabs of one account would.
        await LoginAsync(MemberEmail, MemberPassword);
        var state = await Context.StorageStateAsync();

        var framesFromServer = 0;
        var mediaRequests = 0;
        var counting = false;
        var viewers = new List<(IBrowserContext Context, IPage Page)>();
        try
        {
            for (var i = 0; i < Viewers; i++)
            {
                var context = await Browser.NewContextAsync(new() { StorageState = state });
                await context.RouteAsync("**/cdn.apple-mapkit.com/**", route => route.AbortAsync());
                var page = await context.NewPageAsync();
                page.WebSocket += (_, socket) =>
                {
                    if (socket.Url.Contains("_blazor")) socket.FrameReceived += (_, _) => { if (counting) Interlocked.Increment(ref framesFromServer); };
                };
                page.Request += (_, request) => { if (counting && request.Url.Contains("/media/field-sessions/")) Interlocked.Increment(ref mediaRequests); };
                viewers.Add((context, page));
            }

            await Task.WhenAll(viewers.Select(async v =>
            {
                await v.Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
                await Expect(v.Page.Locator("[data-testid='scrubber']")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            }));

            counting = true;
            // Each presses Play, and half of them scrub somewhere mid-way, as a room would.
            await Task.WhenAll(viewers.Select(async (v, i) =>
            {
                await v.Page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = true }).ClickAsync();
                if (i % 2 == 1)
                {
                    await v.Page.WaitForTimeoutAsync(1500);
                    await v.Page.Locator("[data-testid='scrubber']").FillAsync("500");
                }
            }));
            await viewers[0].Page.WaitForTimeoutAsync(8000);
            counting = false;

            var playheads = await Task.WhenAll(viewers.Select(v => v.Page.EvaluateAsync<JsonElement>(@"async () => {
                const m = await import('/_content/Ben.Web.Website.Library/js/fieldKitPlayer.js');
                const s = m.state(document.querySelector('[data-testid=field-player]'));
                return { playhead: s?.playhead ?? -1, playing: s?.playing ?? false,
                         reconnect: !!document.querySelector('#components-reconnect-modal.components-reconnect-show, #components-reconnect-modal[open]') };
            }")));

            TestContext.Out.WriteLine($"{Viewers} viewers, 8 s: {framesFromServer} frames from the server in all, {mediaRequests} media requests");
            for (var i = 0; i < Viewers; i++)
            {
                var p = playheads[i];
                TestContext.Out.WriteLine($"viewer {i}: playhead {p.GetProperty("playhead").GetDouble():0} ms, playing {p.GetProperty("playing").GetBoolean()}");
                Assert.That(p.GetProperty("reconnect").GetBoolean(), Is.False, $"viewer {i} lost its connection");
                // Scrubbed viewers jumped to 0:30; the others played from the start.
                var least = i % 2 == 1 ? 30_000 : 6_000;
                Assert.That(p.GetProperty("playhead").GetDouble(), Is.GreaterThan(least), $"viewer {i} should have played on");
            }

            // A keep-alive or two each is the connection, not the player.
            Assert.That(framesFromServer, Is.LessThanOrEqualTo(Viewers * 3),
                "ten people watching should cost the server next to nothing while they watch");
        }
        finally
        {
            foreach (var (context, _) in viewers) await context.CloseAsync();
        }
    }
}
