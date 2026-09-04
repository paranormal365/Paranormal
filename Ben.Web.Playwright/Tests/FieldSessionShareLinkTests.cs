using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Handing a session to somebody with no account (item 207).
/// </summary>
/// <remarks>
/// <para>The one thing a unit test cannot show here is the page a stranger actually gets. Every
/// interesting failure of this feature is a rendering failure: an empty map that reads as a night
/// with no signal, a share panel offered to a recipient, a refusal that never paints and leaves a
/// loader spinning for ever. Item #88 shipped exactly that last shape and only a real browser
/// found it.</para>
///
/// <para>The browser signs in for nothing on the shared page. That is the point of the test: the
/// context is deliberately the one a producer has, which is none.</para>
/// </remarks>
[Category("ShareLink")]
public class FieldSessionShareLinkTests : BenTestBase
{
    private const string Document = """
    {"format_version":"1.0.0",
     "device":{"manufacturer":"Apple","model":"iPhone17,1"},
     "session":{"started_at":"2026-09-03T22:00:00.000Z",
                "ended_at":"2026-09-03T22:00:04.000Z",
                "location_label":"Share check, back bedroom",
                "trigger":{"mode":"interval","interval_seconds":1}},
     "readings":[
       {"at":"2026-09-03T22:00:01.000Z","triggered_by":"interval",
        "measurements":{"emf":{"value":48.0,"unit":"uT","baseline":48.0}},
        "position":{"latitude":36.16270000,"longitude":-86.78160000,"accuracy_meters":28}},
       {"at":"2026-09-03T22:00:02.000Z","triggered_by":"interval",
        "measurements":{"emf":{"value":53.0,"unit":"uT","baseline":48.0}},
        "position":{"latitude":36.16280000,"longitude":-86.78150000,"accuracy_meters":28}}]}
    """;

    private IAPIRequestContext _api = null!;
    private string _token = null!;
    private string _sessionId = null!;

    [SetUp]
    public async Task UploadASessionToShare()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var login = await _api.PostAsync("/login", new()
        {
            DataObject = new { email = MemberEmail, password = MemberPassword },
        });
        Assert.That(login.Ok, Is.True, "the seeded member should be able to sign in");
        _token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;

        var form = _api.CreateFormData();
        form.Append("file", new FilePayload
        {
            Name = "data.json",
            MimeType = "application/json",
            Buffer = System.Text.Encoding.UTF8.GetBytes(Document),
        });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());

        var upload = await _api.PostAsync("/api/field-sessions/document", new()
        {
            Headers = Bearer, Multipart = form,
        });
        Assert.That(upload.Ok, Is.True, $"upload failed: {await upload.TextAsync()}");
        _sessionId = (await upload.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    private Dictionary<string, string> Bearer => new() { ["Authorization"] = $"Bearer {_token}" };

    /// <summary>Makes a link through the API, so the browser side starts where a recipient does.</summary>
    private async Task<JsonElement> MakeLinkAsync(bool includePositions = false, string? fileId = null)
    {
        var response = await _api.PostAsync($"/api/field-sessions/{_sessionId}/shares", new()
        {
            Headers = Bearer,
            DataObject = new
            {
                fileId,
                expiresInDays = 7,
                note = "playwright",
                includePositions,
            },
        });
        Assert.That(response.Ok, Is.True, $"could not make a link: {await response.TextAsync()}");
        return (await response.JsonAsync())!.Value;
    }

    [Test]
    public async Task Somebody_with_no_account_can_open_the_link_and_see_the_readings()
    {
        var link = await MakeLinkAsync();
        var token = link.GetProperty("token").GetString();

        // No LoginAsync. A signed-in browser would prove the page works for somebody who never
        // needed the link, which is the one visitor this feature is not for.
        await Page.GotoAsync($"{BaseUrl}/s/{token}");

        await Expect(Page.GetByText("Share check, back bedroom").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The trace is drawn, not merely axed. A chart with axes and no line reads as a quiet
        // night rather than a page that failed to load its document.
        var tracePaths = await Page.Locator(".k-chart svg path[stroke]:not([stroke='none'])").CountAsync();
        Assert.That(tracePaths, Is.GreaterThan(0), "the magnetic trace should actually be drawn");
    }

    [Test]
    public async Task The_recipient_is_told_the_link_expires_and_that_locations_were_withheld()
    {
        var link = await MakeLinkAsync();
        var token = link.GetProperty("token").GetString();

        await Page.GotoAsync($"{BaseUrl}/s/{token}");
        var banner = Page.Locator("[data-testid='share-banner']");
        await Expect(banner).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var text = await banner.InnerTextAsync();
        Assert.That(text, Does.Contain("shared link"));
        Assert.That(text, Does.Contain("stops working"));
        Assert.That(text, Does.Contain("were not shared"),
            "a recipient who is not told the locations were withheld reads the empty map as a "
            + "fact about the night");

        // And the map says the same thing rather than the sentence it uses for a session that
        // genuinely never got a fix. Two different causes, two different truths.
        await Expect(Page.Locator("[data-testid='no-position']")).ToContainTextAsync("weren't shared");
    }

    [Test]
    public async Task The_recipient_is_never_offered_the_machinery_for_sharing_it_onward()
    {
        var link = await MakeLinkAsync();
        var token = link.GetProperty("token").GetString();

        await Page.GotoAsync($"{BaseUrl}/s/{token}");
        await Expect(Page.GetByText("Share check, back bedroom").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.That(await Page.Locator("[data-testid='share-panel']").CountAsync(), Is.EqualTo(0),
            "a recipient must not be handed the panel that makes more links");
    }

    [Test]
    public async Task A_withdrawn_link_says_so_rather_than_spinning_for_ever()
    {
        var link = await MakeLinkAsync();
        var token = link.GetProperty("token").GetString();
        var shareId = link.GetProperty("id").GetString();

        var revoke = await _api.DeleteAsync(
            $"/api/field-sessions/{_sessionId}/shares/{shareId}", new() { Headers = Bearer });
        Assert.That(revoke.Ok, Is.True);

        await Page.GotoAsync($"{BaseUrl}/s/{token}");

        // The failure this exists to catch: a refusal that never paints. A loader that spins for
        // ever is indistinguishable from a broken site, and the recipient has nobody to ask.
        await Expect(Page.Locator("text=/isn't working|expired|withdrawn/i").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task An_unknown_link_gets_the_same_answer_as_a_withdrawn_one()
    {
        await Page.GotoAsync($"{BaseUrl}/s/definitely-not-a-real-token");

        await Expect(Page.Locator("text=/isn't working|expired|withdrawn/i").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task Including_the_locations_puts_the_map_back()
    {
        var link = await MakeLinkAsync(includePositions: true);
        var token = link.GetProperty("token").GetString();

        await Page.GotoAsync($"{BaseUrl}/s/{token}");
        await Expect(Page.GetByText("Share check, back bedroom").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The positive half. Everything else here proves what the link withholds; if the opt-in
        // were broken this suite would still be green and the switch would do nothing.
        var banner = await Page.Locator("[data-testid='share-banner']").InnerTextAsync();
        Assert.That(banner, Does.Not.Contain("were not shared"));
        Assert.That(await Page.Locator("[data-testid='no-position']").CountAsync(), Is.EqualTo(0),
            "a session whose fixes were shared should draw its track");
    }

    [Test]
    public async Task The_owner_can_make_and_withdraw_a_link_from_the_players_own_page()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/field-sessions/{_sessionId}");

        await Expect(Page.Locator("[data-testid='share-panel']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.Locator("[data-testid='share-toggle']").ClickAsync();

        await Page.Locator("#share-note").FillAsync("for the client");
        await Page.Locator("[data-testid='share-create']").ClickAsync();

        var row = Page.Locator("[data-testid='share-row']").First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(row).ToContainTextAsync("for the client");

        // Withdrawing is the half that cannot be added later: a link that can be made and never
        // pulled back is worse than no link at all.
        await Page.Locator("[data-testid='share-revoke']").First.ClickAsync();
        await Expect(Page.Locator("[data-testid='share-row']").First)
            .ToContainTextAsync("withdrawn", new() { Timeout = 20_000 });
    }
}
