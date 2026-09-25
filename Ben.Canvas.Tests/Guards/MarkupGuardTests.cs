using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// Vendored plugins are documented.
/// </summary>
/// <remarks>
/// The site's rule (VendoredPluginsAreDocumentedTests): a library copied into the repository names its
/// version, source and licence beside it, or nobody can later tell whether it is safe to update or
/// whether its licence still permits use. ApexCharts was pinned below v5 for exactly that reason.
/// </remarks>
public sealed class VendoredPluginsAreDocumentedTests
{
    [Fact]
    public void Every_plugin_carries_its_licence_and_a_vendored_note()
    {
        var root = Path.Combine(RepoFiles.EditorWwwroot(), "plugins");
        if (!Directory.Exists(root)) return;

        var problems = new List<string>();

        foreach (var plugin in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(plugin);
            if (!Directory.EnumerateFiles(plugin, "LICENSE*").Any())
                problems.Add($"{name}: no LICENSE file");

            var note = Path.Combine(plugin, "VENDORED.md");
            if (!File.Exists(note)) { problems.Add($"{name}: no VENDORED.md"); continue; }

            var text = File.ReadAllText(note);
            if (!Regex.IsMatch(text, @"\b\d+\.\d+\.\d+\b")) problems.Add($"{name}: VENDORED.md names no version");
            if (!text.Contains("https://", StringComparison.Ordinal)) problems.Add($"{name}: VENDORED.md names no https source");
            if (!Regex.IsMatch(text, @"\b[0-9a-fA-F]{64}\b")) problems.Add($"{name}: VENDORED.md has no SHA-256");
        }

        Assert.True(problems.Count == 0, "Vendored plugins must be documented:\n  " + string.Join("\n  ", problems));
    }
}

/// <summary>
/// Bootstrap utilities pinned to a light colour are not used.
/// </summary>
/// <remarks>
/// Copied in intent from Ben.Web.Tests/Services/FixedLightUtilityGuardTests.cs. bg-light, bg-white and
/// text-bg-light have no dark-theme definition, so in dark mode the panel stays white while its text
/// stays light: a white card with near-white text.
/// </remarks>
public sealed class FixedLightUtilityGuardTests
{
    private static readonly Dictionary<string, string> Banned = new()
    {
        ["bg-light"] = "bg-body-tertiary (surface) or bg-body-secondary (chip)",
        ["bg-white"] = "bg-body",
        ["text-bg-light"] = "bg-body-secondary text-body-emphasis",
        ["text-dark"] = "text-body-emphasis",
    };

    [Fact]
    public void No_razor_file_uses_a_background_pinned_to_one_theme()
    {
        var offenders = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.razor"))
        {
            var lines = RepoFiles.ReadWithoutComments(file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (var (cls, fix) in Banned)
                    if (Regex.IsMatch(lines[i], $@"(?<![\w-]){Regex.Escape(cls)}(?![\w-])"))
                        offenders.Add($"{RepoFiles.Relative(file)}:{i + 1} - {cls} -> {fix}");
        }

        Assert.True(offenders.Count == 0, "These ignore the viewer's theme:\n  " + string.Join("\n  ", offenders));
    }
}

/// <summary>
/// Every label points at a control that exists.
/// </summary>
/// <remarks>
/// A label whose <c>for</c> names nothing is announced as nothing and cannot be tapped to focus its field,
/// which on an iPhone is the difference between a 44px target and a tiny input.
/// </remarks>
public sealed class LabelAssociationTests
{
    [Fact]
    public void Every_label_for_names_an_id_in_the_same_file()
    {
        var offenders = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.razor"))
        {
            var text = RepoFiles.ReadWithoutComments(file);
            var ids = Regex.Matches(text, @"\bid=""([^""@]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            foreach (Match m in Regex.Matches(text, @"<label[^>]*\bfor=""([^""@]+)"""))
                if (!ids.Contains(m.Groups[1].Value))
                    offenders.Add($"{RepoFiles.Relative(file)}: for=\"{m.Groups[1].Value}\"");
        }

        Assert.True(offenders.Count == 0, "These labels name no control:\n  " + string.Join("\n  ", offenders));
    }
}

/// <summary>
/// A button that shows only an icon still has a name.
/// </summary>
/// <remarks>
/// VoiceOver reads an unnamed icon button as "button", which on a toolbar of ten of them is useless.
/// </remarks>
public sealed class IconButtonsHaveNamesTests
{
    [Fact]
    public void Every_icon_only_button_has_an_accessible_name()
    {
        var offenders = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.razor"))
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"<button([^>]*)>\s*<BcIcon[^>]*/>\s*</button>", RegexOptions.Singleline))
                if (!Regex.IsMatch(m.Groups[1].Value, @"\b(aria-label|title)="))
                    offenders.Add($"{RepoFiles.Relative(file)}: {m.Value.Split('\n')[0].Trim()}");

        Assert.True(offenders.Count == 0, "These icon-only buttons have no aria-label or title:\n  " + string.Join("\n  ", offenders));
    }
}

/// <summary>
/// No script writes markup outside the sanitiser.
/// </summary>
/// <remarks>
/// The canvas renders pasted content. Markup written through innerHTML anywhere but the one module that
/// sanitises is an injection route; M4 adds the fact that the JS allow-list equals Core's.
/// </remarks>
public sealed class HtmlAllowListTests
{
    private static readonly HashSet<string> Sanitisers = new(StringComparer.Ordinal);

    [Fact]
    public void No_script_writes_markup_outside_the_sanitiser()
    {
        var offenders = RepoFiles.Files(RepoFiles.EditorWwwroot(), "*.js")
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}plugins{Path.DirectorySeparatorChar}"))
            .Where(f => !Sanitisers.Contains(Path.GetFileName(f)))
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"innerHTML\s*=|outerHTML\s*=|insertAdjacentHTML|document\.write"))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These write markup directly:\n  " + string.Join("\n  ", offenders));
    }
}

/// <summary>
/// JavaScript never creates, moves or removes the nodes Blazor renders.
/// </summary>
/// <remarks>
/// Blazor diffs against the tree it rendered. A script that re-parents or removes one of those nodes
/// leaves the diff patching a tree that no longer matches, and edits are silently lost - the reason the
/// site banned Bootstrap's own modal plugin. Scripts may toggle their own classes and custom properties.
/// </remarks>
public sealed class JsNeverCreatesNodesTests
{
    /// <summary>Files allowed to create an element, with the reason.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["domInterop.js"] = "a transient download link, removed in the same call; desktop browsers start a download only from a link",
        ["pasteInterop.js"] = "a canvas that is never attached, to redraw a photo where the browser has no OffscreenCanvas",
        ["mapInterop.js"] = "the MapKit JS script tag, which is how Apple ships MapKit, added once when a live map is first asked for",
        ["snapshotInterop.js"] = "a canvas that is never attached, to draw the published picture where the browser has no OffscreenCanvas",
    };

    private static IEnumerable<string> Scripts() =>
        RepoFiles.Files(RepoFiles.EditorWwwroot(), "*.js")
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}plugins{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_script_creates_elements_outside_its_allow_list()
    {
        var offenders = Scripts()
            .Where(f => !Allowed.ContainsKey(Path.GetFileName(f)))
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"createElement\s*\(|cloneNode\s*\(|appendChild\s*\(|insertBefore\s*\(|removeChild\s*\(|\.remove\(\s*\)"))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These create, move or remove nodes:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_script_restyles_a_board_node_by_its_class()
    {
        var offenders = Scripts()
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"(classList\.add|className\s*=|setAttribute\(\s*['""]class)\s*\(?\s*['""](bc-node|bc-edge|bc-group)['""]"))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Board nodes are Blazor-rendered only:\n  " + string.Join("\n  ", offenders));
    }
}
