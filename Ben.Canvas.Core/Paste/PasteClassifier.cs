using System.Net;
using System.Text.RegularExpressions;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Core.Paste;

/// <summary>
/// Decides what a paste or drop becomes.
/// </summary>
/// <remarks>
/// <para>The priority order is the whole design, and each rule exists because the one below it gets a real
/// case wrong:</para>
/// <list type="number">
/// <item>More than the item limit: keep the first ones and say so.</item>
/// <item>The board's own copy wins over everything, because the browser also puts plain text beside it.</item>
/// <item>Files next. A pasted screenshot arrives with placeholder HTML beside it, so once any file is present
/// the text flavours are ignored.</item>
/// <item>Plain text that is exactly one address becomes a link card, not a note.</item>
/// <item>Plain text that is a place becomes a map box.</item>
/// <item>HTML with real content becomes a message; HTML that is only a link or a lone picture becomes a link.</item>
/// <item>Anything else that is text becomes a note, cut to the text limit.</item>
/// </list>
/// <para>Pure: the browser has already read the clipboard and stored the files.</para>
/// </remarks>
public static partial class PasteClassifier
{
    public static PastePlan Classify(PasteEnvelope envelope, CanvasEditorOptions options)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(options);

        var intents = new List<PasteIntent>();
        var refusals = new List<PasteRefusal>();
        var unused = new List<(Guid, string?)>();

        var all = (envelope.Items ?? []).Where(i => i is not null).ToList();
        var items = all;
        if (all.Count > options.MaxPasteItems)
        {
            items = all.Take(options.MaxPasteItems).ToList();
            refusals.Add(new(PasteRefusalKind.TooManyItems, CanvasCopy.Sentences.TooManyItems(options.MaxPasteItems)));
            unused.AddRange(all.Skip(options.MaxPasteItems).Where(i => i.AssetId is not null).Select(i => (i.AssetId!.Value, i.Ext)));
        }

        var strings = items.Where(i => string.Equals(i.Kind, "string", StringComparison.OrdinalIgnoreCase)).ToList();
        var files = items.Where(i => string.Equals(i.Kind, "file", StringComparison.OrdinalIgnoreCase)).ToList();

        string? Flavour(string mime) => strings.FirstOrDefault(s => string.Equals(s.MimeType, mime, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.Text))?.Text;

        var plain = Flavour("text/plain");
        var html = Flavour("text/html");
        var uriList = Flavour("text/uri-list");

        // 1. Our own copy.
        if (CanvasClipboardPayload.TryReadPayload(plain, out var payload) && payload is not null)
        {
            intents.Add(new PasteIntent.Internal(payload));
            unused.AddRange(files.Where(f => f.AssetId is not null).Select(f => (f.AssetId!.Value, f.Ext)));
            return new PastePlan(intents, refusals) { UnusedAssets = unused };
        }

        // 2. Files.
        if (files.Count > 0)
        {
            foreach (var file in files) ClassifyFile(file, options, intents, refusals, unused);
            return new PastePlan(intents, refusals) { UnusedAssets = unused };
        }

        // 3. A lone address.
        var url = UrlDetector.IsSingleUrl(plain) ?? UrlDetector.IsSingleUrl(FirstUri(uriList));
        if (url is not null)
        {
            intents.Add(new PasteIntent.Link(url));
            return new PastePlan(intents, refusals);
        }

        // 4. A place.
        if (CoordinateDetector.TryParse(plain, out var lat, out var lng))
        {
            intents.Add(new PasteIntent.Map(lat, lng));
            return new PastePlan(intents, refusals);
        }

        // 5. Formatted text.
        if (html is not null)
        {
            var visible = PasteHtmlAllowList.VisibleText(html);
            var linkish = UrlDetector.IsSingleUrl(visible);
            if (linkish is null && visible.Length == 0) linkish = LoneLinkOrPicture(html);

            if (linkish is not null)
            {
                intents.Add(new PasteIntent.Link(linkish));
                return new PastePlan(intents, refusals);
            }

            if (visible.Length > 0 && html.Length <= options.MaxHtmlChars)
            {
                intents.Add(new PasteIntent.Html(html, visible));
                return new PastePlan(intents, refusals);
            }

            plain ??= visible.Length > 0 ? visible : null;
        }

        // 6. Plain text.
        if (!string.IsNullOrWhiteSpace(plain))
        {
            var text = plain;
            if (text.Length > options.MaxTextChars)
            {
                text = text[..options.MaxTextChars];
                refusals.Add(new(PasteRefusalKind.TextTruncated, CanvasCopy.Sentences.TextTruncated(options.MaxTextChars)));
            }

            intents.Add(new PasteIntent.Text(text));
            return new PastePlan(intents, refusals);
        }

        // 7. Nothing usable.
        if (refusals.Count == 0) refusals.Add(new(PasteRefusalKind.NothingToPaste, CanvasCopy.Sentences.NothingToPaste));
        return new PastePlan(intents, refusals);
    }

    private static void ClassifyFile(PasteItem file, CanvasEditorOptions options, List<PasteIntent> intents, List<PasteRefusal> refusals, List<(Guid, string?)> unused)
    {
        var head = file.Head ?? [];
        var name = file.FileName ?? "";
        var mime = file.MimeType ?? "";

        void Unused()
        {
            if (file.AssetId is { } id) unused.Add((id, file.Ext));
        }

        var heic = ImageSignature.LooksLikeHeic(head)
            || name.EndsWith(".heic", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".heif", StringComparison.OrdinalIgnoreCase)
            || mime.StartsWith("image/heic", StringComparison.OrdinalIgnoreCase)
            || mime.StartsWith("image/heif", StringComparison.OrdinalIgnoreCase);

        if (heic)
        {
            refusals.Add(new(PasteRefusalKind.Heic, CanvasCopy.Sentences.Heic));
            Unused();
            return;
        }

        var imageExt = ImageSignature.ExtensionFor(head);
        if (imageExt is not null)
        {
            if (file.Size > options.MaxImageBytes)
            {
                refusals.Add(new(PasteRefusalKind.TooLargeImage, CanvasCopy.Sentences.TooLargeImage((int)Math.Ceiling(file.Size / 1024d / 1024d))));
                Unused();
                return;
            }

            if (file.AssetId is not { } imageId)
            {
                refusals.Add(new(PasteRefusalKind.NotStored, CanvasCopy.Sentences.AssetNotStored(DisplayName(name))));
                return;
            }

            intents.Add(new PasteIntent.Image(imageId, imageExt, file.Width, file.Height));
            return;
        }

        if (file.Size > options.MaxFileBytes)
        {
            refusals.Add(new(PasteRefusalKind.TooLargeFile, CanvasCopy.Sentences.TooLargeFile));
            Unused();
            return;
        }

        if (file.AssetId is not { } fileId)
        {
            refusals.Add(new(PasteRefusalKind.NotStored, CanvasCopy.Sentences.AssetNotStored(DisplayName(name))));
            return;
        }

        intents.Add(new PasteIntent.File(fileId, file.Ext, string.IsNullOrWhiteSpace(name) ? "file" + (file.Ext ?? "") : name, file.Size, mime));
    }

    private static string DisplayName(string name) => string.IsNullOrWhiteSpace(name) ? "That file" : name;

    private static string? FirstUri(string? uriList) =>
        uriList?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));

    /// <summary>An anchor or picture with no visible text: the paste is really an address.</summary>
    private static string? LoneLinkOrPicture(string html)
    {
        var href = AnchorHref().Match(html);
        if (href.Success && SafeHttpUrl.Normalize(WebUtility.HtmlDecode(href.Groups["v"].Value)) is { } link) return link;

        var src = ImageSrc().Match(html);
        if (src.Success && SafeHttpUrl.IsHttps(WebUtility.HtmlDecode(src.Groups["v"].Value))) return SafeHttpUrl.Normalize(WebUtility.HtmlDecode(src.Groups["v"].Value));

        return null;
    }

    [GeneratedRegex("""<a\s[^>]*?href\s*=\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorHref();

    [GeneratedRegex("""<img\s[^>]*?src\s*=\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageSrc();
}

/// <summary>Turns a paste plan into blocks and connectors at the paste point.</summary>
public static class PastePlacer
{
    /// <summary>
    /// Blocks for each intent. The store assigns paint order when it adds them.
    /// </summary>
    /// <param name="htmlToAllowed">
    /// The sanitiser for message HTML. Raw pasted markup is never stored; the browser-side sanitiser runs in
    /// the editor, and Core's normaliser runs again whenever the board is read.
    /// </param>
    public static (List<CanvasNode> Nodes, List<CanvasEdge> Edges) Place(PastePlan plan, double worldX, double worldY, DateTime nowUtc, Func<string, string> htmlToAllowed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(htmlToAllowed);

        var nodes = new List<CanvasNode>();
        var edges = new List<CanvasEdge>();
        var cascade = 0;

        foreach (var intent in plan.Intents)
        {
            if (intent is PasteIntent.Internal internalCopy)
            {
                PlaceInternal(internalCopy.Payload, worldX, worldY, nodes, edges);
                continue;
            }

            var x = worldX + Commands.CanvasStore.CascadeOffset * cascade;
            var y = worldY + Commands.CanvasStore.CascadeOffset * cascade;
            cascade++;

            nodes.Add(intent switch
            {
                PasteIntent.Image image => ImageNode(image, x, y),
                PasteIntent.File file => Node(CanvasNodeType.File, x, y, new FileData
                {
                    AssetId = file.AssetId, OpfsExt = file.Ext, FileName = file.FileName, Size = file.Size, ContentType = file.ContentType,
                }),
                PasteIntent.Link link => Node(CanvasNodeType.Link, x, y, new LinkData { Url = link.Url, Tier = LinkPreviewTier.None }),
                PasteIntent.Map map => Node(CanvasNodeType.Map, x, y, new MapData
                {
                    Latitude = map.Latitude,
                    Longitude = map.Longitude,
                    Pins = [new MapPin { Latitude = map.Latitude, Longitude = map.Longitude }],
                }),
                PasteIntent.Html markup => Node(CanvasNodeType.Message, x, y, new MessageData { Html = htmlToAllowed(markup.Markup), TimestampUtc = nowUtc }),
                PasteIntent.Text text => Node(CanvasNodeType.Text, x, y, new TextData { Text = text.Value }),
                _ => throw new ArgumentOutOfRangeException(nameof(plan), intent.GetType().Name, "Unknown paste intent."),
            });
        }

        return (nodes, edges);
    }

    private static CanvasNode Node(CanvasNodeType type, double x, double y, NodeData data)
    {
        var descriptor = BlockRegistry.Get(type);
        return new CanvasNode { Type = type, X = x, Y = y, Width = descriptor.DefaultWidth, Height = descriptor.DefaultHeight, Data = data };
    }

    private static CanvasNode ImageNode(PasteIntent.Image image, double x, double y)
    {
        var descriptor = BlockRegistry.Get(CanvasNodeType.Image);
        var node = Node(CanvasNodeType.Image, x, y, new ImageData
        {
            AssetId = image.AssetId,
            OpfsExt = image.Ext,
            NaturalWidth = image.Width ?? 0,
            NaturalHeight = image.Height ?? 0,
        });

        if (image.Width is > 0 and var w && image.Height is > 0 and var h)
        {
            var scale = Math.Min(1, Math.Min(descriptor.DefaultWidth / w, descriptor.DefaultHeight / h));
            double width = w * scale, height = h * scale;
            if (width < descriptor.MinWidth || height < descriptor.MinHeight)
            {
                var up = Math.Max(descriptor.MinWidth / width, descriptor.MinHeight / height);
                width *= up;
                height *= up;
            }

            node.Width = Math.Max(descriptor.MinWidth, width);
            node.Height = Math.Max(descriptor.MinHeight, height);
        }

        return node;
    }

    private static void PlaceInternal(CanvasClipboardPayload payload, double worldX, double worldY, List<CanvasNode> nodes, List<CanvasEdge> edges)
    {
        if (payload.Nodes.Count == 0) return;

        var left = payload.Nodes.Min(n => n.X);
        var top = payload.Nodes.Min(n => n.Y);
        var map = new Dictionary<Guid, Guid>();

        foreach (var source in payload.Nodes)
        {
            var copy = source.Clone();
            copy.Id = Guid.NewGuid();
            copy.X = worldX + (source.X - left);
            copy.Y = worldY + (source.Y - top);
            copy.GroupId = null;
            copy.Z = 0;
            map[source.Id] = copy.Id;
            nodes.Add(copy);
        }

        foreach (var edge in payload.Edges)
        {
            if (!map.TryGetValue(edge.FromNodeId, out var from) || !map.TryGetValue(edge.ToNodeId, out var to)) continue;
            var copy = edge.Clone();
            copy.Id = Guid.NewGuid();
            copy.FromNodeId = from;
            copy.ToNodeId = to;
            edges.Add(copy);
        }
    }
}
