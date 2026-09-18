using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Paste;

namespace Ben.Canvas.Core.Serialization;

/// <summary>
/// Brings a board read from anywhere up to what this build expects.
/// </summary>
/// <remarks>
/// <para>The rule, copied from the video editor's migrations: changes are additive. A field that did not
/// exist gets its default, a value that cannot be right is cleared, and the one destructive exception - a
/// connector to a block that is not there - is removed because it can be neither drawn nor selected.</para>
///
/// <para>This also runs for boards that were never old: a hand-edited file, a board from the server and a
/// board restored from the device all come through here, so it is where message HTML is cleaned and unsafe
/// link addresses are dropped, whatever wrote them.</para>
///
/// <para>Upgrading twice gives the same board as upgrading once.</para>
/// </remarks>
public static class CanvasDocumentMigrations
{
    public static CanvasDocument Upgrade(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // if (document.SchemaVersion < 2) { ...a future migration goes here... }

        document.Title = string.IsNullOrWhiteSpace(document.Title) ? Text.CanvasCopy.Titles.DefaultBoardTitle : document.Title;
        document.Nodes ??= [];
        document.Edges ??= [];
        document.Groups ??= [];

        document.Nodes.RemoveAll(n => n is null);
        document.Edges.RemoveAll(e => e is null);
        document.Groups.RemoveAll(g => g is null);

        var groupIds = document.Groups.Select(g => g.Id).ToHashSet();
        foreach (var group in document.Groups)
        {
            group.Label ??= "";
            if (!CanvasPalette.IsValid(group.ColorKey)) group.ColorKey = null;
            group.Width = Finite(group.Width, BlockRegistry.GroupMinWidth, BlockRegistry.GroupMinWidth);
            group.Height = Finite(group.Height, BlockRegistry.GroupMinHeight, BlockRegistry.GroupMinHeight);
            group.X = Finite(group.X, 0, double.MinValue);
            group.Y = Finite(group.Y, 0, double.MinValue);
        }

        foreach (var node in document.Nodes)
        {
            var descriptor = BlockRegistry.Get(node.Type);
            node.Data ??= descriptor.CreateDefaultData(document.SavedAtUtc);
            if (node.GroupId is { } gid && !groupIds.Contains(gid)) node.GroupId = null;
            if (!CanvasPalette.IsValid(node.ColorKey)) node.ColorKey = null;
            node.X = Finite(node.X, 0, double.MinValue);
            node.Y = Finite(node.Y, 0, double.MinValue);
            node.Width = Finite(node.Width, descriptor.DefaultWidth, descriptor.MinWidth);
            node.Height = Finite(node.Height, descriptor.DefaultHeight, descriptor.MinHeight);
            CleanData(node.Data);
        }

        var nodeIds = document.Nodes.Select(n => n.Id).ToHashSet();
        document.Edges.RemoveAll(e => !nodeIds.Contains(e.FromNodeId) || !nodeIds.Contains(e.ToNodeId));
        foreach (var edge in document.Edges)
        {
            if (!CanvasPalette.IsValid(edge.ColorKey)) edge.ColorKey = null;

            // The old single Arrow switch, resolved into the two ends once — here, on read, so that
            // nothing downstream has to know whether a board predates M9. Null means "not chosen",
            // which is exactly how a board written before markers existed is told apart from one that
            // deliberately chose to draw nothing; a marker that WAS chosen is left alone.
            edge.FromMarker ??= edge.Arrow == EdgeArrow.Both ? EdgeMarker.Arrow : EdgeMarker.None;
            edge.ToMarker ??= edge.Arrow is EdgeArrow.End or EdgeArrow.Both ? EdgeMarker.Arrow : EdgeMarker.None;

            // The icon is drawn inside the line, so it is clamped rather than allowed to run across
            // the board. Blank reads as nothing, not as an empty box.
            edge.Icon = string.IsNullOrWhiteSpace(edge.Icon)
                ? null
                : edge.Icon.Trim()[..Math.Min(CanvasEdge.MaxIconLength, edge.Icon.Trim().Length)];
        }

        var maxZ = document.Nodes.Select(n => n.Z).Concat(document.Groups.Select(g => g.Z)).DefaultIfEmpty(-1).Max();
        if (document.NextZ <= maxZ) document.NextZ = maxZ + 1;

        document.SchemaVersion = CanvasDocument.CurrentSchemaVersion;
        return document;
    }

    private static void CleanData(NodeData data)
    {
        // A grid from a hand-edited file, a newer editor or a ragged selection is squared up rather
        // than refused: a table missing a cell is still a readable table.
        if (data is TableData table) table.Square();

        // A shape kind this build does not know is drawn as a box rather than refusing the board:
        // a shape is decoration, and losing the whole board over one would be the worse trade.
        if (data is ShapeData shape && !Enum.IsDefined(shape.Kind)) shape.Kind = ShapeKind.Rectangle;

        switch (data)
        {
            case CardData card:
                card.Fields ??= new(StringComparer.Ordinal);
                card.Title ??= "";
                card.TemplateId ??= "evidence";
                break;
            case MessageData message:
                message.Html = PasteHtmlAllowList.Normalize(message.Html);
                message.Author ??= "";
                break;
            case MapData map:
                map.Pins ??= [];
                map.Pins.RemoveAll(p => p is null);
                if (!double.IsFinite(map.Latitude) || Math.Abs(map.Latitude) > 90) map.Latitude = 0;
                if (!double.IsFinite(map.Longitude) || Math.Abs(map.Longitude) > 180) map.Longitude = 0;
                if (!double.IsFinite(map.Zoom) || map.Zoom is < 1 or > 20) map.Zoom = 14;
                break;
            case LinkData link:
                link.Url = SafeHttpUrl.Normalize(link.Url) ?? "";
                if (!SafeHttpUrl.IsHttps(link.ImageSourceUrl)) link.ImageSourceUrl = null;
                break;
            case TextData text:
                text.Text ??= "";
                break;
            case FileData file:
                file.FileName ??= "";
                file.ContentType ??= "";
                break;
        }
    }

    private static double Finite(double value, double fallback, double minimum)
    {
        var v = double.IsFinite(value) ? value : fallback;
        return v < minimum ? minimum : v;
    }
}
