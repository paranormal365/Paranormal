using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The signed-in home page's countdown and What I'm going to load and say no failure, for a guest
/// who has places booked (feature/home-event-countdown, 10/16/2026).
/// </summary>
/// <remarks>
/// Both screens were handed the NAME of their refusal field as text (<c>Refusal="_refusal"</c>), so
/// each always drew its failure state: What I'm going to read "_othersRefusal Try again" to every
/// guest, and Home's card said it couldn't check. Unit tests set the parameter directly and passed;
/// nothing opened these pages signed in and looked. Daniel, the seeded client, holds bookings on
/// the seeded hosted events, which is what the help screenshot showed.
/// </remarks>
[TestFixture]
[Category("Oct16")]
[NonParallelizable]
public class Oct16GoingToTests : Oct16TestBase
{
    private IAPIRequestContext? _sarah;
    private string _orgId = "", _eventId = "";

    /// <summary>
    /// Daniel says he's coming to a public evening ten days out, made for this test. The countdown
    /// tests used to lean on his seeded booking, which earlier tests in a full run take back
    /// (10/22/2026: three failures with nothing wrong on the page).
    /// </summary>
    [SetUp]
    public async Task DanielHasSomethingComingUp()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        var day = DateTime.UtcNow.Date.AddDays(10);
        (_eventId, _) = await PublicCalendarEventAsync(_sarah, _orgId, $"Oct16 countdown {Guid.NewGuid():N}"[..24],
            UtcFrom(day.AddHours(19), "America/Chicago"), UtcFrom(day.AddHours(22), "America/Chicago"));
        var daniel = await ApiAsAsync(ClientEmail, ClientPassword);
        await OkJsonAsync(await daniel.PostAsync($"/api/public/events/{_eventId}/rsvp", new() { DataObject = new { } }),
            "Daniel saying he's coming");
    }

    [TearDown]
    public async Task TakeTheEveningAway()
    {
        if (_sarah is not null && _eventId.Length > 0)
            await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{_eventId}");
    }

    [Test]
    public async Task What_Im_going_to_lists_without_any_failure_line()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-events");
        await WaitForTheCircuitAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "What I'm going to" })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(1500);   // the second list loads after the first

        await Expect(Page.Locator("#my-events-others-refusal")).ToHaveCountAsync(0);
        var text = await Page.Locator("main").InnerTextAsync();
        Assert.That(text, Does.Not.Contain("_others"), "A field's name is printed on the page.");
        Assert.That(text, Does.Not.Contain("Try again"), "What I'm going to shows a failure for a guest whose lists loaded.");
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, @"(^|\s)_[a-z]\w+"), Is.False,
            "Something on the page reads like an unrendered field name (_something).");
    }

    [Test]
    public async Task Home_shows_the_countdown_card_and_no_failure_for_a_guest_with_places()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForTheCircuitAsync();
        await SkipAnyTourAsync();

        await Expect(Page.Locator("[data-testid='home-event-countdown']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='home-countdown-refusal']")).ToHaveCountAsync(0);
        var text = await Page.Locator("main").InnerTextAsync();
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, @"(^|\s)_[a-z]\w+"), Is.False,
            "Something on the home page reads like an unrendered field name (_something).");
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_home_countdown_card_fits_every_screen(string theme)
    {
        await UseThemeAsync(theme);
        await LoginAsync(ClientEmail, ClientPassword);
        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/");
            await WaitForTheCircuitAsync();
            await SkipAnyTourAsync();
            var card = Page.Locator("[data-testid='home-event-countdown']");
            problems.AddRange(await LookAtAsync(card, "home-countdown", theme));
            problems.AddRange(await TouchTargetAsync(card, "home-countdown", theme));
        }
        NothingWrong(problems);
    }
}
