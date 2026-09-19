using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// No Razor comment sits between a tag's attributes.
/// </summary>
/// <remarks>
/// Copied in intent from Ben.Video.Tests/Services/RazorMarkupGuardTests.cs. A <c>@* *@</c> inside an
/// attribute list compiles, then throws at render time and blanks the component. Neither the compiler
/// nor an ordinary unit test catches it.
/// </remarks>
public sealed class RazorMarkupGuardTests
{
    [Fact]
    public void No_razor_comment_sits_between_a_tags_attributes()
    {
        var offenders = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.razor"))
        {
            var text = File.ReadAllText(file);
            // An opening tag (letter after '<') that reaches a Razor comment before its closing '>'.
            foreach (Match m in Regex.Matches(text, @"<[A-Za-z][\w:.-]*\s[^<>]*?@\*", RegexOptions.Singleline))
            {
                var line = text[..m.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{RepoFiles.Relative(file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These put a Razor comment inside a tag's attribute list, which throws at render time. Move the "
            + "comment above the tag:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The board, its blocks and its overlays take no Blazor pointer handlers. The one delegated script
    /// listener owns pointers there; a Blazor handler would render mid-gesture and fight it.
    /// </summary>
    [Fact]
    public void Board_markup_has_no_blazor_pointer_handlers()
    {
        var files = new[]
        {
            Path.Combine(RepoFiles.EditorRoot(), "Components", "Board", "CanvasBoard.razor"),
            Path.Combine(RepoFiles.EditorRoot(), "Components", "Board", "EdgeLayer.razor"),
            Path.Combine(RepoFiles.EditorRoot(), "Components", "Nodes", "NodeFrame.razor"),
        }.Concat(RepoFiles.Files(Path.Combine(RepoFiles.EditorRoot(), "Components", "Overlays"), "*.razor"));

        var offenders = files
            .Where(f => Regex.IsMatch(RepoFiles.ReadWithoutComments(f), @"@on(pointer|mouse|touch)\w*|@onclick|@ondblclick|@oncontextmenu"))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These board files take Blazor pointer events:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Blocks stack by DOM order; an inline z-index would fight the paint order the store keeps.</summary>
    [Fact]
    public void Nodes_never_set_z_index_inline()
    {
        var offenders = RepoFiles.Files(Path.Combine(RepoFiles.EditorRoot(), "Components"), "*.razor")
            .Where(f => RepoFiles.ReadWithoutComments(f).Contains("z-index", StringComparison.Ordinal))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These set z-index in markup:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>url(#id) resolves against &lt;base href&gt; and breaks under a sub-path; ids also collide in the site body.</summary>
    [Fact]
    public void Svg_never_references_ids_by_url()
    {
        var offenders = RepoFiles.UiFiles("*.razor", "*.css")
            .Where(f => RepoFiles.ReadWithoutComments(f).Contains("url(#", StringComparison.Ordinal))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "These reference an element id by url():\n  " + string.Join("\n  ", offenders));
    }
}
