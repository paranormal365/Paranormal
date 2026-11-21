using System.Text;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Putting a field session in a room of its place (Ben, 11/21/2026): "I recorded on the Paranormal Platform and
/// want to attach it to my room at the site." Sarah publishes a session to a public place, puts it in one of her
/// group's rooms there from the session's page, or types a room the place doesn't have, and the place's page and
/// the public playback say which room it was.
/// </summary>
[TestFixture]
[Category("FieldSessionEngine")]
public class SessionRoomTests : Oct16TestBase
{
    private const string OrgName = "Playwright Session Rooms";
    private IAPIRequestContext _sarah = null!;
    private string? _orgId;
    private string _placeId = null!;
    private string _placeName = null!;
    private string _roomId = null!;

    private static readonly DateTime Start = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc);
    private static string At(int seconds) => Start.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    /// <summary>A minute of readings as the phone writes them.</summary>
    private static string Document(string label)
    {
        var readings = new StringBuilder();
        for (var t = 0; t <= 60; t += 5)
        {
            if (readings.Length > 0) readings.Append(',');
            readings.Append("{\"at\":\"" + At(t) + "\",\"measurements\":{\"emf\":{\"value\":" + (48 + t % 7)
                + ",\"unit\":\"uT\",\"baseline\":48}}}");
        }
        return "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone16,2\"},"
             + "\"session\":{\"started_at\":\"" + At(0) + "\",\"ended_at\":\"" + At(60) + "\",\"location_label\":\"" + label + "\"},"
             + "\"readings\":[" + readings + "]}";
    }

    /// <summary>Sends a session as Sarah and publishes it to the test's place.</summary>
    private async Task<string> PublishedSessionAsync(string label)
    {
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(Document(label)) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var sent = await OkJsonAsync(await _sarah.PostAsync("/api/field-sessions/document", new() { Multipart = form }), "sending the session");
        var id = sent.GetProperty("id").GetString()!;
        var published = await _sarah.PostAsync($"/api/field-sessions/{id}/publish", new() { DataObject = new { placeId = _placeId } });
        Assert.That(published.Ok, Is.True, await published.TextAsync());
        return id;
    }

    /// <summary>A public place of its own, and one room Sarah's throwaway group names there (kept off the page).</summary>
    [SetUp]
    public async Task APublicPlaceWithARoom()
    {
        _sarah = await ApiAsAsync(UserEmail, UserPassword);

        // The place arrives the way a free individual's does: named while publishing a first session.
        _placeName = $"PW Rooms Hall {Guid.NewGuid():N}"[..24];
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(Document("first")) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var first = await OkJsonAsync(await _sarah.PostAsync("/api/field-sessions/document", new() { Multipart = form }), "sending the first session");
        var firstId = first.GetProperty("id").GetString()!;
        var lat = 30 + Random.Shared.NextDouble();
        var named = await _sarah.PostAsync($"/api/field-sessions/{firstId}/publish", new()
        {
            DataObject = new { newPlace = new { name = _placeName, city = "Nashville", state = "TN", latitude = lat, longitude = -100.0 } },
        });
        Assert.That(named.Ok, Is.True, await named.TextAsync());
        var mine = await OkJsonAsync(await _sarah.GetAsync($"/api/field-sessions/{firstId}/room"), "reading the first session's room");
        _placeId = mine.GetProperty("placeId").GetString()!;

        var register = await _sarah.PostAsync("/api/security/organizations/register", new()
        {
            DataObject = new { name = OrgName, urlName = $"pw-srooms-{Guid.NewGuid():N}"[..20], kind = 0 },
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());
        _orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        var room = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/places/{_placeId}/rooms", new()
        {
            DataObject = new { name = "Room 217", floor = "Second floor", isPublic = false },
        }), "naming the room");
        _roomId = room.GetProperty("id").GetString()!;
    }

    [TearDown]
    public async Task TakeTheGroupAwayAgain()
    {
        if (_orgId is null) return;
        var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        // A purge, because the group has a room; the session keeps the room's name, not the link.
        var purged = await admin.DeleteAsync($"/api/admin/organizations/{_orgId}/purge", new() { DataObject = new { confirmName = OrgName } });
        if (!purged.Ok) await admin.DeleteAsync($"/api/organizations/{_orgId}");
    }

    [Test]
    public async Task A_session_put_in_a_room_shows_with_that_room_on_the_place_s_page_and_its_playback()
    {
        var label = $"Room night {Guid.NewGuid():N}"[..20];
        var id = await PublishedSessionAsync(label);

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{id}");
        var picker = Page.Locator("[data-testid=session-room]");
        await Expect(picker).ToContainTextAsync("No room yet", new() { Timeout = 20_000 });
        await picker.Locator("[data-testid=session-room-choices] button", new() { HasText = "Room 217" }).ClickAsync();
        await Expect(picker.Locator("[data-testid=session-room-now]")).ToContainTextAsync("Recorded in Room 217");

        // The place's page: the room beside the night, and the room to narrow the archive to.
        await Page.GotoAsync($"{BaseUrl}/places/{_placeId}");
        var rooms = Page.Locator("[data-testid=archive-rooms]");
        // The archive comes in after the rest of the page, and later still on a slow database.
        await Expect(rooms).ToContainTextAsync("Room 217 (1)", new() { Timeout = 60_000 });
        await rooms.GetByRole(AriaRole.Button, new() { Name = "Room 217 (1)" }).ClickAsync();
        await Expect(Page.Locator("[data-testid=archive-room]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid=archive-room]")).ToHaveTextAsync("Room 217");

        // Its public playback says so too.
        await Page.Locator("[data-testid=archive-play]").First.ClickAsync();
        await Expect(Page.Locator("[data-testid=archive-room-banner]")).ToContainTextAsync("Recorded in Room 217", new() { Timeout = 60_000 });
    }

    [Test]
    public async Task A_room_the_place_does_not_have_can_be_typed_and_taken_off_again()
    {
        var label = $"Typed room {Guid.NewGuid():N}"[..20];
        var id = await PublishedSessionAsync(label);

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        var row = Page.Locator("[data-testid=session-row]", new() { HasText = label });
        await Expect(row.Locator("[data-testid=session-row-room]")).ToHaveTextAsync("Say which", new() { Timeout = 20_000 });
        await row.Locator("[data-testid=session-row-room] a").ClickAsync();

        var picker = Page.Locator("[data-testid=session-room]");
        await Expect(picker).ToContainTextAsync("No room yet", new() { Timeout = 20_000 });
        await Page.Locator("#session-room-name").FillAsync("The bell tower");
        await Page.Locator("#session-room-save").ClickAsync();
        await Expect(picker.Locator("[data-testid=session-room-now]")).ToContainTextAsync("Recorded in The bell tower");

        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Expect(Page.Locator("[data-testid=session-row]", new() { HasText = label })
            .Locator("[data-testid=session-row-room]")).ToHaveTextAsync("The bell tower", new() { Timeout = 20_000 });

        await Page.GotoAsync($"{BaseUrl}/field-sessions/{id}");
        await Page.Locator("#session-room-clear").ClickAsync();
        await Expect(picker.Locator("[data-testid=session-room-now]")).ToContainTextAsync("No room yet");
    }
}
