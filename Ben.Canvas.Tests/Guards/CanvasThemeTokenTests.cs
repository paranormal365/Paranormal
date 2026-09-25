using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// The editor's colour tokens must all exist, must all come from the site's palette, and no component
/// may paint a colour of its own.
/// </summary>
/// <remarks>
/// <para>Copied from Ben.Video.Tests/Services/EditorThemeTokenTests.cs for the --bc-* family.</para>
///
/// <para><b>A token nothing defines</b> makes its whole declaration invalid at computed-value time, so a
/// panel loses its background rather than showing a wrong one. <b>A token re-pointed at a literal</b>
/// looks right in whichever theme it was written against and wrong in the other. <b>A hard-coded
/// colour in a component</b> is the same failure one step earlier.</para>
/// </remarks>
public sealed class CanvasThemeTokenTests
{
    /// <summary>
    /// Custom properties written per element rather than in the theme: by the board's JavaScript during a
    /// gesture, or inline by a component (a block's palette colour). Defining these on :root would defeat
    /// the fallback every use of them carries.
    /// </summary>
    private static readonly string[] SetAtRuntime =
    [
        "--bc-zoom", "--bc-pan-x", "--bc-pan-y", "--bc-dx", "--bc-dy", "--bc-rx", "--bc-ry", "--bc-rw", "--bc-rh",
        "--bc-guide-pos", "--bc-mq-x", "--bc-mq-y", "--bc-mq-w", "--bc-mq-h",
        "--bc-node-color", "--bc-edge-stroke", "--bc-kb-inset", "--bc-sheet-dy",
    ];

    private static IEnumerable<string> ComponentCss() => RepoFiles.Files(Path.Combine(RepoFiles.EditorRoot(), "Components"), "*.css");

    private static IEnumerable<string> ThemeCss() => RepoFiles.Files(Path.Combine(RepoFiles.EditorWwwroot(), "css"), "*.css");

    [Fact]
    public void Every_referenced_editor_token_is_defined()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ComponentCss().Concat(ThemeCss()))
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"var\((--bc-[a-z0-9-]+)"))
                used.Add(m.Groups[1].Value);

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ThemeCss())
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"^\s*(--bc-[a-z0-9-]+)\s*:", RegexOptions.Multiline))
                defined.Add(m.Groups[1].Value);

        Assert.NotEmpty(used);

        var missing = used.Except(defined).Except(SetAtRuntime).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "These editor tokens are referenced but never defined, so the declarations using them drop "
            + "entirely. Define them in bc-variables.css and map them in bc-theme.css, or add them to "
            + "SetAtRuntime if the board's JavaScript sets them:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Editor_tokens_are_mapped_onto_the_site_palette()
    {
        var themeFile = Path.Combine(RepoFiles.EditorWwwroot(), "css", "bc-theme.css");
        Assert.True(File.Exists(themeFile), $"The theme map is missing: {themeFile}");

        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(themeFile), @"^\s*(--bc-[a-z0-9-]+)\s*:\s*([^;]+);", RegexOptions.Multiline))
        {
            var value = m.Groups[2].Value;
            if (!value.Contains("--bs-", StringComparison.Ordinal) && !value.Contains("--bc-", StringComparison.Ordinal))
                offenders.Add($"{m.Groups[1].Value}: {value.Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "These editor tokens no longer resolve from the site's palette, so they will not follow the "
            + "light/dark toggle:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The site's Kendo primary is a fill colour; as text on the dark ground it is navy on navy. The
    /// editor's own accent is mixed toward the body colour for exactly that reason, so it must be asked first.
    /// </summary>
    [Fact]
    public void The_editor_asks_for_its_own_accent_before_the_host_theme()
    {
        var offenders = ComponentCss()
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"var\(\s*--kendo-color-primary\s*,\s*var\(\s*--bc-accent"))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These ask for the host's Kendo primary before the editor's own accent, which paints navy "
            + "text on the dark ground. Use var(--bc-accent):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_component_css_hardcodes_a_colour()
    {
        var files = ComponentCss().Append(Path.Combine(RepoFiles.EditorWwwroot(), "css", "bc-kit.css"));
        var offenders = new List<string>();

        foreach (var file in files.Where(File.Exists))
        {
            var lines = RepoFiles.ReadWithoutComments(file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla)\(") && !lines[i].Contains("var(--", StringComparison.Ordinal))
                    offenders.Add($"{RepoFiles.Relative(file)}:{i + 1}  {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "These component styles paint a literal colour, which cannot follow the site's theme. Use a "
            + "--bc-* token (or --bs-* inside bc-kit.css):\n  " + string.Join("\n  ", offenders));
    }
}
