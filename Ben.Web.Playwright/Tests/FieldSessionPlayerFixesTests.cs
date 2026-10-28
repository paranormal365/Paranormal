using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The faults the 10/14/2026 audit found in the session player, each pinned where a person sees it.
/// </summary>
/// <remarks>
/// <para><b>Scrubbing after a pause.</b> A paused recording kept its place as the clock, so the
/// <c>timeupdate</c> its own seek fired moved the playhead to the recording's time — clamped to its
/// first second when the scrub left it. The thumb jumped back to wherever the recording began.</para>
///
/// <para><b>A clip recorded on its own.</b> Its reference carries the clip's length and no offset,
/// because the phone writes it when the clip ends. Read as an offset of zero it sat one whole clip
/// late.</para>
///
/// <para><b>A recording past the last reading.</b> The session ended at its last READING, so a
/// recording that ran on past it had a tail nobody could reach.</para>
/// </remarks>
[TestFixture]
[Category("FieldSessionPlayerFixes")]
public class FieldSessionPlayerFixesTests : BenTestBase
{
    private static readonly DateTime Start = new(2026, 8, 25, 2, 5, 7, DateTimeKind.Utc);

    private static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    /// <summary>PCM silence: every browser decodes it.</summary>
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

    /// <summary>A reading every two seconds from 0 to <paramref name="lastSecond"/>, with an audio
    /// reference on the one at <paramref name="refAt"/>.</summary>
    private static string Document(string label, int lastSecond, int refAt, string audioRef, bool withEnd)
    {
        var readings = new StringBuilder();
        for (var t = 0; t <= lastSecond; t += 2)
        {
            if (readings.Length > 0) readings.Append(',');
            var reference = t == refAt ? ",\"audio_ref\":" + audioRef : "";
            readings.Append("{\"at\":\"" + Stamp(Start.AddSeconds(t)) + "\",\"triggered_by\":\"interval\","
                          + "\"measurements\":{\"emf\":{\"value\":" + (48 + t) + ",\"unit\":\"uT\",\"baseline\":48.0}}"
                          + reference + "}");
        }
        var end = withEnd ? ",\"ended_at\":\"" + Stamp(Start.AddSeconds(lastSecond)) + "\"" : "";
        return "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone17,1\"},"
             + "\"session\":{\"started_at\":\"" + Stamp(Start) + "\"" + end
             + ",\"location_label\":\"" + label + "\",\"trigger\":{\"mode\":\"hybrid\",\"interval_seconds\":2}},"
             + "\"readings\":[" + readings + "]}";
    }

    /// <summary>Uploads the document and one WAV, signs in, and opens the player on it.</summary>
    private async Task OpenAsync(string document, string label, string relativePath, int wavSeconds)
    {
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
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString();

        var wav = Wav(wavSeconds);
        var files = Context.APIRequest.CreateFormData();
        files.Append("file", new FilePayload { Name = Path.GetFileName(relativePath), MimeType = "audio/wav", Buffer = wav });
        files.Append("relativePath", relativePath);
        files.Append("sha256", Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant());
        var attach = await api.PostAsync($"/api/field-sessions/{sessionId}/files", new() { Headers = auth, Multipart = files });
        Assert.That(attach.Ok, Is.True, await attach.TextAsync());

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText(label).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // The recording's length arrives from the browser, and placement and the session's end
        // can depend on it; wait until the element has read it.
        await Page.WaitForFunctionAsync(
            "() => { const a = document.querySelector('audio'); return !!a && a.duration > 0; }",
            null, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// The recording has finished its own seek and the page has drawn a few frames since: the moment
    /// the old clock pulled the playhead back to where the recording said it was.
    /// </summary>
    private async Task SeekSettledAsync()
    {
        await Page.WaitForFunctionAsync("() => { const a = document.querySelector('audio'); return !!a && !a.seeking; }",
            null, new() { Timeout = 10_000, PollingInterval = 50 });
        await Page.EvaluateAsync("() => new Promise(r => { let n = 0; const f = () => ++n >= 5 ? r() : requestAnimationFrame(f); requestAnimationFrame(f); })");
    }

    private async Task<double> ElapsedSecondsAsync()
    {
        var text = (await Page.Locator("[data-testid='elapsed']").InnerTextAsync()).Trim();
        return text.Split(':').Select(int.Parse).Reverse().Select((v, i) => v * Math.Pow(60, i)).Sum();
    }

    [Test]
    public async Task Scrubbing_after_a_pause_stays_where_it_was_dragged()
    {
        const string label = "Scrub after pause";
        // Twenty seconds of session; the recording begins at 0:04 (offset 2 s on the reading at 0:06).
        await OpenAsync(Document(label, 20, 6, "{\"filename\":\"media/audio-001.wav\",\"start_offset_seconds\":2}", withEnd: true),
                        label, "media/audio-001.wav", 16);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Play" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='media-clock']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await WaitForPlayerAsync("s.playing && s.playhead > 6000");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Pause" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Play" })).ToBeVisibleAsync(new() { Timeout = 5_000 });

        var scrubber = Page.Locator("input[type=range]");

        // Inside the recording: 900/1000 of twenty seconds is 0:18.
        await scrubber.FillAsync("900");
        await SeekSettledAsync();
        Assert.That(await ElapsedSecondsAsync(), Is.EqualTo(18).Within(1),
            "a paused recording's own seek must not move the playhead it was seeked to");

        // Before the recording begins: 0:02. The old clock clamped its seek to the recording's
        // first second and reported 0:04 back.
        await scrubber.FillAsync("100");
        await SeekSettledAsync();
        Assert.That(await ElapsedSecondsAsync(), Is.EqualTo(2).Within(1),
            "scrubbing outside the recording must not land on the recording's first second");
    }

    [Test]
    public async Task A_clip_recorded_on_its_own_sits_where_it_began()
    {
        const string label = "Clip on its own";
        // The capture button's reference: written on the reading at 0:20 when the 16-second clip
        // ENDED, with its length and no offset. It began at 0:04.
        await OpenAsync(Document(label, 24, 20, "{\"filename\":\"media/audio-002.wav\",\"media_type\":\"audio/mp4\",\"duration_seconds\":16}", withEnd: true),
                        label, "media/audio-002.wav", 16);

        await Expect(Page.Locator("[data-testid='media-starts-at']")).ToContainTextAsync("0:04");
    }

    [Test]
    public async Task A_recording_that_runs_past_the_last_reading_can_be_reached()
    {
        const string label = "Past the last reading";
        // Readings for ten seconds, no end time (the session was interrupted), and a 16-second
        // recording from 0:00. The session runs as long as what it recorded: 0:16.
        await OpenAsync(Document(label, 10, 0, "{\"filename\":\"media/audio-003.wav\",\"start_offset_seconds\":0}", withEnd: false),
                        label, "media/audio-003.wav", 16);

        var total = Page.Locator("[data-testid='elapsed'] + span");
        await Expect(total).ToContainTextAsync("0:16", new() { Timeout = 10_000 });
    }
}
