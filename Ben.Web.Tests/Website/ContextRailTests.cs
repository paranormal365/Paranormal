using System.Text.RegularExpressions;
using Ben.Web.Website.Library.Shared;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The rail's own menus for a group and an event (Signal, Ben 2026-10-01: "when you navigate to the
/// sub-page, the menu becomes the menu for that page with a back button at the top").
/// </summary>
public class ContextRailTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ev  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("organizations", false, false)]
    [InlineData("organizations/new", false, false)]
    [InlineData("organizations/11111111-1111-1111-1111-111111111111", true, false)]
    [InlineData("organizations/11111111-1111-1111-1111-111111111111?tab=members", true, false)]
    [InlineData("organizations/11111111-1111-1111-1111-111111111111/cases/abc", true, false)]
    [InlineData("organizations/11111111-1111-1111-1111-111111111111/events", true, false)]
    [InlineData("organizations/11111111-1111-1111-1111-111111111111/events/22222222-2222-2222-2222-222222222222", true, true)]
    [InlineData("organizations/11111111-1111-1111-1111-111111111111/events/22222222-2222-2222-2222-222222222222/door#x", true, true)]
    [InlineData("o/paranormal365", false, false)]
    public void An_address_names_its_group_and_event(string relative, bool hasOrg, bool hasEvent)
    {
        var (org, ev) = ContextRail.Parse(relative);
        Assert.Equal(hasOrg ? Org : null, org);
        Assert.Equal(hasEvent ? Ev : null, ev);
    }

    /// <summary>
    /// Every screen the event page indexes is in the event's rail menu. Where the rail shows, the
    /// page hides those index entries (they would be the same list twice) — so a screen added to
    /// the page and not to the rail would be unreachable on a desktop.
    /// </summary>
    [Fact]
    public void The_event_rail_lists_every_screen_the_event_page_indexes()
    {
        var page = File.ReadAllText(RepoFile("Ben.Web.Website.Library/Manage/Events/OrgEventPage.razor"));
        var rail = File.ReadAllText(RepoFile("Ben.Web.Website.Library/Shared/ContextRail.razor"));

        var labels = Regex.Matches(page, @"@IndexEntry\(""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(labels);

        var missing = labels.Where(l => !rail.Contains($"\"{l}\"", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Screens the event page indexes but the rail does not list: " + string.Join(", ", missing));
    }

    /// <summary>The group's rail menu offers every tab the group page draws.</summary>
    [Fact]
    public void The_group_rail_lists_every_tab_the_group_page_draws()
    {
        var page = File.ReadAllText(RepoFile("Ben.Web.Website.Library/Organization/OrganizationView.razor"));
        var rail = File.ReadAllText(RepoFile("Ben.Web.Website.Library/Shared/ContextRail.razor"));

        var ids = Regex.Matches(page, @"<BenTab Title=""[^""]+"" Id=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(ids);

        var missing = ids.Where(id => !rail.Contains($"\"{id}\"", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Tabs the group page draws but the rail does not list: " + string.Join(", ", missing));
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }
}
