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
}
