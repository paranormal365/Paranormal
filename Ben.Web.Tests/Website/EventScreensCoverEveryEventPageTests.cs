using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Ben.Web.Website.Library.Manage.Events;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The SuperAdmin's list of events reaches every screen an event has.
/// </summary>
/// <remarks>
/// <see cref="EventScreens"/> is what the rows of <c>/admin/events</c> open onto. A screen added under
/// <c>Manage/Events</c> and not named there would be one the SuperAdmin can only find by knowing its address; this
/// fails first, naming the page.
/// </remarks>
public sealed class EventScreensCoverEveryEventPageTests
{
    private const string EventRoute = "/organizations/{OrgId:guid}/events/{EventId:guid}";

    [Fact]
    public void Every_event_page_is_one_of_the_screens_the_list_offers()
    {
        var pages = RepoFiles.Paths("*.razor")
            .Where(p => p.Replace('\\', '/').Contains("/Ben.Web.Website.Library/Manage/Events/"))
            .SelectMany(p => Regex.Matches(File.ReadAllText(p), @"^@page\s+""([^""]+)""", RegexOptions.Multiline)
                .Select(m => (File: Path.GetFileName(p), Route: m.Groups[1].Value)))
            .Where(x => x.Route.StartsWith(EventRoute, StringComparison.Ordinal))
            .ToList();

        Assert.True(pages.Count > 10, $"only {pages.Count} event pages were found — a guard that reads nothing proves nothing");

        var offered = EventScreens.Organizer.Select(s => s.Suffix).ToHashSet(StringComparer.Ordinal);
        var missing = pages.Where(x => !offered.Contains(x.Route[EventRoute.Length..])).ToList();

        Assert.True(missing.Count == 0,
            "event pages the SuperAdmin's list does not link to — add them to EventScreens.Organizer:\n  "
          + string.Join("\n  ", missing.Select(x => $"{x.File}: {x.Route}")));
    }

    [Fact]
    public void Every_screen_the_list_offers_is_a_real_page()
    {
        var routes = RepoFiles.Paths("*.razor")
            .Where(p => p.Replace('\\', '/').Contains("/Ben.Web.Website.Library/Manage/Events/"))
            .SelectMany(p => Regex.Matches(File.ReadAllText(p), @"^@page\s+""([^""]+)""", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var dead = EventScreens.Organizer.Where(s => !routes.Contains(EventRoute + s.Suffix)).Select(s => s.Label).ToList();
        Assert.True(dead.Count == 0, "screens the list offers that no page serves: " + string.Join(", ", dead));
    }
}
