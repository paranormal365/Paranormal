using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Playing back a session somebody recorded on their phone.
/// </summary>
/// <remarks>
/// The page reads the device's own document, so what it must never do is render a refusal or an
/// unreadable document as a quiet night. Both of those look identical to "nothing happened",
/// which is the failure this codebase keeps finding.
/// </remarks>
public class FieldSessionPlaybackTests : BenTestBase
{
    [Test]
    public async Task A_session_that_is_not_there_says_so_rather_than_showing_an_empty_recording()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{Guid.NewGuid()}");

        // Waited for, not sampled: the player holds until the circuit is signed in before it
        // asks, so the first paint is a loader. Checking immediately would test the spinner.
        await Expect(Page.Locator("text=/couldn't be loaded|isn't here|no longer have access/i")
                        .First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task The_playback_page_loads_without_killing_the_circuit()
    {
        // A page whose OnInitializedAsync throws takes the circuit down and shows a blank screen
        // — the failure mode this repo has hit more than once, and one curl can never catch.
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{Guid.NewGuid()}");
        await Page.WaitForSelectorAsync("body");

        var reconnect = await Page.Locator("#components-reconnect-modal").CountAsync();
        Assert.That(reconnect, Is.EqualTo(0), "the circuit should still be alive");
    }

    [Test]
    public async Task An_uploaded_session_plays_back_with_its_readings_and_marks()
    {
        // Upload through the API, then open the page — the two halves of the feature meeting.
        // A page tested against hand-made rows would prove nothing about what a phone writes.
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var login = await api.PostAsync("/login", new()
        {
            DataObject = new { email = MemberEmail, password = MemberPassword },
        });
        Assert.That(login.Ok, Is.True, "the seeded member should be able to sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        var deviceSessionId = Guid.NewGuid().ToString();
        var document = """
        {"format_version":"1.0.0",
         "device":{"manufacturer":"Apple","model":"iPhone17,1"},
         "session":{"started_at":"2026-08-25T02:05:07.000Z",
                    "ended_at":"2026-08-25T02:09:07.000Z",
                    "location_label":"Playback check, north wall",
                    "trigger":{"mode":"hybrid","interval_seconds":2}},
         "readings":[
           {"at":"2026-08-25T02:05:07.000Z","triggered_by":"interval",
            "measurements":{"emf":{"value":48.0,"unit":"uT","baseline":48.0}},
            "position":{"latitude":36.1627,"longitude":-86.7816,"accuracy_meters":28},
            "motion":{"heading_degrees":271.5}},
           {"at":"2026-08-25T02:06:07.000Z","triggered_by":"event",
            "measurements":{"marker":{"value":"sentry_emf"},
                            "room":{"value":"Cellar"},
                            "emf":{"value":53.0,"unit":"uT","baseline":48.0}},
            "note":"field moved 50 mG from base"}]}
        """;

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload
        {
            Name = "data.json",
            MimeType = "application/json",
            Buffer = System.Text.Encoding.UTF8.GetBytes(document),
        });
        form.Append("deviceSessionId", deviceSessionId);

        var upload = await api.PostAsync("/api/field-sessions/document", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            Multipart = form,
        });
        Assert.That(upload.Ok, Is.True, $"upload failed: {await upload.TextAsync()}");
        var sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString();

        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{sessionId}");

        // The label the device wrote, the mark it recorded, and the honest accuracy — all read
        // out of the document rather than from anything the server reshaped.
        // The player waits for the circuit to be signed in before it asks the API, so the first
        // paint is a loader rather than the session.
        await Expect(Page.GetByText("Playback check, north wall").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("Magnetic spike").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("±28 m").First).ToBeVisibleAsync();

        // The room the operator named. Nothing else on this page can say which part of the
        // building a mark came from — the accuracy circle covers all of it.
        await Expect(Page.Locator("[data-testid='marker-room']").First).ToBeVisibleAsync();
        Assert.That(await Page.Locator("[data-testid='marker-room']").First.InnerTextAsync(),
                    Is.EqualTo("Cellar"));

        // The trace is drawn, not written, so its presence has to be checked as a drawn path.
        // A chart that renders its axes and no line looks like a quiet night rather than a
        // broken chart, which is the failure worth catching here.
        var tracePaths = await Page.Locator(".k-chart svg path[stroke]:not([stroke='none'])")
            .CountAsync();
        Assert.That(tracePaths, Is.GreaterThan(0),
            "the magnetic trace should actually be drawn, not just its axes");

        await Page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "fieldkit-player.png"), FullPage = true });
        // And it is attributed — the account that sent it is on the page.
        var attributed = await Page.Locator("text=/recorded by|nobody signed in/i").CountAsync();
        Assert.That(attributed, Is.GreaterThan(0),
            "a session should say who recorded it, or say plainly that nobody did");
    }
}