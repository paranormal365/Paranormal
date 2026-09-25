using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The programme, walked by its host and by a guest (item 235 phase 10).
/// </summary>
/// <remarks>
/// <para>Against the seeded rooms weekend, which has a published programme and a confirmed party of
/// four led by the seeded guest. Everything a run changes is put back: the guest leaves whatever they
/// joined, and the host removes the session it added.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventProgrammeTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private IAPIRequestContext _api = null!;
    private string _orgId = "";
    private string? _madeBookingId;

    [SetUp]
    public async Task StartWithAConfirmedGuestInNoClass()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        // The seed leaves this event in Draft on purpose, and the public endpoints below
        // answer only for a published one. This fixture used to inherit a publish from
        // whichever earlier run happened to do it (item 243); it does its own now.
        await PublishSeededEventAsync(_orgId, RoomsEventId);

        await EnsureTheGuestHasAConfirmedPlaceAsync();
        await LeaveEverythingAsync();
    }

    [TearDown]
    public async Task PutItBack()
    {
        await LeaveEverythingAsync();

        // The day pass this run gave the guest goes again, so the weekend is as the other tests expect.
        if (_madeBookingId is not null && await HeadersAsync(SuperAdminEmail, SuperAdminPassword) is { } admin)
            await _api.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/{_madeBookingId}/cancel",
                new() { Headers = admin, DataObject = new { decisionNote = "Programme test finished." } });

        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_guest_signs_up_and_the_count_says_so()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await OpenTheWeekendAsync();

        var ovilus = Page.Locator(".public-session", new() { HasTextString = "Operating the Ovilus" });
        await Expect(ovilus).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(ovilus.Locator(".session-count")).ToContainTextAsync("0 of 15");

        await ClickUntilAsync(ovilus.Locator(".session-signup"), ovilus.Locator(".session-mine"));
        await Expect(ovilus.Locator(".session-mine")).ToContainTextAsync("You're in");
        await Expect(ovilus.Locator(".session-count")).ToContainTextAsync("1 of 15");

        await ovilus.GetByRole(AriaRole.Button, new() { Name = "Leave" }).ClickAsync();
        await Expect(ovilus.Locator(".session-count")).ToContainTextAsync("0 of 15", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task The_host_adds_a_session_and_it_lands_on_its_night()
    {
        var title = $"Ghost box basics {Guid.NewGuid():N}"[..24];

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/sessions");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#session-title")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#session-title").FillAsync(title);
        await Page.Locator("#session-starts").FillAsync("19:30");
        await Page.Locator("#session-ends").FillAsync("20:15");
        await Page.Locator("#session-location").FillAsync("The library");
        await Page.Locator("#session-capacity").FillAsync("6");
        await ClickUntilAsync(Page.Locator("#session-save"), Page.Locator("#programme-note"));

        var row = Page.Locator(".programme-session", new() { HasTextString = title });
        await Expect(row).ToContainTextAsync("7:30 PM");
        await Expect(row).ToContainTextAsync("0 of 6");

        // Nobody signed up, so it can be removed rather than cancelled.
        await row.GetByRole(AriaRole.Button, new() { Name = "Cancel…" }).ClickAsync();
        await ClickUntilAsync(row.GetByRole(AriaRole.Button, new() { Name = "Remove it instead" }), Page.Locator("#programme-note"));
        await Expect(Page.Locator(".programme-session", new() { HasTextString = title })).ToHaveCountAsync(0);
    }

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_programme_fits_however_narrow_it_is(int width, int height)
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.SetViewportSizeAsync(width, height);
        await OpenTheWeekendAsync();

        var signUp = Page.Locator(".public-session", new() { HasTextString = "Operating the Ovilus" }).Locator(".session-signup");
        await Expect(signUp).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ScrollToAsync(signUp);
        await Expect(signUp).ToBeInViewportAsync(new() { Ratio = 1f });

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the event page slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheWeekendAsync()
    {
        var ev = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}");
        Assert.That(ev.Ok, Is.True, "the seeded rooms weekend is not on the public site");
        var body = (await ev.JsonAsync())!.Value;
        var slug = body.GetProperty("urlName").GetString();

        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{slug}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#hosted-programme")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ScrollToAsync(Page.Locator("#hosted-programme"));
    }

    /// <summary>
    /// The seeded guest with a confirmed place on the weekend. Other tests ask for, withdraw and
    /// cancel their bookings there, so this run makes sure rather than hoping the seed survived.
    /// </summary>
    private async Task EnsureTheGuestHasAConfirmedPlaceAsync()
    {
        var guest = await HeadersAsync(ClientEmail, ClientPassword);
        var admin = await HeadersAsync(SuperAdminEmail, SuperAdminPassword);
        if (guest is null || admin is null) Assert.Ignore("The seeded guest or admin cannot sign in on this deployment.");

        var mine = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking", new() { Headers = guest });
        if (mine.Ok && (await mine.TextAsync()).Length > 2)
        {
            var status = (await mine.JsonAsync())!.Value.GetProperty("status").GetInt32();
            if (status == 1) return;                       // Confirmed
            if (status is 0 or 4)                          // Requested or Held: take it back first
                await _api.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking", new() { Headers = guest });
        }

        var made = await _api.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/on-behalf", new()
        {
            Headers = admin,
            DataObject = new { leadAppUserId = await UserIdAsync(guest), kind = 1, partySize = 2, confirmImmediately = true },
        });
        Assert.That(made.Ok, Is.True, $"could not give the guest a place: {await made.TextAsync()}");
        _madeBookingId = (await made.JsonAsync())!.Value.GetProperty("id").GetString();
    }

    /// <summary>
    /// Scrolls to something the programme may redraw underneath the scroll.
    /// </summary>
    /// <remarks>
    /// The programme renders once for the visitor and again once the circuit knows the guest's
    /// places, so a scroll that starts on the first render's element finds it gone. The locator
    /// finds the new one on the next try.
    /// </remarks>
    private async Task ScrollToAsync(ILocator target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await target.ScrollIntoViewIfNeededAsync(new() { Timeout = 5_000 }); return; }
            catch (PlaywrightException) when (attempt < 4) { await Page.WaitForTimeoutAsync(500); }
        }
    }

    /// <summary>The signed-in account's id, so a booking can name its lead exactly.</summary>
    private async Task<string> UserIdAsync(Dictionary<string, string> headers)
    {
        var me = await _api.GetAsync("/api/me", new() { Headers = headers });
        Assert.That(me.Ok, Is.True, await me.TextAsync());
        var body = (await me.JsonAsync())!.Value;
        return body.TryGetProperty("userId", out var id) ? id.GetString()! : body.GetProperty("id").GetString()!;
    }

    private async Task<Dictionary<string, string>?> HeadersAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        if (!login.Ok) return null;
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }

    /// <summary>Takes the seeded guest out of every session on the weekend, whatever an earlier run left.</summary>
    private async Task LeaveEverythingAsync()
    {
        var headers = await HeadersAsync(ClientEmail, ClientPassword);
        if (headers is null) return;

        var programme = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}/programme", new() { Headers = headers });
        if (!programme.Ok) return;

        foreach (var session in (await programme.JsonAsync())!.Value.GetProperty("sessions").EnumerateArray())
        {
            if (session.GetProperty("mine").ValueKind == JsonValueKind.Null) continue;
            await _api.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/sessions/{session.GetProperty("id").GetString()}/sign-up",
                new() { Headers = headers });
        }
    }
}
