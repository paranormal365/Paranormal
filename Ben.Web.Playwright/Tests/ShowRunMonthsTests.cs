using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A show's run past a month, end to end (Ben, 10/24/2026): one credit covers performances within a month of
/// the first; an extra month lets the run go a month longer; calling the show off brings the month back.
/// </summary>
/// <remarks>
/// Buying goes through Stripe, which a local run doesn't have, so the month is given the SuperAdmin way
/// (the same row a purchase writes) and applied from the group's hold — the path a called-off show's months
/// take too.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class ShowRunMonthsTests : Oct16.Oct16TestBase
{
    private IAPIRequestContext _admin = null!;
    private string _orgId = "", _showId = "";
    private DateTime _first;

    private object Show(params int[] daysFromFirst)
    {
        var dates = daysFromFirst.Select(d => _first.AddDays(d).ToString("yyyy-MM-dd")).ToArray();
        return new
        {
            name = $"Playwright run {_tag}",
            placeId = _placeId, timeZoneId = "America/Chicago",
            startsOn = dates[0], endsOn = dates[^1], datesAreSeparate = true, dates,
            contactLine = "Call the box office.",
        };
    }

    private string _placeId = "";
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    [SetUp]
    public async Task APublishedShow()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        _placeId = (await OkJsonAsync(await _admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}"), "the seeded weekend"))
            .GetProperty("placeId").GetString()!;
        _first = DateTime.UtcNow.Date.AddDays(14);

        var made = await OkJsonAsync(await _admin.PostAsync($"/api/organizations/{_orgId}/events", new() { DataObject = Show(0, 7) }), "making the show");
        _showId = made.GetProperty("id").GetString()!;
        await OkJsonAsync(await _admin.PostAsync($"/api/admin/billing/organizations/{_orgId}/event-credits",
            new() { DataObject = new { quantity = 1, reason = "Playwright show run" } }), "a credit to publish with");
        await OkJsonAsync(await _admin.PostAsync($"/api/organizations/{_orgId}/events/{_showId}/publish", new() { DataObject = new { } }), "publishing the show");
    }

    [TearDown]
    public async Task PutItAway()
    {
        if (_showId.Length > 0)
            await _admin.PostAsync($"/api/organizations/{_orgId}/events/{_showId}/cancel", new() { DataObject = new { reason = "Test over" } });
        await _admin.DisposeAsync();
    }

    [Test]
    public async Task A_run_past_a_month_takes_an_extra_month_and_gives_it_back_when_called_off()
    {
        // Six weeks out is past the month, and the refusal offers the way out.
        var refused = await _admin.PutAsync($"/api/organizations/{_orgId}/events/{_showId}", new() { DataObject = Show(0, 7, 42) });
        Assert.That(refused.Status, Is.EqualTo(400));
        Assert.That(await refused.TextAsync(), Does.Contain("Add a month to this show"));

        // A month held by the group, applied to the show: the later date saves.
        await OkJsonAsync(await _admin.PostAsync($"/api/admin/billing/organizations/{_orgId}/run-months",
            new() { DataObject = new { quantity = 1, reason = "Playwright show run" } }), "giving a month");
        var applied = await OkJsonAsync(await _admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{_showId}/run-months/apply?count=1&fromGroup=true", new() { DataObject = new { } }), "applying it");
        Assert.That(applied.GetProperty("applied").GetInt32(), Is.EqualTo(1));
        await OkJsonAsync(await _admin.PutAsync($"/api/organizations/{_orgId}/events/{_showId}", new() { DataObject = Show(0, 7, 42) }), "the later date");

        // The organizer's page says how far the run can go now.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{_showId}");
        await Expect(Page.Locator("#event-run-until")).ToContainTextAsync("with its 1 extra month", new() { Timeout = 30_000 });
        var problems = new List<string>();
        problems.AddRange(await LookAtAsync(Page.Locator("#event-run-months"), "show-run-months", "light"));
        NothingWrong(problems);

        // Called off: the month comes back to the group, held for another show.
        await OkJsonAsync(await _admin.PostAsync($"/api/organizations/{_orgId}/events/{_showId}/cancel",
            new() { DataObject = new { reason = "Test over", refundHow = "Reply to your confirmation email." } }), "calling it off");
        var after = await OkJsonAsync(await _admin.GetAsync($"/api/organizations/{_orgId}/events/{_showId}/run-months"), "reading it back");
        Assert.That(after.GetProperty("applied").GetInt32(), Is.EqualTo(0));
        Assert.That(after.GetProperty("heldByGroup").GetInt32(), Is.GreaterThanOrEqualTo(1));
        _showId = "";
    }
}
