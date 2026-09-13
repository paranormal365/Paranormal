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

        string? madeBookingId = null;
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
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/programme", "hosted-programme");
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/menus", "hosted-menus");
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/files", "hosted-files");
            await SaveAsync(guest, $"/api/public/hosted-events/{RoomsEventId}/room", "hosted-room");
        }
        finally
        {
            if (madeBookingId is not null)
                await admin.PostAsync($"/api/organizations/{orgId}/events/{RoomsEventId}/bookings/{madeBookingId}/cancel",
                    new() { DataObject = new { decisionNote = "Clearing up after the fixtures." } });
        }
    }

    /// <summary>Reads one endpoint and writes its body, scrubbed of pass tokens, into the app's fixtures.</summary>
    private static async Task<string> SaveAsync(IAPIRequestContext api, string path, string name, bool onePerStatus = false)
    {
        var response = await api.GetAsync(path);
        var body = await response.TextAsync();
        Assert.That(response.Ok, Is.True, $"{path}: {response.Status} {body}");

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
}
