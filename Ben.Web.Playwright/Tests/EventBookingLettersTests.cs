using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Choosing how often a group writes to you about its bookings (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para>The letters themselves cannot be read here — the harness has no mail — so their timing,
/// their recipients and their silence when there is nothing to say are unit tests. What is walked is
/// the one control a person has over them: that it is on the page they would look for it on, that
/// a choice survives a reload, and that it fits a phone.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventBookingLettersTests : BenTestBase
{
    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;

    [SetUp]
    public async Task StartAsItHappens()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");
        await ResetAsync();
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutItBack()
    {
        await ResetAsync();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_choice_is_saved_and_still_chosen_after_a_reload()
    {
        await OpenAsync();

        var group = Page.Locator($".booking-letters-group[data-org='{_orgId}']");
        await Expect(group).ToBeVisibleAsync();

        await ClickUntilAsync(group.Locator("label", new() { HasTextString = "A daily letter" }),
                              group.GetByRole(AriaRole.Status));
        await Expect(group.GetByRole(AriaRole.Status)).ToContainTextAsync("never an empty one");

        await OpenAsync();
        await Expect(Page.Locator($"#letters-{Guid.Parse(_orgId):N}-1")).ToBeCheckedAsync();
    }

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_choices_fit_without_the_page_sliding_sideways(int width, int height)
    {
        await OpenAsync(width, height);

        // Scrolled to, then required WHOLLY visible: a button half off the right edge of a phone is
        // the failure, and a page that merely has it below the fold is not.
        var nothing = Page.Locator($".booking-letters-group[data-org='{_orgId}']")
            .Locator("label", new() { HasTextString = "Nothing" });
        await nothing.ScrollIntoViewIfNeededAsync();
        await Expect(nothing).ToBeInViewportAsync(new() { Ratio = 1f });

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the notifications page slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/notifications");
        await WaitUntilLoadedAsync();

        var card = Page.Locator("#booking-letters");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await card.ScrollIntoViewIfNeededAsync();
    }

    private async Task ResetAsync()
    {
        var login = await _api.PostAsync("/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, Is.True, await login.TextAsync());
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        await using var admin = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        await admin.PutAsync($"/api/me/event-booking-alerts/{_orgId}", new() { DataObject = new { mode = 0 } });
    }
}
