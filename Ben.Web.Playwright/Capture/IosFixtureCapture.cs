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

            // Phase 14c: the door, as somebody who may run it. The scan LOOKS without admitting (checkIn false), so
            // capturing it records no arrival; a code nobody issued captures the refusal's shape.
            await SaveAsync(admin, "/api/me/hosted-event-duties", "hosted-duties");
            var door = await SaveAsync(admin, $"/api/organizations/{orgId}/events/{RoomsEventId}/door", "hosted-door", scrubCodes: true);
            var nightId = JsonDocument.Parse(door).RootElement.GetProperty("nightId").GetString();
            var passToken = (await (await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking/pass")).JsonAsync())!
                .Value.GetProperty("pass").GetProperty("token").GetString();
            await SavePostAsync(admin, $"/api/organizations/{orgId}/events/{RoomsEventId}/bookings/door/scan",
                new { token = passToken, checkIn = false, hostedEventNightId = nightId }, "hosted-scan-admitted");
            await SavePostAsync(admin, $"/api/organizations/{orgId}/events/{RoomsEventId}/bookings/door/scan",
                new { token = "not-a-pass-anybody-issued", checkIn = false, hostedEventNightId = nightId }, "hosted-scan-refused");
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

    /// <summary>
    /// The event-scenario screens (event-scenario-gaps, 10/21/2026): an event's results, who came to its classes,
    /// a guest's conversation with the organizers, and a tour date's room. These four were first written by hand
    /// from the records, before the endpoints had ever run; each is now what the API answered.
    /// </summary>
    /// <remarks>
    /// Run on a database of its own (<c>BEN_E2E_DB=&lt;fresh&gt; scripts/run-e2e.sh --filter Capture_EventScenarioFixtures</c>):
    /// it turns on the seeded weekend's evidence, accepts two files and leaves them, which a shared database
    /// should not be left holding.
    /// </remarks>
    [Test]
    public async Task Capture_EventScenarioFixtures()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE_IOS") != "1")
            Assert.Ignore("Set BEN_CAPTURE_IOS=1 to re-capture the iPhone app's event-scenario fixtures.");

        var orgId = await OrgIdBySlugAsync("paranormal365");
        await using var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await using var guest = await SignedInAsync(ClientEmail, ClientPassword);
        await using var other = await SignedInAsync(SoloEmail, SoloPassword);
        var guestId = await UserIdAsync(guest);
        var otherId = await UserIdAsync(other);
        var events = $"/api/organizations/{orgId}/events/{RoomsEventId}";

        // Both have a confirmed place at the weekend: Daniel's seeded party, and one made for Wren.
        foreach (var (who, id) in new[] { (guest, guestId), (other, otherId) })
        {
            var mine = await who.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
            var confirmed = mine.Ok && (await mine.TextAsync()).Length > 2 && (await mine.JsonAsync())!.Value.GetProperty("status").GetInt32() == 1;
            if (confirmed) continue;
            var made = await admin.PostAsync($"{events}/bookings/on-behalf",
                new() { DataObject = new { leadAppUserId = id, kind = 1, partySize = 1, confirmImmediately = true } });
            Assert.That(made.Ok, Is.True, await made.TextAsync());
        }

        // ── who came to the classes ──────────────────────────────────────────
        // Daniel signs up two, Wren one; Daniel is marked there.
        var programme = JsonDocument.Parse(await (await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}/programme")).TextAsync()).RootElement;
        var sessionId = programme.GetProperty("sessions").EnumerateArray()
            .First(x => x.GetProperty("requiresSignUp").GetBoolean() && !x.GetProperty("isCancelled").GetBoolean())
            .GetProperty("id").GetString();
        foreach (var (who, people) in new[] { (guest, 2), (other, 1) })
        {
            var signUp = await who.PostAsync($"/api/public/hosted-events/{RoomsEventId}/sessions/{sessionId}/sign-up",
                new() { DataObject = new { people } });
            Assert.That(signUp.Ok || signUp.Status == 409, Is.True, await signUp.TextAsync());
        }
        var roster = JsonDocument.Parse(await (await admin.GetAsync($"{events}/sessions/{sessionId}/roster")).TextAsync()).RootElement;
        var danielsLine = roster.GetProperty("in").EnumerateArray().First(l => l.GetProperty("people").GetInt32() == 2).GetProperty("signUpId").GetString();
        var marked = await admin.PutAsync($"{events}/sessions/{sessionId}/sign-ups/{danielsLine}/attended",
            new() { DataObject = new { attended = true } });
        Assert.That(marked.Ok, Is.True, await marked.TextAsync());
        await SaveAsync(admin, $"{events}/sessions/attendance", "hosted-classes");

        // ── writing to the organizers ────────────────────────────────────────
        var asked = await guest.PostAsync($"/api/public/hosted-events/{RoomsEventId}/organizer-thread",
            new() { DataObject = new { body = "Is there parking at the loading dock?" } });
        Assert.That(asked.Ok, Is.True, await asked.TextAsync());
        var answered = await admin.PostAsync($"{events}/guest-messages/{guestId}",
            new() { DataObject = new { body = "Yes, around the back." } });
        Assert.That(answered.Ok, Is.True, await answered.TextAsync());
        await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/organizer-thread", "hosted-organizer-thread", lastMessages: 2);

        // ── the results ──────────────────────────────────────────────────────
        // The weekend takes evidence, asks for votes, and Daniel offers a recording and a photo, both accepted;
        // the recording is confirmed by two people, so it ranks first though it was sent second.
        var ev = System.Text.Json.Nodes.JsonNode.Parse(await (await admin.GetAsync(events)).TextAsync())!.AsObject();
        ev["collectsEvidence"] = true;
        var saved = await admin.PutAsync(events, new() { DataObject = ev });
        Assert.That(saved.Ok, Is.True, await saved.TextAsync());
        var after = await admin.PutAsync($"{events}/after", new()
        {
            DataObject = new
            {
                allowReviews = true, sendThankYou = false, thankYouNote = (string?)null,
                evidenceDays = 3, resultsInDays = 7, asksForVotes = true, setsEvidence = true,
            },
        });
        Assert.That(after.Ok, Is.True, await after.TextAsync());

        var umbrellaId = JsonDocument.Parse(await (await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}")).TextAsync())
            .RootElement.GetProperty("umbrellaEventId").GetString();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var photo = await OfferAsync(guest, umbrellaId!, "Loading dock.jpg", "image/jpeg",
            await File.ReadAllBytesAsync(Path.Combine(fixtures, "room-photo-1.jpg")), note: null);
        var recording = await OfferAsync(guest, umbrellaId!, "Stairwell whisper.mp3", "audio/mpeg",
            await File.ReadAllBytesAsync(Path.Combine(fixtures, "test-audio.mp3")), note: "Second floor, just after two.");
        foreach (var (submission, _) in new[] { photo, recording })
        {
            var reviewed = await admin.PutAsync($"/api/organizations/{orgId}/evidence-submissions/{submission}/review",
                new() { DataObject = new { accept = true, reason = (string?)null } });
            Assert.That(reviewed.Ok, Is.True, await reviewed.TextAsync());
        }
        foreach (var voter in new[] { admin, other })
        {
            var vote = await voter.PostAsync($"/api/evidence-votes/{recording.UploadFileId}", new() { DataObject = new { voteType = 0 /* Confirms */, comment = (string?)null } });
            Assert.That(vote.Ok, Is.True, await vote.TextAsync());
        }
        await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/results", "hosted-results");

        // ── a tour date's room ───────────────────────────────────────────────
        await CaptureTourRoomAsync(admin, guest, guestId);
    }

    /// <summary>
    /// A walk starting in half an hour on the site's first tour, with a guide from its group; Daniel holds a seat,
    /// is scanned in with the pass his letter carries, and the guide and he each post in the room.
    /// </summary>
    private async Task CaptureTourRoomAsync(IAPIRequestContext admin, IAPIRequestContext guest, string guestId)
    {
        var anon = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        // A walk of its own on the seeded group: a fresh database has no tours.
        var orgId = await OrgIdBySlugAsync("paranormal365");
        var addresses = JsonDocument.Parse(await (await admin.GetAsync($"/api/organizations/{orgId}/addresses")).TextAsync()).RootElement;
        Assert.That(addresses.GetArrayLength(), Is.GreaterThan(0), "The seeded group has no address for a walk to start from.");
        var made = await admin.PostAsync($"/api/organizations/{orgId}/tours", new()
        {
            DataObject = new
            {
                name = "Printers Alley After Dark", startOrganizationAddressId = addresses[0].GetProperty("id").GetString(),
                durationMinutes = 120, defaultCapacity = 20,
            },
        });
        Assert.That(made.Ok, Is.True, "the tour was refused: " + await made.TextAsync());
        var tour = JsonDocument.Parse(await made.TextAsync()).RootElement;
        var tourId = tour.GetProperty("id").GetString()!;

        // The guide: a seeded account on that group's roster.
        var rosterText = await (await admin.GetAsync($"/api/organizations/{orgId}/roster")).TextAsync();
        (IAPIRequestContext Api, string Id)? guide = null;
        foreach (var (email, password) in new[] { (UserEmail, UserPassword), (MemberEmail, MemberPassword), (SellerEmail, SellerPassword) })
        {
            var api = await SignedInAsync(email, password);
            var id = await UserIdAsync(api);
            if (rosterText.Contains(id, StringComparison.OrdinalIgnoreCase)) { guide = (api, id); break; }
            await api.DisposeAsync();
        }
        Assert.That(guide, Is.Not.Null, $"No seeded account is on the roster of {orgId}, so nobody can guide its walk.");

        var starts = DateTime.UtcNow.AddMinutes(30);
        var created = await admin.PostAsync($"/api/organizations/{orgId}/calendar", new()
        {
            DataObject = new
            {
                title = tour.GetProperty("name").GetString(), description = (string?)null, location = (string?)null,
                startDateTime = starts, endDateTime = starts.AddHours(2), isAllDay = false, isPublic = true,
                eventTypeId = (Guid?)null, caseId = (Guid?)null, recurrenceRule = (string?)null, tourId,
                guideAppUserIds = new[] { guide!.Value.Id },
            },
        });
        Assert.That(created.Ok, Is.True, "the tour date was refused: " + await created.TextAsync());
        var dateId = (await created.JsonAsync())!.Value.GetProperty("id").GetString()!;

        try
        {
            var added = await admin.PostAsync($"/api/organizations/{orgId}/calendar/{dateId}/attendees",
                new() { DataObject = new { appUserId = guestId, assignedTask = (string?)null } });
            Assert.That(added.Ok, Is.True, "the guest could not be registered: " + await added.TextAsync());
            var seatId = (await added.JsonAsync())!.Value.GetProperty("id").GetString()!;
            var approved = await admin.PostAsync($"/api/organizations/{orgId}/calendar/{dateId}/attendees/{seatId}/approve", new() { DataObject = new { } });
            Assert.That(approved.Ok, Is.True, "the guest's seat could not be confirmed: " + await approved.TextAsync());

            // The pass is only ever in the guest's letter; with no mail server here, the letter waits in the outbox.
            var guestEmail = ClientEmail;
            var outbox = JsonDocument.Parse(await (await admin.GetAsync("/api/admin/mail/outbox?take=200")).TextAsync()).RootElement;
            string? token = null;
            foreach (var letter in outbox.EnumerateArray()
                         .Where(l => string.Equals(l.GetProperty("to").GetString(), guestEmail, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(l => l.GetProperty("createdUtc").GetDateTime()))
            {
                var html = JsonDocument.Parse(await (await admin.GetAsync($"/api/admin/mail/outbox/{letter.GetProperty("id").GetString()}/body")).TextAsync())
                    .RootElement.GetProperty("html").GetString() ?? "";
                if (Regex.Match(html, @"tour-passes/([A-Za-z0-9_\-]+)\.png") is { Success: true } m) { token = m.Groups[1].Value; break; }
            }
            Assert.That(token, Is.Not.Null, "The guest's letter with the pass in it isn't in the outbox.");

            var scanned = await admin.PostAsync($"/api/organizations/{orgId}/events/{dateId}/tour-door/scan", new() { DataObject = new { token } });
            Assert.That(scanned.Ok, Is.True, "the guest could not be scanned in: " + await scanned.TextAsync());

            var fromGuide = await guide.Value.Api.PostAsync($"/api/public/tour-dates/{dateId}/room",
                new() { DataObject = new { body = "Meet at the corner of Church and 4th in five minutes." } });
            Assert.That(fromGuide.Ok, Is.True, await fromGuide.TextAsync());
            var fromGuest = await guest.PostAsync($"/api/public/tour-dates/{dateId}/room",
                new() { DataObject = new { body = "Thanks, everyone. That basement!" } });
            Assert.That(fromGuest.Ok, Is.True, await fromGuest.TextAsync());

            await SaveAsync(guest, $"/api/public/tour-dates/{dateId}/room", "tour-room");
        }
        finally
        {
            await admin.DeleteAsync($"/api/organizations/{orgId}/calendar/{dateId}");
            await admin.PostAsync($"/api/organizations/{orgId}/tours/{tourId}/retire", new() { DataObject = new { } });
            await guide!.Value.Api.DisposeAsync();
            await anon.DisposeAsync();
        }
    }

    /// <summary>Offers one file as evidence at an event, answering the submission and its stored file.</summary>
    private static async Task<(string Id, string UploadFileId)> OfferAsync(
        IAPIRequestContext who, string calendarEventId, string name, string mime, byte[] bytes, string? note)
    {
        var form = who.CreateFormData();
        form.Append("file", new FilePayload { Name = name, MimeType = mime, Buffer = bytes });
        if (note is not null) form.Append("note", note);
        var sent = await who.PostAsync($"/api/events/{calendarEventId}/evidence", new() { Multipart = form });
        Assert.That(sent.Ok, Is.True, $"{name} was refused: {await sent.TextAsync()}");
        var json = (await sent.JsonAsync())!.Value;
        return (json.GetProperty("id").GetString()!, json.GetProperty("uploadFileId").GetString()!);
    }

    private static async Task<string> UserIdAsync(IAPIRequestContext api)
    {
        var me = (await (await api.GetAsync("/api/me")).JsonAsync())!.Value;
        return me.TryGetProperty("userId", out var uid) ? uid.GetString()! : me.GetProperty("id").GetString()!;
    }

    /// <summary>Posts to one endpoint and writes its answer, scrubbed of pass tokens, into the app's fixtures.</summary>
    private static async Task<string> SavePostAsync(IAPIRequestContext api, string path, object payload, string name)
    {
        var response = await api.PostAsync(path, new() { DataObject = payload });
        var body = await response.TextAsync();
        Assert.That(response.Ok, Is.True, $"{path}: {response.Status} {body}");
        return await WriteAsync(body, name);
    }

    /// <summary>Reads one endpoint and writes its body, scrubbed of pass tokens, into the app's fixtures.</summary>
    private static async Task<string> SaveAsync(IAPIRequestContext api, string path, string name, bool onePerStatus = false,
        string? onlyMessage = null, bool scrubCodes = false, int? lastMessages = null)
    {
        var response = await api.GetAsync(path);
        var body = await response.TextAsync();
        Assert.That(response.Ok, Is.True, $"{path}: {response.Status} {body}");

        // A door lists the last six characters of every party's pass. They are not credentials, but a public
        // repository is no place for them either: each becomes a made-up code, the guest's matching the scrubbed token.
        if (scrubCodes)
        {
            var door = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
            // A sample of the list, not the harness's every test party: the guest and two others.
            var sample = door["expected"]!.AsArray()
                .OrderByDescending(p => p!["leadName"]!.GetValue<string>() == "Daniel Park")
                .Take(3).Select(p => p!.DeepClone()).ToList();
            door["expected"] = new System.Text.Json.Nodes.JsonArray([.. sample]);
            var n = 0;
            foreach (var party in door["expected"]!.AsArray())
                if (party!["code"] is not null)
                    party["code"] = party["leadName"]!.GetValue<string>() == "Daniel Park" ? "-TOKEN" : $"CODE{++n:00}";
            body = door.ToJsonString();
        }

        // A room on a harness database holds every earlier run's posts, by other test accounts; keep the one made here.
        if (onlyMessage is not null)
        {
            var room = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
            var kept = room["messages"]!.AsArray().Where(m => m!["id"]!.GetValue<string>() == onlyMessage).Select(m => m!.DeepClone());
            room["messages"] = new System.Text.Json.Nodes.JsonArray([.. kept]);
            body = room.ToJsonString();
        }

        // A conversation on a harness database can hold an earlier run's lines; keep the exchange made here.
        if (lastMessages is int keep)
        {
            var thread = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
            var all = thread["messages"]!.AsArray();
            thread["messages"] = new System.Text.Json.Nodes.JsonArray([.. all.Skip(Math.Max(0, all.Count - keep)).Select(m => m!.DeepClone())]);
            body = thread.ToJsonString();
        }

        if (onePerStatus)
        {
            var kept = JsonDocument.Parse(body).RootElement.EnumerateArray()
                .GroupBy(e => e.GetProperty("status").GetInt32())
                .Select(g => g.First().GetRawText());
            body = "[" + string.Join(",", kept) + "]";
        }

        return await WriteAsync(body, name);
    }

    private static async Task<string> WriteAsync(string body, string name)
    {
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
