using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Captures what the API really answers for a guest's hosted-event screens, as the iPhone app's test fixtures
/// (item 235 phase 14).
/// </summary>
/// <remarks>
/// <para>The app's rule: a fixture is only worth anything while it is something the API really answered — an
/// invented one once shipped a feature that could not decode the real response. So these are written by running
/// the real endpoints on the harness database, as the seeded guest with a confirmed booking at the seeded rooms
/// weekend.</para>
///
/// <para>Runs only with <c>BEN_CAPTURE_IOS=1</c>, through <c>scripts/run-e2e.sh</c>. The pass token is replaced with a
/// placeholder before it is written: a fixture in a public repository must not carry a working credential, even for
/// a throwaway database.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
public sealed class IosFixtureCapture : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    [Test]
    public async Task Capture_HostedEventFixtures()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE_IOS") != "1")
            Assert.Ignore("Set BEN_CAPTURE_IOS=1 to re-capture the iPhone app's hosted-event fixtures.");

        var orgId = await OrgIdBySlugAsync("paranormal365");
        await using var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await using var guest = await SignedInAsync(ClientEmail, ClientPassword);

        string? madeBookingId = null, signedUpSession = null, addedFileId = null, postedMessageId = null;
        var mine = await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
        var confirmed = mine.Ok && (await mine.TextAsync()).Length > 2 && (await mine.JsonAsync())!.Value.GetProperty("status").GetInt32() == 1;
        if (!confirmed)
        {
            if (mine.Ok && (await mine.TextAsync()).Length > 2)
                await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
            var me = (await (await guest.GetAsync("/api/me")).JsonAsync())!.Value;
            var guestId = me.TryGetProperty("userId", out var uid) ? uid.GetString() : me.GetProperty("id").GetString();
            var made = await admin.PostAsync($"/api/organizations/{orgId}/events/{RoomsEventId}/bookings/on-behalf",
                new() { DataObject = new { leadAppUserId = guestId, kind = 1, partySize = 2, confirmImmediately = true } });
            Assert.That(made.Ok, Is.True, await made.TextAsync());
            madeBookingId = (await made.JsonAsync())!.Value.GetProperty("id").GetString();
        }

        try
        {
            var booking = (await (await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking")).JsonAsync())!.Value;
            var bookingId = booking.GetProperty("id").GetString();
            // A confirmed booking made on the board may not have its pass yet.
            await admin.PostAsync($"/api/organizations/{orgId}/events/{RoomsEventId}/bookings/{bookingId}/pass", new() { DataObject = new { } });

            var hosted = await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}", "hosted-event");
            var umbrellaId = JsonDocument.Parse(hosted).RootElement.GetProperty("umbrellaEventId").GetString();

            // One of each status at most: a harness database accumulates hundreds of released test bookings, and a
            // fixture is a sample of the shape, not a copy of the table.
            await SaveAsync(guest, "/api/public/hosted-events/mine", "hosted-mine", onePerStatus: true);
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/my-booking/pass", "hosted-pass");
            await SaveAsync(guest, $"/api/public/events/{umbrellaId}", "hosted-umbrella-event");
            // Phase 14b: the screens during the event need something in them — an empty list decodes whatever
            // shape the elements have. So the guest takes a place in a session, the organizer adds a file for
            // guests, and the guest posts a photo to the room. All three are undone below.
            var programme = JsonDocument.Parse(await (await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}/programme")).TextAsync()).RootElement;
            signedUpSession = programme.GetProperty("sessions").EnumerateArray()
                .First(s => s.GetProperty("requiresSignUp").GetBoolean() && !s.GetProperty("isCancelled").GetBoolean())
                .GetProperty("id").GetString();
            if (programme.GetProperty("sessions").EnumerateArray().First(s => s.GetProperty("id").GetString() == signedUpSession)
                    .GetProperty("mine").ValueKind == JsonValueKind.Null)
            {
                var signUp = await guest.PostAsync($"/api/public/hosted-events/{RoomsEventId}/sessions/{signedUpSession}/sign-up",
                    new() { DataObject = new { people = 1 } });
                Assert.That(signUp.Ok, Is.True, await signUp.TextAsync());
            }
            else signedUpSession = null;

            var fileForm = admin.CreateFormData();
            fileForm.Append("file", new FilePayload { Name = "Guest pack.txt", MimeType = "text/plain", Buffer = "Doors open at seven."u8.ToArray() });
            fileForm.Append("folder", "Before you come");
            fileForm.Append("description", "Parking, doors and what to bring.");
            fileForm.Append("audience", "1");
            var added = await admin.PostAsync($"/api/organizations/{orgId}/events/{RoomsEventId}/files", new() { Multipart = fileForm });
            Assert.That(added.Ok, Is.True, await added.TextAsync());
            addedFileId = JsonDocument.Parse(await added.TextAsync()).RootElement.EnumerateArray()
                .Last(f => f.GetProperty("fileName").GetString() == "Guest pack.txt").GetProperty("id").GetString();

            var postForm = guest.CreateFormData();
            postForm.Append("body", "The stairs, just after ten.");
            postForm.Append("media", new FilePayload { Name = "stairs.png", MimeType = "image/png", Buffer = TinyPng() });
            postForm.Append("sendToHosts", "false");
            postForm.Append("agreeToShow", "true");
            var posted = await guest.PostAsync($"/api/public/hosted-events/{RoomsEventId}/room", new() { Multipart = postForm });
            Assert.That(posted.Ok, Is.True, await posted.TextAsync());
            postedMessageId = JsonDocument.Parse(await posted.TextAsync()).RootElement.GetProperty("messages").EnumerateArray()
                .First(m => m.GetProperty("isMine").GetBoolean() && m.GetProperty("hasMedia").GetBoolean()).GetProperty("id").GetString();

            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/programme", "hosted-programme");
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/menus", "hosted-menus");
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/files", "hosted-files");
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/room", "hosted-room", onlyMessage: postedMessageId);
        }
        finally
        {
            if (postedMessageId is not null)
                await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/room/messages/{postedMessageId}");
            if (addedFileId is not null)
                await admin.DeleteAsync($"/api/organizations/{orgId}/events/{RoomsEventId}/files/{addedFileId}");
            if (signedUpSession is not null)
                await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/sessions/{signedUpSession}/sign-up");
            if (madeBookingId is not null)
                await admin.PostAsync($"/api/organizations/{orgId}/events/{RoomsEventId}/bookings/{madeBookingId}/cancel",
                    new() { DataObject = new { decisionNote = "Clearing up after the fixtures." } });
        }
    }

    /// <summary>Reads one endpoint and writes its body, scrubbed of pass tokens, into the app's fixtures.</summary>
    private static async Task<string> SaveAsync(IAPIRequestContext api, string path, string name, bool onePerStatus = false,
        string? onlyMessage = null)
    {
        var response = await api.GetAsync(path);
        var body = await response.TextAsync();
        Assert.That(response.Ok, Is.True, $"{path}: {response.Status} {body}");

        // A room on a harness database holds every earlier run's posts, by other test accounts; keep the one made here.
        if (onlyMessage is not null)
        {
            var room = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
            var kept = room["messages"]!.AsArray().Where(m => m!["id"]!.GetValue<string>() == onlyMessage).Select(m => m!.DeepClone());
            room["messages"] = new System.Text.Json.Nodes.JsonArray([.. kept]);
            body = room.ToJsonString();
        }

        if (onePerStatus)
        {
            var kept = JsonDocument.Parse(body).RootElement.EnumerateArray()
                .GroupBy(e => e.GetProperty("status").GetInt32())
                .Select(g => g.First().GetRawText());
            body = "[" + string.Join(",", kept) + "]";
        }

        var scrubbed = Regex.Replace(body, "\"token\":\"[^\"]+\"", "\"token\":\"fixture-pass-token\"");
        scrubbed = Regex.Replace(scrubbed, "event-passes/[A-Za-z0-9_\\-]+\\.png", "event-passes/fixture-pass-token.png");
        var pretty = JsonSerializer.Serialize(JsonDocument.Parse(scrubbed).RootElement, new JsonSerializerOptions { WriteIndented = true });

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        var file = Path.Combine(root!.FullName, "Ben.iOS", "BenKit", "Tests", "BenKitTests", "Fixtures", $"{name}.json");
        await File.WriteAllTextAsync(file, pretty + "\n");
        return scrubbed;
    }

    private async Task<IAPIRequestContext> SignedInAsync(string email, string password)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");
}
