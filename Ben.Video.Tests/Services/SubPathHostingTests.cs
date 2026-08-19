using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The editor has to work when it is not served from the root of its origin.
/// </summary>
/// <remarks>
/// <para>Production serves it from a sub-path — https://ishaunted.com/editor/ — because a sub-path
/// inherits the site's certificate while a subdomain needs its own. Every asset the library
/// addressed as "/_content/Ben.Video.Editor/…" then pointed one directory above where it is
/// published: 35 JS module imports, the Kendo theme, both Ben.Video stylesheets. The files were
/// all present in the package. Only the URLs were wrong, and the failure was silent — the app
/// booted, then had no interop and no styling.</para>
///
/// <para>Modules now go through <c>window.benImportEditorModule</c>, which resolves against
/// <c>document.baseURI</c> in the browser, so the answer is right for any mount point and there is
/// nothing per-host to keep in sync.</para>
/// </remarks>
public sealed class SubPathHostingTests
{
    private const string LoaderFunction = "benImportEditorModule";

    private static DirectoryInfo EditorRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir!.FullName, "Ben.Video.Editor"));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(EditorRoot().FullName, "*.razor", SearchOption.AllDirectories)
                 .Concat(Directory.EnumerateFiles(EditorRoot().FullName, "*.cs", SearchOption.AllDirectories))
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                          && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_Source_File_Addresses_Its_Own_Assets_From_The_Site_Root()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"""(\.?/_content/Ben\.Video\.Editor/[^""]*)"""))
                offenders.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        }

        Assert.True(offenders.Count == 0,
            "These paths are anchored to the origin root (or to whichever module happens to be "
            + $"importing, for './'). Pass the library-relative path to {LoaderFunction} instead — "
            + "\"js/domInterop.js\", not \"/_content/Ben.Video.Editor/js/domInterop.js\":\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Modules_Are_Imported_Through_The_Base_Aware_Loader()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            // Blazor's built-in "import" resolves a relative specifier against the importing
            // script, not against <base href>, so it cannot be used for this library's own modules.
            foreach (Match m in Regex.Matches(text, @"InvokeAsync<IJSObjectReference>\(\s*""import"""))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            $"These files import a module through Blazor's \"import\" rather than {LoaderFunction}, "
            + "which is the only one of the two that honours <base href>:\n  "
            + string.Join("\n  ", offenders.Distinct()));
    }

    [Fact]
    public void The_Loader_Script_Exists_And_Defines_The_Function_Callers_Use()
    {
        var loader = Path.Combine(EditorRoot().FullName, "wwwroot", "js", "moduleLoader.js");

        Assert.True(File.Exists(loader),
            $"{loader} is missing — every module import in the editor calls into it.");

        var text = File.ReadAllText(loader);

        Assert.Contains($"window.{LoaderFunction}", text, StringComparison.Ordinal);

        // document.baseURI is the whole point: it is <base href>, so it is right under every host
        // and every mount point.
        Assert.Contains("document.baseURI", text, StringComparison.Ordinal);
    }
}
