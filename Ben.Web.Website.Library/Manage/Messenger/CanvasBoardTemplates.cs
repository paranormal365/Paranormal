namespace Ben.Web.Website.Library.Manage.Messenger;

/// <summary>One of the boards a new board can start from.</summary>
/// <param name="Id">What goes in the editor's link. Must match the canvas catalogue exactly.</param>
/// <param name="Name">What the person choosing sees.</param>
/// <param name="Summary">One line saying what they would get.</param>
/// <param name="IconName">A symbol in the site's Feather sprite.</param>
public sealed record CanvasBoardTemplate(string Id, string Name, string Summary, string IconName);

/// <summary>
/// The boards on offer when somebody starts a new one from the Research tab.
/// </summary>
/// <remarks>
/// <para><b>Why this list is written out again here.</b> The catalogue itself lives in
/// <c>Ben.Canvas.Core.Templates.BoardTemplates</c>, and this project deliberately does not reference
/// it — the same rule <c>CanvasBoardLinks</c> follows on the API side: the canvas libraries are
/// WebAssembly-facing, and the contract between the site and the editor is the URL, not a shared
/// assembly. The site needs nothing from the catalogue except five ids and some words.</para>
///
/// <para><b>What stops it drifting.</b> <c>Ben.Web.Tests</c> references both, so
/// <c>CanvasBoardTemplateCatalogueTests</c> asserts these ids are the canvas's ids, in the same order.
/// A template added, renamed or dropped in Core fails that test rather than quietly shipping a button
/// that opens a blank board.</para>
///
/// <para>The words are the site's own to change: this is where somebody chooses, and a sentence
/// written for a picker may want to read differently from one written for a catalogue. Only the ids
/// are pinned.</para>
/// </remarks>
public static class CanvasBoardTemplates
{
    public static IReadOnlyList<CanvasBoardTemplate> All { get; } =
    [
        new("blank", "Blank board", "Nothing on it. Build it your own way.", "file"),
        new("moodboard", "Moodboard", "Coloured sections for pictures and feel, and a cluster of themes.", "layout"),
        new("research-plan", "Research plan", "What to ask, where to look, what matters most, and when.", "clipboard"),
        new("family-tree", "Family tree", "Who is related to whom, with a photo frame above every name.", "git-merge"),
        new("deck", "Presentation deck", "Slide frames you can walk somebody through.", "monitor"),
    ];
}
