using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The public event page's "How long" tile says a long event the way a person would — nights or
/// days, never "352 hr" (fix/public-event-page, 10/16/2026).
/// </summary>
/// <remarks>
/// Two public dates are made on Paranormal365's calendar on Central time and deleted afterwards:
/// 6:00 PM to 6:00 AM fifteen days later (ends earlier in the day than it starts, so it runs over
/// nights: 15) and 9:00 AM to 5:00 PM four days later (later in the day, so it runs over days: 5).
/// The seeded séance weekend covers the hosted path, which counts its own dates.
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("PublicEvents")]
[NonParallelizable]
public class Oct16EventLengthTests : Oct16TestBase
{
    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private readonly List<string> _dates = [];

    private static readonly Regex InHours = new(@"\d+\s*hr", RegexOptions.IgnoreCase);

    [SetUp]
    public async Task SignIn()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
    }

    [TearDown]
    public async Task TakeTheDatesAway()
    {
        foreach (var id in _dates) await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{id}");
        _dates.Clear();
    }

    private ILocator HowLong => Page.Locator(".page-hero__fact", new() { HasTextString = "How long" }).Locator("dd");

    private async Task<string> HowLongItReadsAsync(DateTime startLocal, DateTime endLocal)
    {
        var (id, slug) = await PublicCalendarEventAsync(_sarah, _orgId, $"Oct16 length {Guid.NewGuid():N}"[..21],
            UtcFrom(startLocal, "America/Chicago"), UtcFrom(endLocal, "America/Chicago"));
        _dates.Add(id);

        await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{slug}");
        await Expect(HowLong).ToBeVisibleAsync(new() { Timeout = 30_000 });
        return (await HowLong.InnerTextAsync()).Trim();
    }

    [Test]
    public async Task Fifteen_evenings_into_mornings_read_as_15_nights()
    {
        var first = DateTime.UtcNow.Date.AddDays(20);
        var shown = await HowLongItReadsAsync(first.AddHours(18), first.AddDays(15).AddHours(6));

        Assert.That(shown, Is.EqualTo("15 nights"), $"The tile read \"{shown}\".");
        Assert.That(InHours.IsMatch(shown), Is.False, $"A fifteen-night event is still counted in hours: \"{shown}\".");
    }

    [Test]
    public async Task Five_daytimes_read_as_5_days()
    {
        var first = DateTime.UtcNow.Date.AddDays(20);
        var shown = await HowLongItReadsAsync(first.AddHours(9), first.AddDays(4).AddHours(17));

        Assert.That(shown, Is.EqualTo("5 days"), $"The tile read \"{shown}\".");
    }

    [Test]
    public async Task An_evening_still_reads_in_hours()
    {
        // The short form is still the useful one: the change must not turn an evening into "1 night".
        var first = DateTime.UtcNow.Date.AddDays(20);
        var shown = await HowLongItReadsAsync(first.AddHours(19), first.AddHours(23));

        Assert.That(shown, Is.EqualTo("4 hr"), $"The tile read \"{shown}\".");
    }

    [Test]
    public async Task A_hosted_run_counts_its_own_nights()
    {
        var slug = await PublishSeededEventAsync(_orgId, RoomsEventId);
        await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{slug}");
        await Expect(HowLong).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var shown = (await HowLong.InnerTextAsync()).Trim();

        // "2 nights" or "2 nights, 7:00 PM–2:00 AM" — counted by the dates, with the hours when every date keeps them.
        Assert.That(shown, Does.Match(@"^\d+ nights?(, .+)?$"), $"The weekend's tile read \"{shown}\".");
        Assert.That(InHours.IsMatch(shown), Is.False, $"The weekend is still counted in hours: \"{shown}\".");
    }
}
