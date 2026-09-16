using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// The standalone host's shell: base href, stylesheet order, viewport, theme boot, fonts and class names.
/// </summary>
/// <remarks>
/// The stylesheet order is load-bearing and invisible: both the Kendo theme and the palette define
/// variables on :root, so whichever loads later wins, and the wrong order paints the editor white on a
/// dark page without a single error.
/// </remarks>
public sealed class HostShellGuardTests
{
    private static string IndexHtml() => File.ReadAllText(Path.Combine(RepoFiles.HostWwwroot(), "index.html"));

    private static string IndexWithoutComments() => RepoFiles.StripComments(IndexHtml(), "html");

    [Fact]
    public void Exactly_one_base_href_and_it_is_root()
    {
        var bases = Regex.Matches(IndexWithoutComments(), @"<base\s[^>]*>");

        var single = Assert.Single(bases);
        Assert.Contains("href=\"/\"", single.Value);
    }

    [Fact]
    public void No_asset_path_starts_at_the_site_root()
    {
        var offenders = Regex.Matches(IndexWithoutComments(), @"\b(href|src)=""(/[^""]*)""")
            .Select(m => m.Value)
            .Where(v => !v.StartsWith("href=\"/\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "Root-absolute paths miss under /editors/canvas/:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_stylesheets_load_in_the_binding_order()
    {
        var html = IndexWithoutComments();
        string[] order =
        [
            "fonts/fonts.css",
            "Ben.Wasm.Canvas.styles.css",
            "kendo-theme-bootstrap/all.css",
            "css/site-palette.css",
            "theme/telerik-night.css",
            "css/bc-variables.css",
            "css/bc-theme.css",
            "css/bc-kit.css",
            "css/bootstrap-subset.css",
            "css/app.css",
        ];

        var positions = order.Select(name => (name, at: html.IndexOf(name, StringComparison.Ordinal))).ToList();

        Assert.All(positions, p => Assert.True(p.at >= 0, $"{p.name} is not linked."));
        for (var i = 1; i < positions.Count; i++)
            Assert.True(positions[i - 1].at < positions[i].at,
                $"{positions[i - 1].name} must load before {positions[i].name}; the later file wins on :root.");
    }

    [Fact]
    public void The_loader_is_a_classic_script_before_blazor()
    {
        var html = IndexWithoutComments();
        var loader = Regex.Match(html, @"<script[^>]*moduleLoader\.js[^>]*>");
        var blazor = html.IndexOf("blazor.webassembly", StringComparison.Ordinal);

        Assert.True(loader.Success, "moduleLoader.js is not loaded.");
        Assert.DoesNotContain("type=\"module\"", loader.Value);
        Assert.True(loader.Index < blazor, "moduleLoader.js must load before Blazor starts.");
    }

    [Fact]
    public void The_viewport_meta_owns_the_keyboard()
    {
        var meta = Regex.Match(IndexWithoutComments(), @"<meta\s+name=""viewport""\s+content=""([^""]+)""");

        Assert.True(meta.Success);
        Assert.Equal("width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content", meta.Groups[1].Value);
        Assert.DoesNotContain("user-scalable", meta.Groups[1].Value);
        Assert.DoesNotContain("maximum-scale", meta.Groups[1].Value);
    }

    [Fact]
    public void The_page_is_night_coloured_before_css()
    {
        var html = IndexWithoutComments();

        Assert.Contains("<meta name=\"theme-color\" content=\"#212529\"", html);
        var setsTheme = html.IndexOf("setAttribute('data-bs-theme'", StringComparison.Ordinal);
        var firstStylesheet = html.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);
        Assert.True(setsTheme >= 0 && setsTheme < firstStylesheet, "The theme must be set before the first stylesheet, or the page flashes light.");
    }

    [Fact]
    public void The_shell_has_no_manifest_link() =>
        Assert.DoesNotContain("rel=\"manifest\"", IndexWithoutComments());

    [Fact]
    public void The_title_names_the_product() =>
        Assert.Contains("<title>IsHaunted Canvas</title>", IndexWithoutComments());

    [Fact]
    public void Fonts_resolve_against_the_base()
    {
        var fonts = RepoFiles.ReadWithoutComments(Path.Combine(RepoFiles.HostWwwroot(), "fonts", "fonts.css"));

        Assert.DoesNotContain("url(/", fonts);
        Assert.Contains("url(ps-", fonts);
    }

    [Fact]
    public void The_shell_uses_bwc_classes()
    {
        var offenders = RepoFiles.Files(RepoFiles.HostRoot(), "*.css", "*.razor", "*.html")
            .Where(f => RepoFiles.ReadWithoutComments(f).Contains("bwv-", StringComparison.Ordinal))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "The video editor's bwv- classes leaked into the canvas host:\n  " + string.Join("\n  ", offenders));
    }
}
