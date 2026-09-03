using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// One timeline: while a recording plays, the readings, room and map follow ITS clock.
/// </summary>
/// <remarks>
/// Ben's rule for the player: whatever the session recorded plays along the same timeline. Before
/// this the audio element and the page's playhead were two unrelated clocks — press Play on the
/// page and the readings advanced while the audio sat silent; press play on the audio and the
/// readings sat still.
/// </remarks>
[TestFixture]
[Category("FieldSessionMediaClock")]
public class FieldSessionMediaClockTests : BenTestBase
{
    /// <summary>A real, decodable recording: PCM silence, which every browser plays.</summary>
    private static byte[] Wav(int seconds, int rate = 8000)
    {
        var samples = seconds * rate;
        var data = new byte[samples * 2];
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
    /// Twenty seconds of session, audio started four seconds in: the reading at +6 s carries the
    /// reference with an offset of two seconds into the file.
    /// </summary>
    private static string Document()
    {
        var start = new DateTime(2026, 8, 25, 2, 5, 7, DateTimeKind.Utc);
        var readings = new StringBuilder();
        for (var t = 0; t <= 20; t += 2)
        {
            if (readings.Length > 0) readings.Append(',');
            var at = start.AddSeconds(t).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            var room = t < 10 ? "Hall" : "Cellar";
            var audioRef = t == 6 ? ",\"audio_ref\":{\"filename\":\"media/audio-001.wav\",\"start_offset_seconds\":2}" : "";
            readings.Append("{\"at\":\"" + at + "\",\"triggered_by\":\"interval\","
                          + "\"measurements\":{\"emf\":{\"value\":" + (48 + t) + ",\"unit\":\"uT\",\"baseline\":48.0},"
                          + "\"room\":{\"value\":\"" + room + "\"}}" + audioRef + "}");
        }
        return $$$"""
            {"format_version":"1.0.0","device":{"manufacturer":"Apple","model":"iPhone17,1"},
             "session":{"started_at":"2026-08-25T02:05:07.000Z","ended_at":"2026-08-25T02:05:27.000Z",
                        "location_label":"Media clock check","trigger":{"mode":"hybrid","interval_seconds":2}},
             "readings":[{{{readings}}}]}
            """;
    }

    [Test]
    public async Task Pressing_play_on_the_page_plays_the_recording_and_the_readings_follow_it()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True);
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(Document()) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var upload = await api.PostAsync("/api/field-sessions/document", new() { Headers = auth, Multipart = form });
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString();

        var wav = Wav(seconds: 16);
        var files = Context.APIRequest.CreateFormData();
        files.Append("file", new FilePayload { Name = "audio-001.wav", MimeType = "audio/wav", Buffer = wav });
        files.Append("relativePath", "media/audio-001.wav");
        files.Append("sha256", Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant());
        var attach = await api.PostAsync($"/api/field-sessions/{sessionId}/files", new() { Headers = auth, Multipart = files });
        Assert.That(attach.Ok, Is.True, await attach.TextAsync());

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText("Media clock check").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The row says where the recording sits: it began four seconds into the session.
        await Expect(Page.Locator("[data-testid='media-starts-at']")).ToContainTextAsync("0:04");

        // Play from the page. The recording covers 0:04 onward and the playhead is at 0:00, so the
        // page seeks the audio to its start and lets it drive.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Play" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='media-clock']")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Page.WaitForTimeoutAsync(3000);
        var audioTime = await Page.EvaluateAsync<double>("() => document.querySelector('audio').currentTime");
        var elapsed = (await Page.Locator("[data-testid='elapsed']").InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"audio at {audioTime:0.0}s, page shows {elapsed}");
        Assert.That(audioTime, Is.GreaterThan(1.0), "the recording should actually be playing");
        // Page time = 4 s (audio start) + audio time; allow the two clocks a second of slack.
        // "m:ss" or "mm:ss" or "h:mm:ss" — read it as parts rather than guessing the format.
        var parts = elapsed.Split(':').Select(int.Parse).ToArray();
        var shown = parts.Reverse().Select((v, i) => v * Math.Pow(60, i)).Sum();
        Assert.That(shown, Is.EqualTo(4 + audioTime).Within(1.5), "the page's playhead should follow the recording's clock");

        // The readings followed too: past ten seconds the room is the Cellar.
        if (shown >= 10)
            await Expect(Page.Locator("[data-testid='current-room']")).ToContainTextAsync("Cellar");

        // Pausing the recording itself pauses the page.
        await Page.EvaluateAsync("() => document.querySelector('audio').pause()");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Play" })).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }
}
