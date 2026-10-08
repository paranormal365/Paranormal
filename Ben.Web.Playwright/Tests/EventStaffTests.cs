using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Handing one event's door to somebody who is not in the group (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>The claim worth walking is that an invitation grants nothing until it is accepted</b>,
/// and that the screen says so. A venue that thinks it has staffed Saturday and has not is a venue
/// with nobody on the door.</para>
///
/// <para>The invitation letter cannot be followed here — the harness has no mail — so the token
/// half is covered by unit tests and what is walked is the venue's side: invite, see it standing,
/// take it back.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventStaffTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _helper = string.Empty;

    [SetUp]
    public async Task StartWithNobodyHelping()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        // A fresh address per run, so a row left behind by an earlier one is never what is being
        // looked at. The harness reuses its database.
        _helper = $"steward-{Guid.NewGuid():N}@example.test";

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheStaffAsync(admin);
        await admin.DisposeAsync();

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutItBack()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheStaffAsync(admin);
        await admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task An_invitation_stands_openly_as_granting_nothing_yet()
    {
        await OpenTheStaffPageAsync();

        await Page.Locator("#staff-email").FillAsync(_helper);
        await Page.Locator("#staff-role").FillAsync("Door");

        await ClickUntilAsync(Page.Locator("#staff-add"), Page.Locator("#staff-note"));

        await Expect(Page.Locator("#staff-note")).ToContainTextAsync("can do nothing until they accept");
        await Expect(Page.Locator("#staff-list")).ToContainTextAsync(_helper);
        await Expect(Page.Locator("#staff-list")).ToContainTextAsync("hasn't accepted yet");
        await Expect(Page.Locator("#staff-list")).ToContainTextAsync("runs the door");
    }

    [Test]
    public async Task A_helper_who_may_do_nothing_is_refused_before_anything_is_sent()
    {
        // A row that grants nothing is not a helper, it is a mistake — and the mistake is easiest
        // to make by unticking the one box that was on by default.
        await OpenTheStaffPageAsync();

        await Page.Locator("#staff-email").FillAsync(_helper);
        await Page.Locator("#staff-can-door").UncheckAsync();

        await Expect(Page.Locator("#staff-add")).ToBeDisabledAsync();
        await Expect(Page.Locator("#staff-needs")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Taking_somebody_off_says_so_and_the_row_goes()
    {
        await OpenTheStaffPageAsync();

        await Page.Locator("#staff-email").FillAsync(_helper);
        await ClickUntilAsync(Page.Locator("#staff-add"), Page.Locator("#staff-note"));
        await Expect(Page.Locator("#staff-list")).ToContainTextAsync(_helper);

        var remove = Page.Locator("#staff-list button", new() { HasTextString = "Remove" }).First;
        await ClickUntilAsync(remove, Page.GetByText("no longer helping"));

        await Expect(Page.Locator("#staff-note")).ToContainTextAsync("no longer helping");
    }

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_page_never_slides_sideways_however_narrow_it_is(int width, int height)
    {
        await OpenTheStaffPageAsync(width, height);

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the staff page slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheStaffPageAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/staff");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#staff-add")).ToBeVisibleAsync(new() { Timeout = 30_000 });
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

    /// <summary>Leaves the event with nobody helping, whatever an earlier run did.</summary>
    private async Task ClearTheStaffAsync(IAPIRequestContext admin)
    {
        var url = $"/api/organizations/{_orgId}/events/{RoomsEventId}/staff";

        var list = await admin.GetAsync(url);
        if (!list.Ok) return;

        foreach (var person in (await list.JsonAsync())!.Value.GetProperty("staff").EnumerateArray())
        {
            var id = person.GetProperty("id").GetString();
            await admin.DeleteAsync($"{url}/{id}");
        }
    }
}
