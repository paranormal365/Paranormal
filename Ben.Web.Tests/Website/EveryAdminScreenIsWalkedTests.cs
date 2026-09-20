using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every administration screen is walked by a person, not only reached by a crawler (item 246).
/// </summary>
/// <remarks>
/// <para><b>Why both, when the route crawl already visits everything.</b> The crawl discovers
/// routes from <c>@page</c> and opens each one, which proves a screen comes up. It cannot prove a
/// screen WORKS: <c>/admin/email-templates</c> draws its list of letters before anything is
/// chosen, so a completely broken editor returned a perfectly healthy page. The walk is where
/// somebody opens a thing and uses it.</para>
///
/// <para><b>And the walk is a hand-written list.</b> That is the gap this closes: a new admin
/// screen joins the crawl by existing and joins the walk only if somebody remembers. One was added
/// on 2026-09-20 and nothing noticed it was unwalked — the suite stayed green.</para>
///
/// <para>Being on <see cref="NotWalked"/> is a decision with a reason, not a way to make this
/// quiet.</para>
/// </remarks>
public sealed class EveryAdminScreenIsWalkedTests
{
    /// <summary>Screens the walk deliberately does not open, and why.</summary>
    private static readonly Dictionary<string, string> NotWalked = new()
    {
        ["/admin/users/create"] =
            "A form for making an account. Walking it means either abandoning a half-filled form "
          + "or creating a real person on the testing copy on every run, and the account it makes "
          + "would outlive the walk.",

        ["/admin/session-files"] =
            "A file browser over whatever a session left behind. What it shows depends entirely on "
          + "what has been uploaded, so a walk of it proves nothing repeatable.",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> DeclaredAdminRoutes()
    {
        foreach (var project in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" })
        {
            var root = Path.Combine(RepoRoot(), project);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^@page\s+""([^""]+)""",
                                                  RegexOptions.Multiline))
                {
                    var route = m.Groups[1].Value;

                    // Routes with a parameter are opened by following a link from a list, which is
                    // how the walk reaches them; there is no fixed URL to name.
                    if (route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
                        && !route.Contains('{'))
                        yield return route;
                }
            }
        }
    }

    [Fact]
    public void Every_plain_admin_route_is_walked_or_says_why_not()
    {
        var walk = File.ReadAllText(Path.Combine(
            RepoRoot(), "Ben.Web.Playwright", "Capture", "ProductWalk.cs"));

        var unwalked = DeclaredAdminRoutes()
            .Where(r => !walk.Contains($"\"{r}\"", StringComparison.Ordinal))
            .Where(r => !NotWalked.ContainsKey(r))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(unwalked.Count == 0,
            "These administration screens are reached by the route crawl but never opened and used "
          + "by anybody:\n  " + string.Join("\n  ", unwalked)
          + "\n\nAdd them to ProductWalk's SuperAdmin walk, or to "
          + $"{nameof(EveryAdminScreenIsWalkedTests)}.{nameof(NotWalked)} with the reason.");
    }

    /// <summary>
    /// An excuse for a screen that no longer exists is an excuse nobody will re-examine.
    /// </summary>
    [Fact]
    public void Every_excuse_is_still_about_a_real_screen()
    {
        var declared = DeclaredAdminRoutes().ToHashSet(StringComparer.Ordinal);

        var stale = NotWalked.Keys.Where(r => !declared.Contains(r)).ToList();

        Assert.True(stale.Count == 0,
            "These are excused from the walk but no longer exist:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void Every_excuse_gives_a_reason()
        => Assert.All(NotWalked, pair =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value));
            Assert.True(pair.Value.Length > 40,
                $"{pair.Key}'s reason is too short to be one.");
        });

    /// <summary>
    /// Every administration screen has something on the site that leads to it.
    /// </summary>
    /// <remarks>
    /// <para>A page reachable only by typing its URL is a feature nobody will find. This codebase
    /// keeps meeting the same fault from the other direction — an endpoint written with no screen —
    /// and <c>ModerationQueuesHaveAnEntranceTests</c> guards that half. This is the near side: a
    /// screen with no way in.</para>
    ///
    /// <para>It is not hypothetical. <c>/admin/email-templates</c> was built, tested, walked,
    /// screenshotted and merged to master with nothing anywhere linking to it (2026-09-20). Every
    /// check passed, because every check knew the URL.</para>
    ///
    /// <para>A link from anywhere counts — the navigation, a button on a related screen, a card on
    /// a dashboard. What does not count is the page naming its own route.</para>
    /// </remarks>
    [Fact]
    public void Every_admin_screen_has_a_way_in()
    {
        var root = RepoRoot();
        var files = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                      || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => f, File.ReadAllText);

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, text) in files)
            foreach (Match m in Regex.Matches(text, @"^@page\s+""([^""]+)""", RegexOptions.Multiline))
            {
                var route = m.Groups[1].Value;
                if (route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) && !route.Contains('{'))
                    owners[route] = file;
            }

        var orphans = owners
            .Where(pair => !files.Any(f => f.Key != pair.Value
                                        && f.Value.Contains(pair.Key, StringComparison.Ordinal)))
            .Select(pair => pair.Key)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "These administration screens exist but nothing on the site leads to them, so they "
          + "can only be reached by typing the URL:" + Environment.NewLine + "  "
          + string.Join(Environment.NewLine + "  ", orphans)
          + Environment.NewLine + Environment.NewLine
          + "Add a navigation entry, or a link from the screen somebody would look on.");
    }

    /// <summary>Screens outside Administration with no link to them, and why each is allowed.</summary>
    private static readonly Dictionary<string, string> NoLinkNeeded = new()
    {
        ["/logout"] =
            "Reached by BenUserMenu's Sign Out, which calls the sign-out code rather than "
          + "navigating to a URL. There is nothing to link to.",

        ["/signout"] =
            "The same page under its other name, for anybody who types the word they expect.",

        ["/styleguide"] =
            "A reference page for whoever is building the site, deliberately not offered to "
          + "people using it.",

        ["/organization-security"] =
            "A LEFTOVER. Its own subtitle calls it a \"Starter management UI\" — scaffolding from "
          + "the security integration that the real screens replaced. Nothing links to it and "
          + "nothing references it. The endpoints behind it ARE gated "
          + "(SetAccessGrantAsync calls EnsureCanManageOrganizationAsync, and the interface says "
          + "the actor must be a SuperAdmin or an Owner), so this is untidiness rather than a "
          + "hole — but it is a page that registers groups, searches every user and sets grants, "
          + "and it should probably be deleted rather than excused. Recorded 2026-09-20.",
    };

    private static IEnumerable<string> DeclaredPublicRoutes(Dictionary<string, string> files)
    {
        foreach (var (file, text) in files)
        {
            if (!file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (Match m in Regex.Matches(text, @"^@page\s+""([^""]+)""", RegexOptions.Multiline))
            {
                var route = m.Groups[1].Value;

                if (route is "/" || route.Contains('{')) continue;
                if (route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)) continue;

                yield return route;
            }
        }
    }

    /// <summary>
    /// Every screen a member or a group uses has something that leads to it.
    /// </summary>
    /// <remarks>
    /// <para>The same rule as the administration one above, applied to the rest of the site. The
    /// API and the services are scanned too, not just the two website projects: a confirmation
    /// link is built where the LETTER is written, so scanning only the site reported
    /// <c>/confirm-email</c> and <c>/reset-password</c> as orphans when they are reached by
    /// thousands of people.</para>
    ///
    /// <para>Routes carrying a parameter are excluded. They are reached by following a link built
    /// from a real id, so there is no literal string to find, and a rule that cannot see them
    /// would either be silent or wrong about all of them.</para>
    /// </remarks>
    [Fact]
    public void Every_member_and_group_screen_has_a_way_in()
    {
        var root = RepoRoot();
        var files = new[] { "Ben.Web.Website.Library", "Ben.Web.Website", "Ben.Data.WebApi", "Ben.Web.Services" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                      || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => f, File.ReadAllText);

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, text) in files)
            foreach (Match m in Regex.Matches(text, @"^@page\s+""([^""]+)""", RegexOptions.Multiline))
            {
                var route = m.Groups[1].Value;
                if (route is "/" || route.Contains('{')) continue;
                if (route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)) continue;
                owners[route] = file;
            }

        var orphans = owners
            .Where(pair => !NoLinkNeeded.ContainsKey(pair.Key))
            .Where(pair => !files.Any(f => f.Key != pair.Value
                                        && f.Value.Contains(pair.Key, StringComparison.Ordinal)))
            .Select(pair => pair.Key)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "These screens exist but nothing leads to them, so they can only be reached by typing "
          + "the URL:" + Environment.NewLine + "  "
          + string.Join(Environment.NewLine + "  ", orphans)
          + Environment.NewLine + Environment.NewLine
          + "Add a link from wherever somebody would look, or say why it needs none in "
          + $"{nameof(NoLinkNeeded)}.");
    }

    [Fact]
    public void Every_screen_excused_from_needing_a_link_still_exists()
    {
        var root = RepoRoot();
        var declared = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^@page\s+""([^""]+)""",
                                           RegexOptions.Multiline)
                                  .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var stale = NoLinkNeeded.Keys.Where(r => !declared.Contains(r)).ToList();

        Assert.True(stale.Count == 0,
            "These are excused from needing a link but no longer exist:" + Environment.NewLine
          + "  " + string.Join(Environment.NewLine + "  ", stale));
    }
}
