using Telerik.Blazor.Components;
using Telerik.Blazor.Components.Editor;

namespace Ben.Web.Website.Library.Kit.Blocks;

/// <summary>The toolbar a text block offers: a little more than a message's — headings and quotes, because a page has sections.</summary>
public static class BlockTextTools
{
    public static readonly List<IEditorTool> Default =
    [
        new EditorButtonGroup(new Bold(), new Italic(), new Underline()),
        new Format(),
        new EditorButtonGroup(new UnorderedList(), new OrderedList(), new Indent(), new Outdent()),
        new EditorButtonGroup(new CreateLink(), new Unlink()),
        new EditorButtonGroup(new Undo(), new Redo()),
    ];
}
