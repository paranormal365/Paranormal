using System.Text.RegularExpressions;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// The editor works wherever it is mounted: at the site root, and under /editors/canvas/.
/// </summary>
/// <remarks>
/// Copied in intent from Ben.Video.Tests/Services/SubPathHostingTests.cs. A root-absolute asset path
/// is right only at the root of an origin. Under /editors/canvas/ every one of them asks the site root
/// for a file that lives under the editor, and the failure is quiet: a 404 stylesheet is an empty
/// stylesheet, and a 404 module is an editor that renders but does nothing.
/// </remarks>
public sealed class SubPathHostingTests
{
    private static IEnumerable<string> Sources() =>
        RepoFiles.UiFiles("*.cs", "*.razor", "*.js", "*.css").Append(Path.Combine(RepoFiles.HostWwwroot(), "index.html"));

    [Fact]
    public void No_source_file_addresses_its_own_assets_from_the_site_root()
    {
        var offenders = Sources()
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"[""'(]/_content/"))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These address _content/ from the site root, which 404s under /editors/canvas/. Use a "
            + "base-relative path, or CanvasModules.ImportAsync for modules:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Modules_are_imported_through_the_base_aware_loader()
    {
        var offenders = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.cs", "*.razor"))
        {
            var text = RepoFiles.ReadWithoutComments(file);

            if (Regex.IsMatch(text, @"InvokeAsync<IJSObjectReference>\(\s*""import"""))
                offenders.Add($"{RepoFiles.Relative(file)}: uses Blazor's \"import\" identifier");

            if (Path.GetFileName(file) != "CanvasModules.cs" && Regex.IsMatch(text, @"\b(js|JS|Js|jsRuntime|JSRuntime)\.InvokeAsync<IJSObjectReference>\("))
                offenders.Add($"{RepoFiles.Relative(file)}: imports a module without CanvasModules.ImportAsync");
        }

        Assert.True(offenders.Count == 0,
            "Every module import must go through CanvasModules.ImportAsync, which resolves against the page's "
            + "<base>:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_asset_url_in_component_css_resolves_inside_the_library()
    {
        var offenders = new List<string>();
        foreach (var file in RepoFiles.Files(RepoFiles.EditorRoot(), "*.css"))
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"url\(\s*[""']?(/[^)""']*)"))
                offenders.Add($"{RepoFiles.Relative(file)}: {m.Groups[1].Value}");

        Assert.True(offenders.Count == 0,
            "These CSS urls are root-absolute and miss under a sub-path:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_loader_script_exists_and_defines_the_function_callers_use()
    {
        var loader = Path.Combine(RepoFiles.EditorWwwroot(), "js", "moduleLoader.js");
        Assert.True(File.Exists(loader), $"The module loader is missing: {loader}");

        var text = File.ReadAllText(loader);

        Assert.Contains($"window.{CanvasModules.LoaderFunction}", text);
        Assert.Contains("document.baseURI", text);
    }

    [Theory]
    [InlineData("/js/modalInterop.js")]
    [InlineData("_content/Ben.Canvas.Editor/js/modalInterop.js")]
    public void The_import_helper_refuses_a_path_that_builds_its_own_url(string path)
    {
        Assert.Throws<ArgumentException>(() => { _ = CanvasModules.ImportAsync(new NoJs(), path); });
    }
}
