using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Three things the 2026-09-02 sweep found by hand-building uploads the app itself never sends.
/// </summary>
[TestFixture]
[Category("FieldSessionHardening")]
public class FieldSessionHardeningTests : BenTestBase
{
    private const string Document = """
        {"format_version":"1.0.0",
         "device":{"manufacturer":"Apple","model":"iPhone17,1"},
         "session":{"started_at":"2026-08-25T02:05:07.000Z","ended_at":"2026-08-25T02:09:07.000Z",
                    "location_label":"Hardening check, east stair",
                    "trigger":{"mode":"hybrid","interval_seconds":2}},
         "readings":[
           {"at":"2026-08-25T02:05:07.000Z","triggered_by":"interval",
            "measurements":{"emf":{"value":48.0,"unit":"uT","baseline":48.0}},
            "audio_ref":"media/audio-001.m4a"},
           {"at":"2026-08-25T02:06:07.000Z","triggered_by":"event",
            "measurements":{"marker":{"value":"sentry_emf"},"emf":{"value":53.0,"unit":"uT","baseline":48.0}}}]}
        """;

    private async Task<(IAPIRequestContext Api, string Token)> SignedInApiAsync()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = MemberEmail, password = MemberPassword } });
        Assert.That(login.Ok, Is.True, "the seeded member should be able to sign in");
        return (api, (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!);
    }

    private async Task<IAPIResponse> UploadDocumentAsync(IAPIRequestContext api, string token, string document)
    {
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(document) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        // Deliberately no recordedByAppUserId: the hand-built shape that exposed the gap.
        return await api.PostAsync("/api/field-sessions/document", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            Multipart = form,
        });
    }

    [Test]
    public async Task A_document_that_recorded_nothing_is_refused_at_the_door()
    {
        var (api, token) = await SignedInApiAsync();
        var empty = Document.Replace("\"readings\":[", "\"readings\":[],\"_was\":[");

        var upload = await UploadDocumentAsync(api, token, empty);

        Assert.That(upload.Status, Is.EqualTo(400), await upload.TextAsync());
        Assert.That(await upload.TextAsync(), Does.Contain("no readings"));
    }

    /// <summary>
    /// Leaving out who recorded it means the sender did; and a recording whose bytes cannot be
    /// decoded says so on the row instead of offering a dead control.
    /// </summary>
    [Test]
    public async Task The_sender_is_credited_and_an_undecodable_recording_is_named()
    {
        var (api, token) = await SignedInApiAsync();
        var upload = await UploadDocumentAsync(api, token, Document);
        Assert.That(upload.Ok, Is.True, await upload.TextAsync());
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString();

        // Bytes that are not audio at all, with the digest of exactly those bytes: the transport
        // is fine, the content is not. Before this the row showed a silent, dead <audio>.
        var garbage = Encoding.UTF8.GetBytes("this is not an m4a file, however hard the browser tries\n");
        var digest = Convert.ToHexString(SHA256.HashData(garbage)).ToLowerInvariant();
        var files = Context.APIRequest.CreateFormData();
        files.Append("file", new FilePayload { Name = "audio-001.m4a", MimeType = "audio/mp4", Buffer = garbage });
        files.Append("relativePath", "media/audio-001.m4a");
        files.Append("sha256", digest);
        var attach = await api.PostAsync($"/api/field-sessions/{sessionId}/files", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            Multipart = files,
        });
        Assert.That(attach.Ok, Is.True, await attach.TextAsync());

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText("Hardening check, east stair").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Credited to the sender, not "nobody".
        await Expect(Page.GetByText("nobody signed in when recorded")).ToHaveCountAsync(0);
        await Expect(Page.Locator("text=/recorded by/i").First).ToBeVisibleAsync();

        // The browser reads the header (preload=metadata), cannot decode it, and the page says so.
        await Expect(Page.Locator("[data-testid='undecodable']")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("audio")).ToHaveCountAsync(0);
        // The digest matched, so this is NOT reported as damage in transit.
        await Expect(Page.GetByText("arrived damaged")).ToHaveCountAsync(0);
    }
}
