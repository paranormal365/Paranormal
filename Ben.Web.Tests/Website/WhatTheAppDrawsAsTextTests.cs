using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A field the website renders as HTML must not be drawn as text by the shipped app (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>The bug this exists for, twice.</b> Case descriptions became a formatting editor, so
/// the website started drawing them with <c>MarkupString</c> — and every client on the iPhone app
/// read <c>&lt;p&gt;Things going bump in the night&lt;/p&gt;</c>, because SwiftUI's <c>Text</c>
/// renders a string exactly as given. Ben found it on a real case. Looking for others found the
/// same thing on a case's timeline entries, where the group-facing screen stripped the tags and
/// the client-facing one did not.</para>
///
/// <para><b>Why a name match, and why that is enough.</b> The two sides share no types — one is C#,
/// one is Swift — so the only thing they have in common is what a field is called. A name that is
/// rendered as HTML on one side and decoded as a plain string on the other is not proof of a bug,
/// but it is always worth a look, and every one of them here has been looked at. Two were real.
/// Two were different fields that happen to share a word.</para>
///
/// <para><b>Computed Swift properties are not read.</b> <c>public var summary: String {</c> is
/// worked out in the app and never arrives from us, so counting it produced a fourth "overlap"
/// that was nothing at all — and the entry written to excuse it was then refused by the
/// stale-entry check below, which is what that check is for.</para>
///
/// <para><b>The fix is always on the server.</b> The app cannot change until its next build, which
/// is why <c>CaseMessageBodies</c> exists and why this list keeps growing rather than the app being
/// patched: the field the app reads stays plain, and the formatted copy goes beside it under a name
/// the shipped app has never heard of.</para>
/// </remarks>
public sealed class WhatTheAppDrawsAsTextTests
{
    /// <summary>Every name the site renders as HTML that the app also decodes, and what it is.</summary>
    private static readonly Dictionary<string, string> Looked = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Description"] =
            "REAL, fixed 2026-09-20. ClientCaseDetail.Description is converted with "
          + "PlainTextHtml.ToText and DescriptionHtml carries the formatting.",

        ["Body"] =
            "REAL, fixed 2026-09-20. ClientCaseOccurrence.Body is converted and BodyHtml carries "
          + "the formatting. CaseMessage.Body was already plain by the same reasoning, guarded by "
          + "CaseMessageBodiesArePlainTextTests.",

        ["Notes"] =
            "Different fields sharing a word. The site renders an Investigation's Notes as HTML "
          + "on a group screen the app has no equivalent of; the app's notes belongs to a hosted "
          + "event hub record and never passes through that screen.",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Property names the website hands to the browser as markup.</summary>
    private static HashSet<string> RenderedAsHtml()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" })
        {
            var root = Path.Combine(RepoRoot(), project);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                var text = File.ReadAllText(file);

                foreach (Match m in Regex.Matches(text, @"MarkupString\)\s*\(?\s*[\w_\.\?]*?\.(\w+)"))
                    names.Add(m.Groups[1].Value);

                foreach (Match m in Regex.Matches(text, @"Html=""@\([^)]*\.(\w+)"))
                    names.Add(m.Groups[1].Value);
            }
        }

        return names;
    }

    /// <summary>Field names the shipped app decodes as a plain string.</summary>
    /// <remarks>
    /// Read from BenKit's models rather than the views: a model field is what arrives from the
    /// server, and any view is free to draw it with Text tomorrow even if none does today.
    /// </remarks>
    private static HashSet<string> DecodedAsStringByTheApp()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(RepoRoot(), "Ben.iOS", "BenKit", "Sources");
        if (!Directory.Exists(root)) return names;

        foreach (var file in Directory.EnumerateFiles(root, "*.swift", SearchOption.AllDirectories))
        {
            // Stored properties only. A computed one ("public var summary: String {") is Swift's
            // own work and never arrives from us.
            foreach (Match m in Regex.Matches(File.ReadAllText(file),
                                              @"public var (\w+)\s*:\s*String\??\s*(?:=[^\n{]*)?$",
                                              RegexOptions.Multiline))
                names.Add(m.Groups[1].Value);
        }

        return names;
    }

    [Fact]
    public void The_app_still_has_models_to_check_against()
    {
        // If the iOS sources move, every check below passes by finding nothing. That failure
        // would be silent and permanent, which is the one outcome worth refusing outright.
        Assert.True(DecodedAsStringByTheApp().Count > 20,
            "Found almost no string fields in BenKit — the iOS sources have moved, and this guard "
          + "is now checking nothing.");

        Assert.True(RenderedAsHtml().Count > 0,
            "Found nothing rendered as MarkupString — the website's markup has changed shape.");
    }

    [Fact]
    public void Every_field_the_site_renders_as_html_and_the_app_reads_has_been_looked_at()
    {
        var overlap = RenderedAsHtml()
            .Where(DecodedAsStringByTheApp().Contains)
            .Where(name => !Looked.ContainsKey(name))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(overlap.Count == 0,
            "These are rendered as HTML by the website AND decoded as plain strings by the iPhone "
          + "app. If they are the same field, the app is showing somebody the tags:"
          + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", overlap)
          + Environment.NewLine + Environment.NewLine
          + "Convert it on the way out with PlainTextHtml.ToText and add the formatted copy beside "
          + $"it, or record in {nameof(Looked)} why the two names are unrelated.");
    }

    [Fact]
    public void Nothing_is_excused_that_no_longer_overlaps()
    {
        var html = RenderedAsHtml();
        var app = DecodedAsStringByTheApp();

        var stale = Looked.Keys
            .Where(name => !(html.Contains(name) && app.Contains(name)))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(stale.Count == 0,
            "These are written down as looked at, but no longer overlap. Delete the entry rather "
          + "than leaving a note nobody can check:" + Environment.NewLine
          + "  " + string.Join(Environment.NewLine + "  ", stale));
    }

    [Fact]
    public void Every_entry_says_what_was_found()
        => Assert.All(Looked, pair =>
            Assert.True(pair.Value.Length > 60,
                $"{pair.Key}'s note is too short to say what was actually checked."));
}
