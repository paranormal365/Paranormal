using Telerik.Blazor.Components;
using Telerik.Blazor.Components.Editor;

namespace Ben.Web.Website.Library.Organization.Cases;

/// <summary>
/// One toolbar vocabulary for case prose: the case description, case notes and messages to the client.
/// </summary>
/// <remarks>
/// Emphasis, lists, links and undo. No headings, tables or images: a case summary is a paragraph, not a page, and
/// the editor's full set wrapped to three rows inside a dialog and five on a phone, which was half of what made the
/// Edit Case dialog feel cramped (beta feedback, 2026-09-14). One list rather than one per screen so the three
/// editors cannot drift apart.
/// </remarks>
public static class CaseEditorTools
{
    public static readonly List<IEditorTool> Prose =
    [
        new EditorButtonGroup(new Bold(), new Italic(), new Underline()),
        new EditorButtonGroup(new UnorderedList(), new OrderedList()),
        new EditorButtonGroup(new CreateLink(), new Unlink()),
        new EditorButtonGroup(new Undo(), new Redo()),
    ];
}
