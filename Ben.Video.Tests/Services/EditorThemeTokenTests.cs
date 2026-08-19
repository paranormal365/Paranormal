using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The editor's colour tokens must all exist, and must all come from the site's palette.
/// </summary>
/// <remarks>
/// <para>Two failures this catches, both silent by nature.</para>
///
/// <para><b>A token nothing defines.</b> Components ask for colour as
/// <c>var(--kendo-color-…, var(--bv-…))</c>. If the <c>--bv-*</c> half is never defined and the
/// host has no Kendo theme loaded, the whole declaration is invalid at computed-value time and
/// the property simply drops — a panel loses its background rather than showing a wrong one.
/// Five tokens were in that state (<c>--bv-bg</c>, <c>--bv-panel-bg</c>, <c>--bv-clip-bg</c>,
/// <c>--bv-danger-bg</c>, <c>--bv-danger-text</c>) and nothing said so.</para>
///
/// <para><b>A token that stops following the site.</b> Every editor token is mapped onto a
/// Bootstrap custom property in ben-video-theme.css, which is what makes the editor track the
/// template's light/dark toggle. Re-pointing one at a literal would look correct in whichever
/// theme it was written against and wrong in the other.</para>
/// </remarks>
public sealed class EditorThemeTokenTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string EditorRoot() => Path.Combine(RepoRoot().FullName, "Ben.Video.Editor");

    private static IEnumerable<string> ComponentCss() =>
        Directory.EnumerateFiles(Path.Combine(EditorRoot(), "Components"), "*.css");

    private static IEnumerable<string> ThemeCss() =>
        Directory.EnumerateFiles(Path.Combine(EditorRoot(), "wwwroot", "css"), "*.css");

    /// <summary>Tokens the app sets as an inline style at runtime rather than in a stylesheet.</summary>
    private static readonly string[] SetAtRuntime =
    [
        "--bv-preview-h",       // VideoEditor, from LayoutService
        "--bv-browser-w",       // panel width, dragged by the user
        "--bv-timeline-h",      // panel height, dragged by the user
        "--bv-marker-color",    // per-marker, from the marker's own colour
        "--bv-waveform-height", // per-waveform, from the Height parameter
        "--bv-wf-progress",     // per-waveform playback position
    ];

    [Fact]
    public void Every_referenced_editor_token_is_defined()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ComponentCss().Concat(ThemeCss()))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"var\((--bv-[a-z0-9-]+)"))
                used.Add(m.Groups[1].Value);

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ThemeCss())
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^\s*(--bv-[a-z0-9-]+)\s*:", RegexOptions.Multiline))
                defined.Add(m.Groups[1].Value);

        Assert.NotEmpty(used);

        var missing = used.Except(defined).Except(SetAtRuntime).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "These editor tokens are referenced but never defined, so the declarations using them "
            + "drop entirely wherever the Kendo half of the pair is absent. Define them in "
            + "ben-video-theme.css, or add them to SetAtRuntime if the app sets them inline:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void Editor_tokens_are_mapped_onto_the_site_palette()
    {
        var themeFile = Path.Combine(EditorRoot(), "wwwroot", "css", "ben-video-theme.css");
        Assert.True(File.Exists(themeFile), $"The theme map is missing: {themeFile}");

        var text = File.ReadAllText(themeFile);

        // Each declaration's value must reach a --bs-* custom property, directly or through
        // another --bv-* token that does. A literal-only value is a token that has stopped
        // following the site's theme.
        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(text, @"^\s*(--bv-[a-z0-9-]+)\s*:\s*([^;]+);", RegexOptions.Multiline))
        {
            var name = m.Groups[1].Value;
            var value = m.Groups[2].Value;

            if (!value.Contains("--bs-", StringComparison.Ordinal)
                && !value.Contains("--bv-", StringComparison.Ordinal))
                offenders.Add($"{name}: {value.Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "These editor tokens no longer resolve from the template's palette, so they will not "
            + "follow the light/dark toggle:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_editor_asks_for_its_own_accent_before_the_host_theme()
    {
        // The accent is the one token where the editor must win: the site's --kendo-color-primary
        // is a *fill* colour (#37508a), and the editor also paints text with its accent, which on
        // the dark ground is navy on navy. The editor's own --bv-accent lifts it for text, so any
        // rule that asks Kendo first would reintroduce the unreadable case.
        var offenders = new List<string>();
        foreach (var file in ComponentCss())
        {
            var text = File.ReadAllText(file);
            if (Regex.IsMatch(text, @"var\(\s*--kendo-color-primary\s*,\s*var\(\s*--bv-accent"))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "These files ask for the host's Kendo primary before the editor's own accent, which "
            + "paints navy text on the dark ground. Use var(--bv-accent, var(--kendo-color-primary)):\n  "
            + string.Join("\n  ", offenders));
    }
}
