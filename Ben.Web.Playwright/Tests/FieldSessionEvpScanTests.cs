using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// An organizer scans a guest's session for EVPs (phase 9 of the audio plan, 10/27/2026): from the
/// session's page, on the group's own copy, with the guest's session left exactly as it was sent.
/// </summary>
/// <remarks>
/// The guest sends the real device bundle to the seeded weekend, the way the phone sends it; a session
/// filed with an event stays filed, so a second run finds the same one. Everything the test makes in
/// the group's files is deleted at the end.
/// </remarks>
[TestFixture]
[Category("FieldKit")]
[Category("AudioEditor")]
public class FieldSessionEvpScanTests : Oct16TestBase
{
    private const string AudioEntry = "media/audio-001.m4a";

    [Test]
    public async Task An_organizer_scans_a_guests_session_and_keeps_an_EVP_without_touching_it()
    {
        // The guest needs a confirmed place at the event to send it anything: a seat, held and confirmed
        // the way a venue does it, unless they already hold one. A fresh database has none.
        var guest = await ApiAsAsync(ClientEmail, ClientPassword);
        var organizer = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        var booked = await EnsureAConfirmedSeatAsync(guest, organizer);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben"));
        string deviceSessionId;
        byte[] audio;
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            using (var seal = zip.GetEntry("seal.json")!.Open())
                deviceSessionId = JsonDocument.Parse(seal).RootElement.GetProperty("session_id").GetString()!;
            using var entry = zip.GetEntry(AudioEntry)!.Open();
            using var copy = new MemoryStream();
            await entry.CopyToAsync(copy);
            audio = copy.ToArray();
        }

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "field-session.ben", MimeType = "application/vnd.ishaunted.field-session", Buffer = bytes });
        form.Append("deviceSessionId", deviceSessionId);
        form.Append("hostedEventId", SeatsEventId);
        var sent = await OkJsonAsync(await guest.PostAsync("/api/field-sessions/bundle", new() { Multipart = form }),
                                     "sending a session to the event");
        var sessionId = sent.GetProperty("id").GetString()!;

        // The organizer may scan it, for the group.
        var view = await OkJsonAsync(await organizer.GetAsync($"/api/field-sessions/{sessionId}/analysis"), "reading what may be scanned");
        if (!view.GetProperty("mayScan").GetBoolean())
            Assert.Inconclusive($"The organizer seat may not scan the weekend's sessions: {view.GetProperty("message")}");
        if (!view.GetProperty("decoderAvailable").GetBoolean())
            Assert.Ignore("This server has no ffmpeg, so it cannot read the phone's .m4a (export MediaTools__FfmpegPath).");
        var scopeLabel = view.GetProperty("scopes")[0].GetProperty("label").GetString()!;
        Assert.That(scopeLabel, Does.EndWith("'s copy"), "The organizer should work on the group's copy, not one of their own.");
        var recordingId = view.GetProperty("clips")[0].GetProperty("fileId").GetString()!;

        var made = new List<string>();
        try
        {
            // From the session's page: scan every recording, and watch it finish.
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
            var panel = Page.Locator("[data-testid=session-evp-panel]");
            await Expect(panel).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(panel).ToContainTextAsync(scopeLabel);
            await Expect(panel).ToContainTextAsync("The session itself stays exactly as it was sent.");

            await panel.Locator("[data-testid=session-scan]").ClickAsync();
            await Expect(panel.Locator("[data-testid=session-scan-status]"))
                .ToContainTextAsync(new System.Text.RegularExpressions.Regex("to review|Nothing stood out"), new() { Timeout = 120_000 });
            await Expect(panel.Locator("[data-testid=session-evp-counts]").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

            // The editor opens the group's copy, and says so.
            await panel.Locator("[data-testid=session-evp-open]").First.ClickAsync();
            await Page.WaitForURLAsync($"**/field-sessions/{sessionId}/audio/{recordingId}**", new() { Timeout = 30_000 });
            await Expect(Page.Locator("#audio-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            var from = Page.Locator("[data-testid=audio-editor-from-session]");
            await Expect(from).ToContainTextAsync("From the session on");
            await Expect(from).ToContainTextAsync(scopeLabel);

            // Saving an EVP from the copy: the group's file, carrying where it came from.
            var copy = await OkJsonAsync(await organizer.PostAsync(
                $"/api/field-sessions/{sessionId}/clips/{recordingId}/analysis-copy", new() { DataObject = new { } }), "finding the group's copy");
            var copyId = copy.GetProperty("copyUploadFileId").GetString()!;
            made.Add(copyId);
            var source = await OkJsonAsync(await organizer.GetAsync($"/api/upload-files/{copyId}"), "reading the copy");
            var clip = await OkJsonAsync(await organizer.PostAsync($"/api/upload-files/{copyId}/clip", new()
            {
                DataObject = new
                {
                    start = 1.0, end = 3.0, label = "Session EVP test", isPublic = false,
                    uploadFileTypeId = source.GetProperty("uploadFileTypeId").GetString(),
                },
            }), "saving an EVP from the copy");
            var clipId = clip.GetProperty("id").GetString()!;
            made.Insert(0, clipId);
            Assert.That(clip.GetProperty("ownerOrganizationId").ValueKind, Is.EqualTo(JsonValueKind.String),
                "An EVP saved from the group's copy belongs to the group.");
            Assert.That(clip.GetProperty("sourceFieldSessionUploadId").GetString(), Is.EqualTo(sessionId));

            await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
            await Expect(Page.Locator("[data-testid=session-evps]")).ToContainTextAsync("Session EVP test", new() { Timeout = 30_000 });

            // The guest's session is exactly as it was sent: the recording on file is the phone's bytes.
            var served = await guest.GetAsync($"/api/field-sessions/{sessionId}/files/{recordingId}");
            Assert.That(served.Ok, Is.True, "The guest could not play their own recording.");
            Assert.That(SHA256.HashData(await served.BodyAsync()), Is.EqualTo(SHA256.HashData(audio)),
                "The guest's recording changed.");

            // And the group's copy is not in the guest's view of their own session.
            var guestView = await OkJsonAsync(await guest.GetAsync($"/api/field-sessions/{sessionId}/analysis"), "the guest's view");
            Assert.That(guestView.GetProperty("clips")[0].GetProperty("copyUploadFileId").GetString(), Is.Not.EqualTo(copyId));
            Assert.That((await guest.GetAsync($"/api/upload-files/{copyId}")).Ok, Is.False, "The guest can open the group's working copy.");
        }
        finally
        {
            foreach (var id in made)
                await organizer.DeleteAsync($"/api/upload-files/{id}");
            if (booked) await guest.DeleteAsync($"/api/public/hosted-events/{SeatsEventId}/my-booking");
        }
    }

    /// <returns>True when this test made the booking (and so takes it away again).</returns>
    private async Task<bool> EnsureAConfirmedSeatAsync(IAPIRequestContext guest, IAPIRequestContext organizer)
    {
        var mine = await guest.GetAsync($"/api/public/hosted-events/{SeatsEventId}/my-booking");
        if (mine.Ok && (await mine.TextAsync()).Contains("\"status\":1")) return false;
        await guest.DeleteAsync($"/api/public/hosted-events/{SeatsEventId}/my-booking");

        var orgId = await OrgIdBySlugAsync(GroupSlug);
        var layout = await OkJsonAsync(await organizer.GetAsync($"/api/organizations/{orgId}/events/{SeatsEventId}/layout"), "reading the seats");
        var seat = layout.GetProperty("units").EnumerateArray().Last().GetProperty("id").GetString();
        var ev = await OkJsonAsync(await organizer.GetAsync($"/api/organizations/{orgId}/events/{SeatsEventId}"), "reading the event");
        var night = ev.GetProperty("nights").EnumerateArray().First().GetProperty("id").GetString();
        var nights = new[] { new { hostedEventNightId = night, hostedEventLayoutUnitId = seat } };

        var held = await OkJsonAsync(await guest.PostAsync($"/api/public/hosted-events/{SeatsEventId}/holds", new()
        {
            DataObject = new { nights, partySize = 1, firstName = "Daniel", lastName = "Park", phone = "615-555-0142" },
        }), "holding a seat");
        await OkJsonAsync(await organizer.PostAsync(
            $"/api/organizations/{orgId}/events/{SeatsEventId}/bookings/{held.GetProperty("id").GetString()}/confirm",
            new() { DataObject = new { nights, decisionNote = "See you on the night." } }), "confirming the seat");
        return true;
    }
}
