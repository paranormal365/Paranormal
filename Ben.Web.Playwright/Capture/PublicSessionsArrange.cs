using System.IO.Compression;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Puts a published session in the public archive where the simulator stands, so the app's
/// "Find public sessions" has something to show.
/// </summary>
/// <remarks>
/// <para>Ben, 10/02/2026: the help picture <c>iphone-public-sessions.png</c> was the only one not
/// retaken in the new look, because on the test database nothing had been published near the
/// simulator's location (36.1627, -86.7816, downtown Nashville) and the capture asserts a row.</para>
///
/// <para>Sarah (the suite's ordinary member) sends the browser tests' field session fixture up as
/// her own and publishes it to the public place nearest that point, or to a new one there if none
/// is within the archive's radius. Sarah rather than James: the iPhone capture signs in as James,
/// and a list of public sessions is about other people's.</para>
///
/// <para>Explicit: it publishes to the archive of the database it runs against. Test databases only:
/// <c>scripts/run-e2e.sh --keep --filter "TestCategory=PublicSessionsArrange"</c>.</para>
/// </remarks>
[TestFixture]
[Category("PublicSessionsArrange")]
[Explicit("Publishes a session to the public archive of the database it runs against; test databases only.")]
[NonParallelizable]
public sealed class PublicSessionsArrange : BenTestBase
{
    private const decimal Latitude  = 36.1627m;
    private const decimal Longitude = -86.7816m;

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben");

    [Test]
    public async Task Publish_a_session_where_the_simulator_stands()
    {
        var api = await ApiAsAsync(UserEmail, UserPassword);

        // Sent up as Sarah's own. The fixture's device session id is the phone's, so a second run
        // finds the first upload rather than making another.
        var bytes = await File.ReadAllBytesAsync(Fixture);
        string deviceSessionId;
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        using (var seal = zip.GetEntry("seal.json")!.Open())
            deviceSessionId = JsonDocument.Parse(seal).RootElement.GetProperty("session_id").GetString()!;

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "field-session-stage.ben", MimeType = "application/vnd.ishaunted.field-session", Buffer = bytes });
        form.Append("deviceSessionId", deviceSessionId);
        var upload = await api.PostAsync("/api/field-sessions/bundle", new() { Multipart = form });
        Assert.That(upload.Ok, Is.True, $"upload: {upload.Status} {await upload.TextAsync()}");
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString()!;
        TestContext.Out.WriteLine($"session {sessionId}");

        // The nearest public place the archive would offer, as the app's picker does.
        var candidates = await api.GetAsync(
            $"/api/places/archive-candidates?latitude={Latitude}&longitude={Longitude}");
        Assert.That(candidates.Ok, Is.True, $"candidates: {candidates.Status}");
        // Not a place an earlier test run named: the picture is for the help, and "Playwright Rooms
        // House" was the nearest the first time this ran.
        var all = (await candidates.JsonAsync())!.Value.EnumerateArray().ToList();
        foreach (var c in all) TestContext.Out.WriteLine($"candidate: {c.GetProperty("name").GetString()}");
        var nearest = all.FirstOrDefault(c =>
        {
            var name = c.GetProperty("name").GetString() ?? "";
            return !new[] { "playwright", "test", "e2e", "walk" }
                .Any(junk => name.Contains(junk, StringComparison.OrdinalIgnoreCase));
        });

        // Published already (a second run, or the first run's choice): take it back first, so it
        // can go to the right place.
        await api.DeleteAsync($"/api/field-sessions/{sessionId}/publish");

        object body = nearest.ValueKind == JsonValueKind.Object
            ? new { placeId = nearest.GetProperty("id").GetGuid() }
            : new
            {
                placeId = (Guid?)null,
                newPlace = new
                {
                    name = "Printers Alley", streetAddress1 = (string?)null, city = "Nashville",
                    state = "TN", zipCode = (string?)null, latitude = Latitude, longitude = Longitude,
                },
            };
        TestContext.Out.WriteLine(nearest.ValueKind == JsonValueKind.Object
            ? $"publishing to {nearest.GetProperty("name").GetString()}"
            : "no public place within the radius; naming Printers Alley");

        var publish = await api.PostAsync($"/api/field-sessions/{sessionId}/publish", new() { DataObject = body });
        Assert.That(publish.Ok, Is.True, $"publish: {publish.Status} {await publish.TextAsync()}");
    }

    private async Task<IAPIRequestContext> ApiAsAsync(string email, string password)
    {
        await using var anon = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await anon.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object> { ["email"] = email, ["password"] = password },
        });
        Assert.That(login.Ok, Is.True, $"API sign-in failed for {email}: {login.Status}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }
}
