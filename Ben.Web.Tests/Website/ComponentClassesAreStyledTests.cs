using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A class name a component invents, and no stylesheet has ever heard of.
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> <c>HelpLink</c> describes itself, in its own summary, as "a small
/// question-mark badge". It rendered <c>&lt;a class="help-link-badge"&gt;?&lt;/a&gt;</c> and
/// <c>help-link-badge</c> was defined in no stylesheet in this repository — one grep hit, the
/// class attribute itself. So ninety-six screens drew a bare underlined "?" welded onto the end of
/// their heading, and the first-run walk read the page headings back as "Pricing?", "Events?",
/// "Start a group?" and a case called "#2026-001 — Hollow Creek Road, Franklin TN?"
/// (2026-09-20).</para>
///
/// <para>Nothing could have caught it. It compiles, it renders, it returns 200, every unit test
/// passes, and no test in this repository looks at how a page appears. A missing rule is invisible
/// to everything except a person opening the page — which is the definition of the debt this
/// codebase keeps paying.</para>
///
/// <para><b>A ratchet, not a sweep.</b> Thirty-two of these were already here when the rule was
/// written and each needs a judgement that belongs with whoever owns that area: some are genuinely
/// decorative and want a rule, and some are hooks for JavaScript or for a parent's
/// <c>::deep</c> selector and correctly have none. What this does is stop the number growing. A
/// new unstyled class fails immediately; an existing one is worked off the list and deleted from
/// it.</para>
/// </remarks>
public sealed class ComponentClassesAreStyledTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static readonly string[] Roots = ["Ben.Web.Website.Library", "Ben.Web.Website"];

    /// <summary>
    /// Class names this codebase owns, by prefix.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. Bootstrap's and Telerik's own classes are styled by their stylesheets
    /// and are none of this test's business; what it watches is the names our components made up,
    /// because those are the ones nothing else will ever define for us.
    /// </remarks>
    private static readonly Regex Owned =
        new(@"\b((?:ben|help|ev|case|pricing|org|feed|band|invite|join)-[a-z0-9-]+)\b",
            RegexOptions.Compiled);

    /// <summary>
    /// Unstyled class names that predate this rule, with the file each first appears in.
    /// </summary>
    /// <remarks>
    /// This list may only get shorter. Before removing an entry, decide which it is: a decorative
    /// class that wants a rule, or a hook that wants a comment in its component saying so.
    /// </remarks>
    private static readonly HashSet<string> Legacy = new(StringComparer.Ordinal)
    {
        "band-row",
        "ben-content-picker-filters", "ben-content-picker-grid", "ben-content-picker-list",
        "ben-date-", "ben-date-field",
        "ben-interactive-placeholder",
        "ben-slideshow--single",
        "ben-tour-backdrop", "ben-tour-card", "ben-tour-highlight",
        "ben-wizard", "ben-wizard-progress", "ben-wizard-refusal",
        "case-edit", "case-timeline",
        "feed-attribution", "feed-badge", "feed-category", "feed-house", "feed-join",
        "feed-like", "feed-mention", "feed-post", "feed-promoted", "feed-tag", "feed-teaser-row",
        "help-contents", "help-page", "help-section",
        "org-ad",
        "pricing-bands",
    };

    private static string AllStyleRules()
    {
        var root = RepoRoot().FullName;
        var text = new System.Text.StringBuilder();

        foreach (var dir in Roots)
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.css", SearchOption.AllDirectories))
            {
                // bin/obj carry generated copies of the same files; reading them would let a
                // stale build satisfy the rule after the source rule was deleted.
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                 || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }
                text.AppendLine(File.ReadAllText(file));
            }
        }
        return text.ToString();
    }

    private static Dictionary<string, string> OwnedClassesInMarkup()
    {
        var root = RepoRoot().FullName;
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dir in Roots)
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories))
            {
                foreach (Match attribute in Regex.Matches(File.ReadAllText(file), "class=\"([^\"]*)\""))
                foreach (Match name in Owned.Matches(attribute.Groups[1].Value))
                {
                    found.TryAdd(name.Groups[1].Value,
                        Path.GetRelativePath(root, file));
                }
            }
        }
        return found;
    }

    [Fact]
    public void A_class_a_component_invents_has_a_rule_somewhere()
    {
        var styles = AllStyleRules();

        var unstyled = OwnedClassesInMarkup()
            .Where(pair => !styles.Contains($".{pair.Key}", StringComparison.Ordinal))
            .Where(pair => !Legacy.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .ToList();

        Assert.True(unstyled.Count == 0,
            "These class names are written into markup and defined in no stylesheet, so they draw "
          + "nothing — which is how a question-mark badge spent months rendering as a stray '?' on "
          + "ninety-six screens:\n  "
          + string.Join("\n  ", unstyled.Select(p => $"{p.Key}  ({p.Value})"))
          + "\nGive each one a rule, or — if it is a hook for JavaScript or a parent's ::deep "
          + "selector — add it to Legacy with a comment in its component saying so.");
    }

    /// <summary>
    /// The list may only get shorter.
    /// </summary>
    /// <remarks>
    /// Without this, a name deleted from the markup lingers in <see cref="Legacy"/> forever and the
    /// list stops describing anything — the failure mode of every allowlist that is never read.
    /// </remarks>
    [Fact]
    public void The_legacy_list_does_not_name_classes_that_are_gone()
    {
        var used = OwnedClassesInMarkup().Keys.ToHashSet(StringComparer.Ordinal);
        var stale = Legacy.Where(name => !used.Contains(name)).OrderBy(n => n).ToList();

        Assert.True(stale.Count == 0,
            "These are listed as legacy and no longer appear in any markup — delete them from the "
          + "list:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The badge that started this. Named on its own because it is the one that did the damage.
    /// </summary>
    [Fact]
    public void The_help_badge_is_styled()
    {
        Assert.Contains(".help-link-badge", AllStyleRules(), StringComparison.Ordinal);
    }

    // ── Storefront S2.4: Smarty's own utilities never reach markup ─────────────
    //
    // The store follows Smarty 5's shop pages for STRUCTURE only. Its utility classes are defined
    // in Smarty's CSS, which is not loaded — a copied `link-normal` or `bg-cover` would draw
    // nothing, silently, the way an unstyled ben-* class does. The equivalents are ben-* classes in
    // ben-store.css (ben-link-normal, ben-card__rail and so on).

    internal static readonly string[] SmartyOnly =
    [
        "text-danger-hover", "link-normal", "link-muted", "article-format", "bg-cover", "text-dashed",
        "show-hover-item", "show-hover-container", "shadow-3d-hover", "transition-hover-top", "bg-suprime",
        "overlay-dark", "overlay-opacity-", "d-middle", "gap-auto-",
    ];

    /// <summary>Smarty utility classes named in one file's class attributes.</summary>
    internal static IReadOnlyList<string> SmartyClassesIn(string markup)
        => Regex.Matches(markup, "class=\"([^\"]*)\"")
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(token => SmartyOnly.Any(bad => bad.EndsWith('-') ? token.StartsWith(bad, StringComparison.Ordinal) : token == bad))
            .Distinct().ToList();

    [Fact]
    public void No_markup_carries_a_smarty_utility_class()
    {
        var root = RepoRoot().FullName;
        var found = Roots.Select(r => Path.Combine(root, r)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => SmartyClassesIn(File.ReadAllText(f)).Select(c => $"{c}  ({Path.GetRelativePath(root, f)})"))
            .ToList();

        Assert.True(found.Count == 0,
            "These classes come from Smarty's CSS, which the site does not load, so they draw nothing:\n  "
          + string.Join("\n  ", found) + "\nUse the ben-* equivalent in ben-store.css, or write one.");
    }

    [Theory]
    [InlineData("<a class=\"small d-block link-normal\">", "link-normal")]
    [InlineData("<div class=\"ratio bg-cover lazy\">", "bg-cover")]
    [InlineData("<div class=\"d-flex gap-auto-2\">", "gap-auto-2")]
    public void The_smarty_check_catches_a_copied_class(string markup, string expected)
        => Assert.Equal([expected], SmartyClassesIn(markup));

    [Fact]
    public void The_smarty_check_leaves_our_own_names_alone()
        => Assert.Empty(SmartyClassesIn("<a class=\"ben-link-normal ben-card__rail text-danger\">"));
}
