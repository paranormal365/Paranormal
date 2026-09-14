using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A SuperAdmin can open every screen of an event run by a group they do not belong to.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Every hosted endpoint lets a SuperAdmin through, and every walk and test until
/// 2026-09-14 used the SuperAdmin account, which owns Paranormal365, so a page that quietly depended on membership would
/// never have been seen to refuse. Ben asked for oversight of every hosted event screen; this is the proof.</para>
///
/// <para><b>The screens are found, not listed.</b> Every <c>@page</c> under <c>Manage/Events</c> that belongs to one
/// event is read from the source, so a screen added next month is opened by this test without anybody remembering to
/// add it. Each must name the event and show none of the ways those pages refuse or fail: an error or "missing" alert,
/// a list that could not load, or the read-only note a member without permission is given.</para>
///
/// <para><b>The group.</b> Music City Spirit Seekers is Emma's; the SuperAdmin is not a member, which the test checks
/// first rather than assumes. The event is a draft of its own, made through the API, reused on the next run and put
/// away afterwards.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class SuperAdminEventScreensTests : BenTestBase
{
    private const string MusicCitySpiritSeekers = "50000001-0000-0000-0000-000000000001";
    private const string BellWitchCave = "40000001-0000-0000-0000-000000000001";
    private const string EventName = "Oversight screens check";

    private IAPIRequestContext _admin = null!;
    private string _eventId = string.Empty;

    /// <summary>Every event screen's address, with the group and event still as parameters, read from the pages.</summary>
    private static IEnumerable<string> EventScreenRoutes()
    {
        var folder = Path.Combine(RouteCrawlHelper.RepoRoot(), "Ben.Web.Website.Library", "Manage", "Events");
        var routes = Directory.EnumerateFiles(folder, "*.razor")
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"^@page\s+""([^""]+)""", RegexOptions.Multiline))
            .Select(m => m.Groups[1].Value)
            .Where(r => r.StartsWith("/organizations/{OrgId:guid}/events/{EventId:guid}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.That(routes, Has.Count.GreaterThan(10), "only a handful of event screens were found — the folder moved?");
        return routes;
    }

    [SetUp]
    public async Task SignInToTheApi()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, Is.True, await login.TextAsync());
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        await api.DisposeAsync();

        _admin = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    [TearDown]
    public async Task PutItAway()
    {
        if (_eventId.Length > 0)
            await _admin.PostAsync($"/api/organizations/{MusicCitySpiritSeekers}/events/{_eventId}/archive", new() { DataObject = new { } });
        await _admin.DisposeAsync();
    }

    [Test]
    public async Task The_SuperAdmin_is_not_a_member_of_the_group_the_screens_belong_to()
    {
        var mine = await _admin.GetAsync("/api/security/organizations/my-memberships");
        Assert.That(mine.Ok, Is.True, await mine.TextAsync());
        Assert.That(await mine.TextAsync(), Does.Not.Contain(MusicCitySpiritSeekers),
            "the SuperAdmin belongs to this group, so opening its screens would prove nothing about oversight");
    }

    [Test]
    public async Task Every_event_screen_opens_for_the_SuperAdmin()
    {
        _eventId = await DraftHostedEventAsync(_admin, MusicCitySpiritSeekers, EventName, BellWitchCave);
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var refused = new List<string>();
        foreach (var route in EventScreenRoutes())
        {
            var address = route.Replace("{OrgId:guid}", MusicCitySpiritSeekers).Replace("{EventId:guid}", _eventId);
            if (await ProblemWithAsync(address) is { } problem)
                refused.Add($"{route}: {problem}");
        }

        // And the group's list of events, which is where an organizer starts.
        if (await ProblemWithAsync($"/organizations/{MusicCitySpiritSeekers}/events") is { } listProblem)
            refused.Add($"/organizations/{{OrgId}}/events: {listProblem}");

        Assert.That(refused, Is.Empty, "screens that did not open for the SuperAdmin:\n  " + string.Join("\n  ", refused));
    }

    [Test]
    public async Task The_list_of_events_opens_onto_every_screen_of_one()
    {
        _eventId = await DraftHostedEventAsync(_admin, MusicCitySpiritSeekers, EventName, BellWitchCave);
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        await Page.GotoAsync($"{BaseUrl}/admin/events");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync(30_000);
        await Page.Locator("#events-search").FillAsync(EventName);
        var row = Page.Locator(".admin-events-grid tr", new() { HasTextString = EventName }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Telerik's own expand control: the row's hierarchy cell, which carries the plus icon and aria-expanded.
        var screens = Page.Locator($"#screens-{_eventId}");
        await ClickUntilAsync(row.Locator("td.k-hierarchy-cell"), screens);
        foreach (var label in new[] { "The event", "Bookings", "The door", "Files", "Keep the files", "Photo wall" })
            await Expect(screens.GetByRole(AriaRole.Link, new() { Name = label, Exact = true })).ToBeVisibleAsync();

        await ClickUntilUrlAsync(screens.GetByRole(AriaRole.Link, new() { Name = "Bookings", Exact = true }),
            $"/organizations/{MusicCitySpiritSeekers}/events/{_eventId}/bookings");
    }

    /// <summary>What is wrong with one screen for the SuperAdmin, or null.</summary>
    private async Task<string?> ProblemWithAsync(string address)
    {
        await Page.GotoAsync($"{BaseUrl}{address}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync(30_000);
        try { await Expect(Spinners).ToHaveCountAsync(0, new() { Timeout = 30_000 }); }
        catch (PlaywrightException) { return "still loading after 30 seconds"; }

        var shown = Main.Locator(".alert-danger:visible, [id$='-missing']:visible, #event-read-only, #events-read-only");
        if (await shown.CountAsync() > 0)
            return $"showed \"{(await shown.First.InnerTextAsync()).Trim()}\"";

        var text = await Main.InnerTextAsync();
        foreach (var refusal in new[] { "Couldn't load this", "Page not found", "do not have access", "not allowed" })
            if (text.Contains(refusal, StringComparison.OrdinalIgnoreCase))
                return $"said \"{refusal}\"";

        return address.EndsWith("/events", StringComparison.Ordinal) || text.Contains(EventName, StringComparison.Ordinal)
            ? null
            : "never named the event";
    }
}
