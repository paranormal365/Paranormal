using Ben.Web.Website.Library.Manage.Events;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every organizer screen of an event is on the event's own menu (10/23/2026: Guest messages existed and
/// could only be reached from the bell or the group's messages).
/// </summary>
public sealed class EventMenuReachesEveryScreenTests
{
    /// <summary>Screens opened from inside another screen rather than from the menu.</summary>
    private static readonly HashSet<string> OpenedFromAnotherScreen =
    [
        "/door-tickets/poster",   // from the door
        "/door/classes",          // from the door
        "/keep",                  // from After the event
    ];

    [Fact]
    public void The_event_menu_names_every_organizer_screen()
    {
        var rail = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Website.Library", "Shared", "ContextRail.razor"));
        var missing = EventScreens.Organizer
            .Where(s => s.Suffix.Length > 0 && !OpenedFromAnotherScreen.Contains(s.Suffix))
            .Where(s => !rail.Contains($"\"{s.Suffix}\"", StringComparison.Ordinal))
            .Select(s => $"{s.Label} ({s.Suffix})")
            .ToList();
        Assert.True(missing.Count == 0, "Not on the event's menu: " + string.Join(", ", missing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Ben.slnx not found above the test output.");
    }
}
