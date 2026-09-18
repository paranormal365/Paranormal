using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Components.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Ben.Canvas.Editor.Blocks;

/// <summary>
/// Which component draws each kind of block.
/// </summary>
/// <remarks>
/// Core knows the facts about a block and nothing about Razor, so the mapping lives here. Every type must
/// have an entry; a test enumerates the block types to prove it.
/// </remarks>
public static class BlockRendererMap
{
    private static readonly Dictionary<CanvasNodeType, Type> Renderers = new()
    {
        [CanvasNodeType.Card] = typeof(CardNode),
        [CanvasNodeType.Message] = typeof(MessageNode),
        [CanvasNodeType.Map] = typeof(MapNode),
        [CanvasNodeType.Image] = typeof(ImageNode),
        [CanvasNodeType.Link] = typeof(LinkNode),
        [CanvasNodeType.Text] = typeof(TextNode),
        [CanvasNodeType.Table] = typeof(TableNode),
        [CanvasNodeType.File] = typeof(FileNode),
        [CanvasNodeType.Audio] = typeof(AudioNode),
        [CanvasNodeType.Video] = typeof(VideoNode),
    };

    public static Type RendererFor(CanvasNodeType type) =>
        Renderers.TryGetValue(type, out var renderer) ? renderer : typeof(GenericNodeBody);

    public static IReadOnlyDictionary<CanvasNodeType, Type> All => Renderers;
}

/// <summary>What every block body receives.</summary>
public abstract class BlockRendererBase : ComponentBase
{
    [Parameter, EditorRequired] public CanvasNode Node { get; set; } = default!;

    /// <summary>The block's data: the live data at rest, a draft copy while editing.</summary>
    [Parameter, EditorRequired] public NodeData Data { get; set; } = default!;

    [Parameter] public bool Editing { get; set; }

    [Inject] protected CanvasStore Store { get; set; } = default!;

    [Inject] protected IOptions<CanvasEditorOptions> Options { get; set; } = default!;

    /// <summary>The field that takes focus when editing starts. Renderers put @ref on their first input.</summary>
    protected ElementReference FirstField;

    private bool _focused;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!Editing)
        {
            _focused = false;
            return;
        }

        if (_focused || FirstField.Context is null) return;
        _focused = true;
        try { await FirstField.FocusAsync(); }
        catch (Exception) { /* not rendered interactively, or the element went away */ }
    }
}
