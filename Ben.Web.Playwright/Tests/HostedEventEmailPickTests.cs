using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Choosing seats without an account, and the emailed link that holds them (item 235 slice 11d).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: guests may hold seats without signing up, as long as the organizer can
/// reach them and nobody can block an event with holds nobody stands behind.</para>
///
/// <para><b>The link is read from the outbox.</b> The harness has no mail server; the pick letter is
/// queued all the same, and the tests read its link there as the SuperAdmin
/// (<see cref="BenTestBase.LinkFromTheOutboxAsync"/>). It used to be read from the API's log, which
/// meant the API wrote a link that holds somebody's places into a text file.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventEmailPickTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _publicUrl = string.Empty;
    private readonly List<string> _tokens = [];

    [SetUp]
    public async Task OpenTheHouse()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/publish",
            new() { DataObject = new { } });

        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}");
        var slug = (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString();
        _publicUrl = $"{BaseUrl}/o/paranormal365/events/{slug}";
        await admin.DisposeAsync();
    }

    [TearDown]
    public async Task LetEverythingGo()
    {
        // A pick nobody confirmed keeps its seat for fifteen minutes; letting it go here keeps the
        // next test's house as it found it.
        foreach (var token in _tokens)
            await _api.DeleteAsync($"/api/public/hosted-events/email-picks/{token}");

        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_stranger_picks_a_seat_confirms_by_the_link_and_holds_it()
    {
        var email = $"pick-{Guid.NewGuid():N}@example.test";

        await OpenThePickerAsync();
        await PickOneAsync();
        await FillBookingContactAsync("picker", email);

        await ClickUntilAsync(Page.Locator("#picker-hold"), Page.Locator("#picker-emailed"));
        await Expect(Page.Locator("#picker-emailed")).ToContainTextAsync("Check your email");
        await Expect(Page.Locator("#picker-emailed")).ToContainTextAsync(email);

        var token = await TokenForAsync(email);

        // Everybody else now sees that square as being decided.
        await OpenThePickerAsync();
        await Expect(Page.Locator(".plan__unit[data-state='pending']").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Page.GotoAsync($"{BaseUrl}/event-picks/{token}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#pick-places")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ClickUntilAsync(Page.Locator("#pick-confirm"), Page.Locator("#pick-held"));
        await Expect(Page.Locator("#pick-set-password")).ToBeVisibleAsync();

        // The link goes on working: it is how somebody with no password lets the places go.
        await ClickUntilAsync(Page.Locator("#pick-let-go"), Page.Locator("#pick-over"));
    }

    [Test]
    public async Task A_link_that_is_let_go_before_the_click_holds_nothing()
    {
        var email = $"pick-{Guid.NewGuid():N}@example.test";

        await OpenThePickerAsync();
        await PickOneAsync();
        await FillBookingContactAsync("picker", email);
        await ClickUntilAsync(Page.Locator("#picker-hold"), Page.Locator("#picker-emailed"));

        var token = await TokenForAsync(email);

        await Page.GotoAsync($"{BaseUrl}/event-picks/{token}");
        await WaitUntilLoadedAsync();
        await ClickUntilAsync(Page.Locator("#pick-let-go"), Page.Locator("#pick-let-go-done"));

        Assert.That(await Page.Locator("#pick-confirm").CountAsync(), Is.Zero,
            "a pick that was let go still offered to hold the places");
    }

    [Test]
    public async Task On_a_phone_the_questions_and_the_button_are_both_reachable()
    {
        await OpenThePickerAsync(375, 812);
        await PickOneAsync();

        var phone = Page.Locator("#picker-phone");
        await phone.ScrollIntoViewIfNeededAsync();
        await Expect(phone).ToBeInViewportAsync();

        await FillBookingContactAsync("picker", $"pick-{Guid.NewGuid():N}@example.test");
        await Expect(Page.Locator("#picker-hold")).ToBeInViewportAsync();
        await Expect(Page.Locator("#picker-hold")).ToBeEnabledAsync();

        // No horizontal page scroll with the form open.
        var wide = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.That(wide, Is.False, "the contact form made the page scroll sideways on a phone");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenThePickerAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(_publicUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#hosted-places")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task PickOneAsync()
    {
        var free = Page.Locator(".plan__unit[data-state='free']");
        await Expect(free.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The last free seat, so a run that left picks behind does not collide with this one.
        var seat = free.Last;
        await seat.ScrollIntoViewIfNeededAsync();
        await ClickUntilAsync(seat, Page.Locator("#picker-contact"));
    }

    /// <summary>The token in the link the pick letter carried, from the outbox.</summary>
    private async Task<string> TokenForAsync(string email)
    {
        const string path = "/event-picks/";
        var token = (await LinkFromTheOutboxAsync(email, path))[path.Length..];
        _tokens.Add(token);
        return token;
    }

    private async Task<IAPIRequestContext> SignedInAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in: {await login.TextAsync()}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }
}
