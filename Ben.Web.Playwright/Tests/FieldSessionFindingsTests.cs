using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// EVPs and enhanced clips on a session's page, with a certificate of authenticity (10/28/2026).
/// </summary>
/// <remarks>
/// Ben: the .ben is never edited. Its owner runs the audio through our analyzer, marks the EVPs
/// and what they believe each says, and may enhance it; an enhanced clip is attached from outside,
/// keeps its time in the session, says it was enhanced, and carries proof it came from the
/// original by our own processing.
/// </remarks>
[TestFixture]
[Category("FieldSessionEngine")]
public class FieldSessionFindingsTests : Oct16TestBase
{
    /// <summary>The stage fixture as a session of its own: a new session id, the same sealed recordings.</summary>
    private static (byte[] Bytes, string SessionId) FreshStageBundle()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben"));
        var sessionId = Guid.NewGuid().ToString().ToUpperInvariant();
        using var output = new MemoryStream();
        using (var from = new ZipArchive(new MemoryStream(source)))
        using (var to = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in from.Entries)
            {
                using var read = entry.Open();
                using var buffer = new MemoryStream();
                read.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (entry.FullName == "seal.json")
                {
                    var seal = JsonNode.Parse(bytes)!.AsObject();
                    seal["session_id"] = sessionId;
                    bytes = Encoding.UTF8.GetBytes(seal.ToJsonString());
                }
                using var write = to.CreateEntry(entry.FullName, CompressionLevel.NoCompression).Open();
                write.Write(bytes);
            }
        }
        return (output.ToArray(), sessionId);
    }

    [Test]
    public async Task A_kept_EVP_and_an_enhanced_clip_sit_on_the_timeline_with_a_certificate_that_checks()
    {
        var api = await ApiAsAsync(MemberEmail, MemberPassword);

        var (bundle, deviceSessionId) = FreshStageBundle();
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "field-session.ben", MimeType = "application/vnd.ishaunted.field-session", Buffer = bundle });
        form.Append("deviceSessionId", deviceSessionId);
        var sent = await OkJsonAsync(await api.PostAsync("/api/field-sessions/bundle", new() { Multipart = form }), "sending the session");
        var sessionId = sent.GetProperty("id").GetString()!;

        var view = await OkJsonAsync(await api.GetAsync($"/api/field-sessions/{sessionId}/analysis"), "reading what may be scanned");
        if (!view.GetProperty("decoderAvailable").GetBoolean())
            Assert.Ignore("This server has no ffmpeg, so it cannot read the phone's .m4a (export MediaTools__FfmpegPath).");
        var recordingId = view.GetProperty("clips")[0].GetProperty("fileId").GetString()!;

        // The member's own copy of the recording, an EVP kept on it, and a normalized clip of it.
        var copy = await OkJsonAsync(await api.PostAsync($"/api/field-sessions/{sessionId}/clips/{recordingId}/analysis-copy",
            new() { DataObject = new { } }), "making the copy");
        var copyId = copy.GetProperty("copyUploadFileId").GetString()!;
        await OkJsonAsync(await api.PostAsync($"/api/upload-files/{copyId}/audio-markers", new()
        {
            DataObject = new { timeSeconds = 2.5, endSeconds = 3.5, label = "Get out", confidenceLevel = 1 },   // Probable
        }), "keeping an EVP");
        var source = await OkJsonAsync(await api.GetAsync($"/api/upload-files/{copyId}"), "reading the copy");
        var clip = await OkJsonAsync(await api.PostAsync($"/api/upload-files/{copyId}/clip", new()
        {
            DataObject = new
            {
                start = 2.0, end = 4.0, label = "Get out, enhanced", isPublic = false, normalize = true,
                uploadFileTypeId = source.GetProperty("uploadFileTypeId").GetString(),
            },
        }), "saving the enhanced clip");
        var clipId = clip.GetProperty("id").GetString()!;

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.Locator("[data-testid=play-pause]")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The EVP is a mark, saying what was heard; the clip sits under its recording, enhanced.
        await Expect(Page.Locator("[data-testid=evp-row]")).ToContainTextAsync("“Get out”");
        var derived = Page.Locator("[data-testid=derived-row]");
        await Expect(derived).ToHaveCountAsync(1);
        await Expect(derived.Locator("[data-testid=derived-kind]")).ToHaveTextAsync("Enhanced");
        await Expect(derived).ToContainTextAsync("normalized");

        // With the switch on, the enhanced clip is what is heard at its moment.
        await WaitForTheCircuitAsync();
        await Page.Locator("[data-testid=prefer-enhanced]").CheckAsync();
        await Page.WaitForFunctionAsync($"() => {{ const m = document.querySelector('audio[data-fk-media=\"{clipId}\"]'); return !!m && m.duration > 0; }}",
                                        null, new() { Timeout = 20_000 });
        var placedAt = await Page.EvaluateAsync<double>($@"async () => {{
            const m = await import('/_content/Ben.Web.Website.Library/js/fieldKitPlayer.js');
            const tl = JSON.parse(m.timelineJson(document.querySelector('[data-testid=field-player]')));
            return tl.clips.find(c => c.fileId === '{clipId}')?.startMs ?? -1;
        }}");
        Assert.That(placedAt, Is.GreaterThanOrEqualTo(0), "the enhanced clip isn't placed on the timeline");
        await Page.EvaluateAsync($@"() => {{
            const s = document.querySelector('[data-testid=scrubber]');
            s.value = String(({placedAt} + 300) / {await DurationAsync()} * 1000);
            s.dispatchEvent(new Event('input', {{ bubbles: true }}));
            s.dispatchEvent(new Event('change', {{ bubbles: true }}));
        }}");
        await Page.Locator("[data-testid=play-pause]").ClickAsync();
        await Page.WaitForFunctionAsync($"() => !document.querySelector('audio[data-fk-media=\"{clipId}\"]').paused",
                                        null, new() { Timeout = 10_000 });
        Assert.That(await Page.EvaluateAsync<bool>($"() => [...document.querySelectorAll('audio[data-fk-media]')].filter(a => a.dataset.fkMedia !== '{clipId}').every(a => a.paused)"),
                    Is.True, "the original kept sounding under the enhanced clip");
        await Page.Locator("[data-testid=play-pause]").ClickAsync();

        // Its certificate, checked now.
        await derived.Locator("[data-testid=certificate-link]").ClickAsync();
        await Expect(Page.Locator("[data-testid=certificate-kind]")).ToHaveTextAsync("Enhanced", new() { Timeout = 20_000 });
        await Expect(Page.Locator("[data-testid=certificate-steps]")).ToContainTextAsync("matches what the phone sealed");
        await Expect(Page.Locator("[data-testid=certificate-steps]")).ToContainTextAsync("loudest point raised");
        await WaitForTheCircuitAsync();
        await Page.Locator("[data-testid=certificate-check]").ClickAsync();
        await Expect(Page.Locator("[data-testid=certificate-outcome]")).ToHaveAttributeAsync("data-outcome", "verified",
            new() { Timeout = 20_000 });

        // Its bytes can't be swapped under it.
        var replace = Context.APIRequest.CreateFormData();
        replace.Append("file", new FilePayload { Name = "Get out, enhanced.flac", MimeType = "audio/flac", Buffer = [1, 2, 3, 4] });
        var refused = await api.PostAsync($"/api/upload-files/{clipId}/replace", new() { Multipart = replace });
        Assert.That(refused.Status, Is.EqualTo(409), await refused.TextAsync());
    }

    private async Task<double> DurationAsync()
        => await Page.EvaluateAsync<double>(@"async () => {
            const m = await import('/_content/Ben.Web.Website.Library/js/fieldKitPlayer.js');
            return m.state(document.querySelector('[data-testid=field-player]')).duration;
        }");
}
