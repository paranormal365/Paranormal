using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Templates;

/// <summary>
/// Lays a template's board out.
/// </summary>
/// <remarks>
/// <para>The point of a builder rather than hand-written object literals is that three things go wrong
/// silently in a hand-written board and cannot go wrong here: the paint order (<c>Z</c>) drifts out of
/// step with <see cref="CanvasDocument.NextZ"/> so the first block somebody adds lands behind the
/// template; a block is left smaller than its own kind allows, so the first drag jumps it; and a
/// connector or a group id is mistyped, leaving a line joined to nothing.</para>
///
/// <para>So sizes default to the registry's, are floored at the registry's minimum, and every id is
/// handed back by the call that made it.</para>
/// </remarks>
public sealed class BoardBuilder
{
    private readonly CanvasDocument _document;
    private int _z;

    internal BoardBuilder(string title)
    {
        _document = new CanvasDocument { Title = title };
    }

    /// <summary>A panel or outline somebody drew round part of the board.</summary>
    public Guid Group(string label, double x, double y, double width, double height,
        string? colorKey = null, GroupFill fill = GroupFill.Panel)
    {
        var group = new CanvasGroup
        {
            Label = label,
            X = x,
            Y = y,
            Width = Math.Max(width, BlockRegistry.GroupMinWidth),
            Height = Math.Max(height, BlockRegistry.GroupMinHeight),
            Z = _z++,
            ColorKey = Palette(colorKey),
            Fill = fill,
        };

        _document.Groups.Add(group);
        return group.Id;
    }

    /// <summary>A block. Width and height default to the kind's own, and never fall below its minimum.</summary>
    public Guid Node(CanvasNodeType type, double x, double y, NodeData data,
        double? width = null, double? height = null,
        string? colorKey = null, Guid? groupId = null, NodeFill fill = NodeFill.Bar)
    {
        var descriptor = BlockRegistry.Get(type);

        var node = new CanvasNode
        {
            Type = type,
            X = x,
            Y = y,
            Width = Math.Max(width ?? descriptor.DefaultWidth, descriptor.MinWidth),
            Height = Math.Max(height ?? descriptor.DefaultHeight, descriptor.MinHeight),
            Z = _z++,
            ColorKey = Palette(colorKey),
            Fill = fill,
            GroupId = groupId,
            Data = data,
        };

        _document.Nodes.Add(node);
        return node.Id;
    }

    /// <summary>A note, which is what most of a blank template is made of.</summary>
    public Guid Note(double x, double y, string text, double? width = null, double? height = null,
        string? colorKey = null, Guid? groupId = null)
        => Node(CanvasNodeType.Text, x, y, new TextData { Text = text }, width, height, colorKey, groupId);

    /// <summary>A line between two blocks.</summary>
    public Guid Edge(Guid from, Guid to,
        EdgeRoute route = EdgeRoute.Curve,
        EdgeLine line = EdgeLine.Solid,
        EdgeMarker? fromMarker = null,
        EdgeMarker? toMarker = null,
        string? label = null,
        string? colorKey = null)
    {
        var edge = new CanvasEdge
        {
            FromNodeId = from,
            ToNodeId = to,
            Route = route,
            Line = line,
            FromMarker = fromMarker,
            ToMarker = toMarker,
            Label = label,
            ColorKey = Palette(colorKey),
        };

        _document.Edges.Add(edge);
        return edge.Id;
    }

    /// <summary>
    /// A palette key, or nothing. Refuses anything that is not one of the palette's own keys, so a
    /// template cannot ship a colour that reads in one theme and vanishes in the other.
    /// </summary>
    private static string? Palette(string? key)
    {
        if (key is null) return null;

        return CanvasPalette.IsValid(key)
            ? key
            : throw new ArgumentOutOfRangeException(nameof(key), key,
                "A template may only use palette keys, never a colour.");
    }

    internal CanvasDocument Finish(Guid? caseId)
    {
        _document.CaseId = caseId;

        // Ahead of everything placed, or the first block somebody adds lands behind the template.
        _document.NextZ = _z;
        return _document;
    }
}
