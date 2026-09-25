using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Bootstrap utilities pinned to a literal light colour must not be used: they ignore the theme.
/// </summary>
/// <remarks>
/// <para><b>The bug.</b> <c>bg-light</c> resolves through <c>--bs-light-rgb</c>, <c>bg-white</c>
/// through <c>--bs-white-rgb</c>, and <c>text-dark</c> through <c>--bs-dark-rgb</c>. This theme
/// redefines none of those three under <c>[data-bs-theme=dark]</c> — verified against
/// <c>smartapp.min.css</c>, which does redefine <c>--bs-tertiary-bg-rgb</c>,
/// <c>--bs-secondary-bg-rgb</c>, <c>--bs-body-bg-rgb</c>, <c>--bs-emphasis-color</c> and
/// <c>--bs-secondary-color</c>. So a <c>bg-light</c> panel stays white in dark mode while the text
/// inside it keeps the theme's light-on-dark foreground: a white card with near-white text, which
/// is worse than no styling at all. Ben reported exactly this on the audit log's expanded row.
/// Item 132.</para>
///
/// <para><b>The replacements</b>, all confirmed to change under <c>[data-bs-theme=dark]</c>:
/// <c>bg-light</c> → <c>bg-body-tertiary</c> for a surface or <c>bg-body-secondary</c> for a chip,
/// <c>bg-white</c> → <c>bg-body</c>, <c>text-dark</c> → <c>text-body-emphasis</c>,
/// <c>text-bg-light</c> → <c>bg-body-secondary text-body-emphasis</c>.</para>
///
/// <para><b>What is NOT banned, and why the list is shorter than it first looked.</b>
/// <c>alert-light</c> reads <c>--bs-light-bg-subtle</c>, <c>--bs-light-text-emphasis</c> and
/// <c>--bs-light-border-subtle</c>, and this theme <i>does</i> redefine all three (to #343a40,
/// #f8f9fa and #495057) — so the fifteen <c>alert alert-light</c> empty states were always
/// theme-aware and were left alone. <c>text-dark</c> on <c>bg-warning</c> or <c>bg-info</c> is
/// outside what THIS guard polices: neither background changes between themes, so neither is a
/// light-only utility. Guessing from the class name rather than reading the compiled CSS would
/// have "fixed" 80 working things.</para>
///
/// <para><b>Theme-independent is not the same as readable, and this file used to say it was.</b>
/// The sentence above once ended "so dark text on them is correct in both". That was true for
/// <c>bg-warning</c> (#aaa256, an olive — dark text reads 8.01:1) and false for <c>bg-info</c>:
/// this template remaps <c>--bs-info</c> to #66366c, a deep purple, where dark text is 2.31:1 and
/// white is 9.09:1. Thirty-four call sites carried the pair. They were corrected on 2026-09-22 and
/// <see cref="DarkTextIsNeverPairedWithTheInfoBackground"/> keeps them corrected. Recorded at
/// length because an exemption whose stated reason has quietly stopped being true is worse than
/// no exemption — this file says so itself, two paragraphs down.
/// </para>
/// </remarks>
public sealed class FixedLightUtilityGuardTests
{
    /// <summary>Utilities with no dark-theme definition. The value is the fix to suggest.</summary>
    private static readonly Dictionary<string, string> Banned = new()
    {
        ["bg-light"]     = "bg-body-tertiary (surface) or bg-body-secondary (chip)",
        ["bg-white"]     = "bg-body",
        ["text-bg-light"]= "bg-body-secondary text-body-emphasis",
    };

    /// <summary>
    /// Backgrounds that are the same colour in both themes, so a fixed text colour on them is not
    /// a THEME fault. Whether it is a readable one is a separate question — see the remarks.
    /// </summary>
    private static readonly string[] ThemeIndependentBackgrounds = ["bg-warning", "bg-info"];

    /// <summary>
    /// Places a fixed colour is the correct answer, with the reason. Keep this short: every entry
    /// is a hole in the rule, and one that stops being true is worse than no entry at all.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["BenQrPlate.razor"] =
            "The QR code container, and the only one. A QR code is read by a camera, not a "
          + "person, and scanners need the light modules light in either theme. It was the "
          + "authenticator panel's own div until the guest's pass needed a code as well; one "
          + "component means one place decides what a camera needs.",
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips comments first. Five guards in this codebase have fired on their own prose, and this
    /// one's explanation names every class it bans. The <c>/*</c> lookbehind keeps a file input's
    /// <c>accept="image/*"</c> from swallowing the rest of the file.
    /// </summary>
    private static string StripComments(string source)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            source, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"(?<![\w""'])/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<string> RazorFiles() =>
        new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Razor AND C#, across the three projects that write markup classes.
    /// </summary>
    /// <remarks>
    /// Wider than <see cref="RazorFiles"/> because class strings are not only in markup: the
    /// status-to-badge mapping that carried a third of the text-dark/bg-info pairs lives in
    /// Ben.Web.Services as a C# switch, and a scan of .razor alone would have reported it clean.
    /// </remarks>
    private static IEnumerable<string> SourceFiles() =>
        new[] { "Ben.Web.Website.Library", "Ben.Web.Website", "Ben.Web.Services" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>Matches the class as a whole word, so bg-body-secondary is not read as bg-body.</summary>
    private static bool UsesClass(string line, string cls) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            line, $@"(?<![\w-]){System.Text.RegularExpressions.Regex.Escape(cls)}(?![\w-])");

    [Fact]
    public void No_razor_file_uses_a_background_pinned_to_one_theme()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var name = Path.GetFileName(file);
            if (Allowed.ContainsKey(name)) continue;

            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (var (cls, fix) in Banned)
                    if (UsesClass(lines[i], cls))
                        offenders.Add($"{name}:{i + 1} — {cls} → {fix}");
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             These use a background that ignores the viewer's theme:

               {string.Join("\n  ", offenders)}

             In dark mode the panel stays white while the text inside stays light — a white card
             with near-white text (item 132). Use the theme-aware token instead, or add the file
             to FixedLightUtilityGuardTests.Allowed with the reason it must be fixed.
             """);
    }

    [Fact]
    public void Dark_text_appears_only_on_a_background_that_never_changes()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var name  = Path.GetFileName(file);
            var lines = StripComments(File.ReadAllText(file)).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (!UsesClass(lines[i], "text-dark")) continue;
                if (ThemeIndependentBackgrounds.Any(bg => UsesClass(lines[i], bg))) continue;

                offenders.Add($"{name}:{i + 1} — {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             These put theme-independent dark text on a background that follows the theme:

               {string.Join("\n  ", offenders)}

             In dark mode that is dark text on a dark surface. text-dark is only safe on a
             background that is the same colour in both themes ({string.Join(", ", ThemeIndependentBackgrounds)});
             everywhere else use text-body-emphasis.
             """);
    }

    /// <summary>An allowlist entry that has stopped being true is worse than no entry.</summary>
    [Fact]
    public void Every_allowed_exception_is_still_real()
    {
        var files = RazorFiles().ToList();

        foreach (var (name, reason) in Allowed)
        {
            var match = files.FirstOrDefault(f => Path.GetFileName(f) == name);
            Assert.True(match is not null, $"Allowed exception '{name}' no longer exists — remove it.");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"Allowed exception '{name}' has no reason.");

            var source = StripComments(File.ReadAllText(match!));
            Assert.True(
                Banned.Keys.Any(cls => source.Split('\n').Any(l => UsesClass(l, cls))),
                $"'{name}' no longer uses a fixed-light utility — remove it from Allowed.");
        }
    }

    /// <summary>
    /// Nothing pairs <c>text-dark</c> with <c>bg-info</c>.
    /// </summary>
    /// <remarks>
    /// <para>Bootstrap's own <c>--bs-info</c> is a bright cyan and dark text on it is right, which
    /// is why the pairing looks idiomatic and spread to thirty-four call sites. This template
    /// remaps <c>--bs-info</c> to #66366c, a deep purple: dark text on it measures 2.31:1 where
    /// small text needs 4.5:1, and white measures 9.09:1. Dropping <c>text-dark</c> is the whole
    /// fix, because a badge with no text colour inherits the white the background expects.</para>
    ///
    /// <para>Scanned as source rather than measured in a browser on purpose: the pairing is
    /// written in C# helpers and in ternaries inside markup, so it reaches the screen from places
    /// no walk reliably opens. Six of the thirty-four were on administration pages alone.</para>
    /// </remarks>
    [Fact]
    public void DarkTextIsNeverPairedWithTheInfoBackground()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pair in new[] { "bg-info text-dark", "text-dark bg-info" })
                if (text.Contains(pair, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} — \"{pair}\"");
        }

        Assert.True(offenders.Count == 0,
            "text-dark on bg-info is 2.31:1 against this template's purple. Drop text-dark; the "
          + "background already expects white:\n  " + string.Join("\n  ", offenders));
    }
}
