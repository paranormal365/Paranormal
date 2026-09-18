using System.Globalization;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Formatting;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Paste;

namespace Ben.Canvas.Core.Persistence;

/// <summary>A block as the published picture draws it.</summary>
/// <param name="Lines">The words under the title, in order; the drawer wraps and cuts them to the box.</param>
/// <param name="ImageUrl">A displayable address for an image block's picture, filled in by the editor before drawing.</param>
public sealed record SnapshotBlock(
    double X, double Y, double Width, double Height,
    string Kind, string? ColorKey, string Title, IReadOnlyList<string> Lines,
    Guid? AssetId, string? Ext, Guid? UploadFileId, string? ImageUrl, bool Filled = false);

/// <summary>A connector as the published picture draws it.</summary>
/// <param name="Path">The SVG path of the curve (a canvas Path2D reads it directly).</param>
/// <param name="Heads">Arrowhead triangles, six numbers each (three points).</param>
public sealed record SnapshotConnector(
    string Path, IReadOnlyList<SnapshotHead> Heads, string? Label, double LabelX, double LabelY, string? ColorKey,
    bool Dashed = false, string? Icon = null);

/// <summary>
/// One end marker as the published picture draws it.
/// </summary>
/// <remarks>
/// Every head used to be six numbers meaning "a triangle". That stopped being true the moment a
/// connector could end in a diamond or a dot, so the shape travels with the points: a polygon arrives
/// as flat x,y pairs the painter fills without knowing which it is, and a dot — which cannot be
/// written as a polygon — arrives as its centre and radius instead.
/// </remarks>
/// <param name="Shape">"arrow", "diamond" or "dot".</param>
/// <param name="Points">Flat x,y pairs for a polygon, or centre x, centre y, radius for a dot.</param>
public sealed record SnapshotHead(string Shape, IReadOnlyList<double> Points);

/// <summary>A group as the published picture draws it.</summary>
public sealed record SnapshotGroup(double X, double Y, double Width, double Height, string Label, string? ColorKey, GroupFill Fill = GroupFill.Outline);

/// <summary>
/// Everything a published picture of a board needs, in world coordinates, and how big the picture is.
/// </summary>
/// <param name="X">The world point at the picture's top-left corner.</param>
/// <param name="Scale">Picture pixels per world unit.</param>
public sealed record SnapshotScene(
    double X, double Y, double Scale, int PixelWidth, int PixelHeight,
    IReadOnlyList<SnapshotGroup> Groups, IReadOnlyList<SnapshotConnector> Connectors, IReadOnlyList<SnapshotBlock> Blocks);

/// <summary>
/// Builds the published picture of a board from the board itself (review correction R11).
/// </summary>
/// <remarks>
/// <para>The picture is drawn from the document, not captured from the screen, so every block is in it even on a
/// board too big to render at once, nothing depends on the zoom or on what was scrolled into view, and the same
/// board always makes the same picture on every browser.</para>
///
/// <para>The long edge is at most <see cref="MaxLongEdge"/> pixels and the area at most <see cref="MaxPixels"/>,
/// because Safari refuses a larger canvas. Small boards are drawn at twice the size so text stays sharp.</para>
///
/// <para>A map box is drawn as its address and coordinates, never as map imagery: Apple's terms allow map data to
/// be stored only temporarily, and a published picture is kept in the case (R35).</para>
/// </remarks>
public static class BoardSnapshot
{
    public const int MaxLongEdge = 4096;
    public const int MaxPixels = 16_000_000;
    public const double Margin = 40;
    public const double MaxScale = 2;

    public static SnapshotScene Build(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rects = document.Nodes.Select(CanvasHitTester.RectOf).Concat(document.Groups.Select(CanvasHitTester.RectOf)).ToList();
        var bounds = rects.Count == 0 ? new WorldRect(0, 0, 400, 300) : WorldRect.Bounds(rects);
        bounds = new WorldRect(bounds.X - Margin, bounds.Y - Margin, bounds.Width + 2 * Margin, bounds.Height + 2 * Margin);

        var scale = Math.Min(MaxScale, MaxLongEdge / Math.Max(bounds.Width, bounds.Height));
        scale = Math.Min(scale, Math.Sqrt(MaxPixels / (bounds.Width * bounds.Height)));
        var width = Math.Max(1, (int)Math.Floor(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Floor(bounds.Height * scale));

        var groups = document.Groups
            .OrderBy(g => g.Z)
            .Select(g => new SnapshotGroup(g.X, g.Y, g.Width, g.Height, g.Label, g.ColorKey, g.Fill))
            .ToList();

        var connectors = new List<SnapshotConnector>();
        foreach (var edge in document.Edges)
        {
            var from = document.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var to = document.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (from is null || to is null) continue;

            var path = EdgeGeometry.Resolve(
                CanvasHitTester.RectOf(from), CanvasHitTester.RectOf(to), edge.FromSide, edge.ToSide, edge.Route);

            // Each end draws whatever it was given. The migrations have already turned an old board's
            // single Arrow switch into these two, so there is one code path for every era.
            var heads = new List<SnapshotHead>();
            if (Shaped(edge.EffectiveToMarker) is { } toShape) heads.Add(Head(path.EndControl, path.P3, toShape));
            if (Shaped(edge.EffectiveFromMarker) is { } fromShape) heads.Add(Head(path.StartControl, path.P0, fromShape));

            var mid = path.MidPoint;
            connectors.Add(new SnapshotConnector(
                path.ToSvgPath(), heads,
                string.IsNullOrWhiteSpace(edge.Label) ? null : edge.Label, mid.X, mid.Y, edge.ColorKey,
                Dashed: edge.Line == EdgeLine.Dashed,
                Icon: string.IsNullOrWhiteSpace(edge.Icon) ? null : edge.Icon));
        }

        var blocks = document.Nodes
            .OrderBy(n => n.Z).ThenBy(n => n.Id)
            .Select(ToBlock)
            .ToList();

        return new SnapshotScene(bounds.X, bounds.Y, scale, width, height, groups, connectors, blocks);
    }

    /// <summary>An arrowhead pointing from <paramref name="from"/> to <paramref name="tip"/>, 12 world units long.</summary>
    /// <summary>The shape name for a marker, or null when the end draws nothing.</summary>
    private static string? Shaped(EdgeMarker marker) => marker switch
    {
        EdgeMarker.Arrow => "arrow",
        EdgeMarker.Diamond => "diamond",
        EdgeMarker.Dot => "dot",
        _ => null,
    };

    /// <summary>
    /// One end marker, as points the painter can fill without knowing which shape it is.
    /// </summary>
    /// <remarks>
    /// A dot is the exception and carries centre-then-radius, because an arc cannot be written as a
    /// polygon. The shape name travels with it so the painter knows which of the two it has.
    /// </remarks>
    private static SnapshotHead Head(CanvasPoint from, CanvasPoint tip, string shape)
    {
        const double length = 12, halfWidth = 6;
        var angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));

        if (shape == "dot")
        {
            const double r = halfWidth * 0.7;
            return new SnapshotHead(shape, [tip.X - cos * r, tip.Y - sin * r, r]);
        }

        var baseX = tip.X - length * cos;
        var baseY = tip.Y - length * sin;

        if (shape == "diamond")
        {
            // A rhombus centred on the end: along the line one way, across it the other.
            var cx = tip.X - cos * length / 2;
            var cy = tip.Y - sin * length / 2;
            return new SnapshotHead(shape,
            [
                cx + cos * length / 2, cy + sin * length / 2,
                cx - sin * halfWidth,  cy + cos * halfWidth,
                cx - cos * length / 2, cy - sin * length / 2,
                cx + sin * halfWidth,  cy - cos * halfWidth,
            ]);
        }

        return new SnapshotHead(shape,
        [
            tip.X, tip.Y,
            baseX - halfWidth * sin, baseY + halfWidth * cos,
            baseX + halfWidth * sin, baseY - halfWidth * cos,
        ]);
    }

    private static SnapshotBlock ToBlock(CanvasNode node)
    {
        var kind = node.Type.ToString().ToLowerInvariant();
        var (title, lines) = Words(node);
        var (assetId, ext, uploadId) = node.Data switch
        {
            ImageData i => (i.AssetId, i.OpfsExt, i.UploadFileId),
            _ => ((Guid?)null, (string?)null, (Guid?)null),
        };
        return new SnapshotBlock(node.X, node.Y, node.Width, node.Height, kind, node.ColorKey, title, lines, assetId, ext, uploadId, null,
            Filled: node.Fill == NodeFill.Solid);
    }

    private static (string Title, IReadOnlyList<string> Lines) Words(CanvasNode node)
    {
        var display = BlockRegistry.Get(node.Type).DisplayName;
        switch (node.Data)
        {
            case CardData card:
                return (Or(card.Title, display), card.Fields
                    .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                    .Select(f => $"{Label(f.Key)}: {f.Value}")
                    .ToList());
            case TextData text:
                return (display, Paragraphs(text.Text));
            case MessageData message:
                return (Or(message.Author, display), Paragraphs(PasteHtmlAllowList.VisibleText(message.Html)));
            case LinkData link:
                return (Or(link.Title, Or(link.SiteName, display)),
                    new[] { link.Description, link.Url }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList());
            case FileData file:
                return (Or(file.FileName, display), [BcFileSize.Format(file.Size)]);
            case MapData map:
                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(map.Address)) lines.Add(map.Address);
                if (map.Latitude != 0 || map.Longitude != 0)
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"{map.Latitude:F5}, {map.Longitude:F5}"));
                lines.AddRange(map.Pins.Where(p => !string.IsNullOrWhiteSpace(p.Title)).Select(p => "• " + p.Title));
                return (display, lines);
            case TableData table:
                // The header names it where there is one, and each row prints as its cells joined —
                // the published picture is a picture, so a grid drawn as lines of text reads better
                // there than a grid drawn badly.
                var grid = table.Rows.Skip(table.HasHeaderRow ? 1 : 0)
                    .Select(r => string.Join("  ", r.Where(c => !string.IsNullOrWhiteSpace(c))))
                    .Where(line => line.Length > 0)
                    .ToList();
                var heading = table.HasHeaderRow && table.Rows.Count > 0
                    ? string.Join("  ", table.Rows[0].Where(c => !string.IsNullOrWhiteSpace(c)))
                    : "";
                return (Or(heading, display), grid);
            case ShapeData shape:
                return (Or(shape.Text, display), []);
            case ImageData image:
                return (Or(image.Caption, display), []);
            default:
                return (display, []);
        }
    }

    private static string Or(string? text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private static List<string> Paragraphs(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>"witness_name" and "witnessName" both read as "Witness name".</summary>
    private static string Label(string key)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(key.Replace('_', ' ').Replace('-', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim().ToLowerInvariant();
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
