using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A session's page says what the latest Paranormal Platform records (10/28/2026): where the
/// session was sent, the time where it was recorded, the questions asked and how long the room
/// was given, the recordings that never arrived, and what its sender or organizer may do with it.
/// </summary>
[TestFixture]
[Category("FieldSessionEngine")]
public class FieldSessionPageTests : Oct16TestBase
{
    private static readonly DateTime Start = new(2026, 10, 23, 12, 48, 0, DateTimeKind.Utc);
    private static string At(int seconds) => Start.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    /// <summary>What one reading carries beyond its field: a mark, a note beside it, anything else.</summary>
    private sealed record Extra(int Seconds, string? Mark = null, string? Note = null, string? Beside = null);

    /// <summary>A minute of readings, marks and notes as the phone writes them.</summary>
    private static string Document(string label, string? zone = null, params Extra[] extras)
    {
        var readings = new StringBuilder();
        for (var t = 0; t <= 60; t += 5)
        {
            if (readings.Length > 0) readings.Append(',');
            var extra = extras.FirstOrDefault(e => e.Seconds == t);
            var mark = extra?.Mark is { } kind ? ",\"marker\":{\"value\":\"" + kind + "\"}" : "";
            var note = extra?.Note is { } text ? ",\"note\":\"" + text + "\"" : "";
            var beside = extra?.Beside is { } more ? "," + more : "";
            readings.Append("{\"at\":\"" + At(t) + "\",\"measurements\":{\"emf\":{\"value\":" + (48 + t % 7)
                + ",\"unit\":\"uT\",\"baseline\":48}" + mark + "}" + note + beside + "}");
        }
        var timezone = zone is null ? "" : ",\"timezone\":\"" + zone + "\"";
        return "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone16,2\"},"
             + "\"session\":{\"started_at\":\"" + At(0) + "\",\"ended_at\":\"" + At(60) + "\",\"location_label\":\"" + label + "\"" + timezone + "},"
             + "\"readings\":[" + readings + "]}";
    }

    private async Task<string> SendDocumentAsync(IAPIRequestContext api, string document)
    {
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(document) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var sent = await OkJsonAsync(await api.PostAsync("/api/field-sessions/document", new() { Multipart = form }), "sending the session");
        return sent.GetProperty("id").GetString()!;
    }

    private async Task OpenAsync(string sessionId, string label, string email, string password)
    {
        await LoginAsync(email, password);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");
        await Expect(Page.GetByText(label).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("[data-testid=play-pause]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    private static string Label(string what) => $"{what} {Guid.NewGuid():N}"[..Math.Min(40, what.Length + 9)];

    /// <summary>
    /// Ben's 10/23 session: the document names its sound recording and the phone never sent it. The
    /// page said nothing, so a night with no sound looked like a broken page.
    /// </summary>
    [Test]
    public async Task A_sound_file_the_phone_never_sent_is_said_by_name()
    {
        var label = Label("No sound");
        var api = await ApiAsAsync(MemberEmail, MemberPassword);
        var id = await SendDocumentAsync(api, Document(label, extras:
            new Extra(5, Beside: "\"audio_ref\":{\"filename\":\"media/audio-001.m4a\",\"start_offset_seconds\":5}")));

        await OpenAsync(id, label, MemberEmail, MemberPassword);
        var missing = Page.Locator("[data-testid=media-missing]");
        await Expect(missing).ToHaveCountAsync(1);
        await Expect(missing).ToContainTextAsync("audio-001.m4a");
        await Expect(missing).ToContainTextAsync("wasn't in what it sent");
    }

    [Test]
    public async Task A_question_says_what_was_asked_and_how_long_the_room_was_given()
    {
        var label = Label("Asked");
        var api = await ApiAsAsync(MemberEmail, MemberPassword);
        var id = await SendDocumentAsync(api, Document(label, extras:
        [
            new Extra(10, Mark: "evp_question", Note: "Is anybody here?"),
            new Extra(25, Mark: "evp_wait_end", Note: "waited 15s after: Is anybody here?"),
        ]));

        await OpenAsync(id, label, MemberEmail, MemberPassword);
        var question = Page.Locator("[data-testid=question-row]");
        await Expect(question).ToContainTextAsync("Asked: “Is anybody here?”");
        await Expect(question.Locator("[data-testid=question-listened]")).ToHaveTextAsync("listened 15 s");
        Assert.That(await Page.Locator("[data-testid=listening-window]").CountAsync(), Is.GreaterThanOrEqualTo(1),
                    "the chart doesn't shade the listening");
    }

    /// <summary>The start time is said where the session was recorded, with the zone named, and the reader can switch.</summary>
    [Test]
    public async Task The_start_is_said_in_the_time_where_it_was_recorded()
    {
        var label = Label("Zone");
        var api = await ApiAsAsync(MemberEmail, MemberPassword);
        var id = await SendDocumentAsync(api, Document(label, zone: "America/Los_Angeles"));

        await OpenAsync(id, label, MemberEmail, MemberPassword);
        var started = Page.Locator("[data-testid=session-started]");
        await Expect(started).ToContainTextAsync("5:48");
        await Expect(started).ToContainTextAsync("PDT");

        // The seeded member reads on another zone: the switch is there, and moves the heading.
        var mine = Page.Locator("[data-testid=time-zone-mine]");
        if (await mine.CountAsync() == 0) Assert.Inconclusive("The seeded member reads on Pacific time already.");
        await mine.ClickAsync();
        await Expect(started).Not.ToContainTextAsync("PDT", new() { Timeout = 10_000 });
        await Page.Locator("[data-testid=time-zone-local]").ClickAsync();   // put the reader's choice back
        await Expect(started).ToContainTextAsync("PDT", new() { Timeout = 10_000 });
    }

    /// <summary>A person's own session can be deleted from its page, not only from the list.</summary>
    [Test]
    public async Task Your_own_session_can_be_deleted_from_its_page()
    {
        var label = Label("Delete me");
        var api = await ApiAsAsync(MemberEmail, MemberPassword);
        var id = await SendDocumentAsync(api, Document(label));

        await OpenAsync(id, label, MemberEmail, MemberPassword);
        await WaitForTheCircuitAsync();
        await Page.Locator("[data-testid=session-actions] [data-testid=delete-session]").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/my-field-sessions", StringComparison.OrdinalIgnoreCase),
                                   new() { Timeout = 20_000 });

        var gone = await api.GetAsync($"/api/field-sessions/{id}?document=false");
        Assert.That(gone.Status, Is.EqualTo(404), "the session is still there");
    }

    /// <summary>
    /// A session sent to an event names the event and links it; its sender may say it can be shown
    /// with the results, and the organizer then sees they may show it — all on the session's page.
    /// </summary>
    [Test]
    public async Task A_session_sent_to_an_event_says_so_and_both_sides_can_act_on_it()
    {
        var guest = await ApiAsAsync(ClientEmail, ClientPassword);
        var organizer = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        var booked = await EnsureAConfirmedSeatAsync(guest, organizer);
        try
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben"));
            string deviceSessionId;
            using (var zip = new ZipArchive(new MemoryStream(bytes)))
            using (var seal = zip.GetEntry("seal.json")!.Open())
                deviceSessionId = JsonDocument.Parse(seal).RootElement.GetProperty("session_id").GetString()!;
            var form = Context.APIRequest.CreateFormData();
            form.Append("file", new FilePayload { Name = "field-session.ben", MimeType = "application/vnd.ishaunted.field-session", Buffer = bytes });
            form.Append("deviceSessionId", deviceSessionId);
            form.Append("hostedEventId", SeatsEventId);
            var sent = await OkJsonAsync(await guest.PostAsync("/api/field-sessions/bundle", new() { Multipart = form }), "sending to the event");
            var id = sent.GetProperty("id").GetString()!;
            await guest.DeleteAsync($"/api/field-sessions/{id}/event-results-consent");   // a run before may have agreed

            // The guest: where it went, linked, and their switch.
            await LoginAsync(ClientEmail, ClientPassword);
            await Page.GotoAsync($"{BaseUrl}/field-sessions/{id}");
            var belongs = Page.Locator("[data-testid=session-belongs]");
            await Expect(belongs).ToContainTextAsync("Sent to", new() { Timeout = 20_000 });
            await Expect(belongs.Locator("[data-testid=session-event]")).ToBeVisibleAsync();
            await WaitForTheCircuitAsync();
            var consent = Page.Locator("[data-testid=session-actions] #show-with-results");
            await Expect(consent).Not.ToBeCheckedAsync();
            await consent.CheckAsync();
            await Expect(consent).ToBeCheckedAsync();
            await Expect(Page.Locator("[data-testid=session-actions-error]")).ToHaveCountAsync(0);

            // The organizer, on the same page: they may now show it.
            await Context.ClearCookiesAsync();
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await Page.GotoAsync($"{BaseUrl}/field-sessions/{id}");
            await Expect(Page.Locator("[data-testid=session-actions] [data-testid=group-session-show]"))
                .ToBeVisibleAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            if (booked) await guest.DeleteAsync($"/api/public/hosted-events/{SeatsEventId}/my-booking");
        }
    }
}
