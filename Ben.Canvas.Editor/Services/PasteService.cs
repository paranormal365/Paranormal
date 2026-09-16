using System.Net;
using System.Text;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Paste;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Core.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>What the browser posts for one paste, drop or Paste-button press.</summary>
/// <param name="Source">"paste", "drop" or "button".</param>
/// <param name="BoardX">For a drop, the point on the board element it landed on; otherwise null.</param>
public sealed record PasteInbound(List<PasteItem> Items, string Source, double? BoardX, double? BoardY);

/// <summary>The board's own copy of the selection: JSON for the board, and plain HTML for anywhere else.</summary>
public sealed record ClipboardPayloadOut(string Json, string Html);

/// <summary>
/// Turns what the browser read from the clipboard or a drop into blocks, and supplies the board's own copy.
/// </summary>
/// <remarks>
/// <para>The decisions are Core's (<see cref="PasteClassifier"/> and <see cref="PastePlacer"/>). This service
/// chooses where the blocks land, adds them as one undo step, deletes stored files nothing used, and says
/// what was refused.</para>
///
/// <para>Where things land: a drop lands where it was dropped; a paste after "Paste" in a menu lands at the
/// menu's point; any other paste is centred in view, and repeated pastes step 24 px so they never stack
/// exactly. The board's own copy lands 24 px from the original while the original is in view, which is what
/// Ctrl+C then Ctrl+V means in every drawing tool.</para>
///
/// <para>Message HTML is normalised by Core's allow-list before it is stored, and again whenever a board is
/// read.</para>
/// </remarks>
public sealed class PasteService(
    CanvasStore store,
    SelectionState selection,
    CanvasViewportState viewport,
    CanvasAssetStore assets,
    AnnouncerService announcer,
    IOptions<CanvasEditorOptions> options,
    IJSRuntime js,
    ILogger<PasteService> log,
    BoardAccess? access = null) : IAsyncDisposable
{
    private static readonly TimeSpan CascadeWindow = TimeSpan.FromSeconds(10);

    private IJSObjectReference? _module;
    private DotNetObjectReference<PasteService>? _self;
    private ElementReference _board;
    private CanvasPoint? _menuPoint;
    private DateTime _menuPointAt;
    private int _cascade;
    private DateTime _lastPasteAt;
    private CanvasViewport _lastPasteView;

    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Sentences for whatever part of a paste was not placed.</summary>
    public event Action<IReadOnlyList<string>>? Problems;

    /// <summary>The browser refused to read the clipboard. When nobody handles it, it is said as a problem.</summary>
    public event Action? PermissionDenied;

    /// <summary>A paste put at least one block on the board.</summary>
    public event Action? Placed;

    public async Task AttachAsync(ElementReference board, ElementReference catcher)
    {
        _board = board;
        var o = options.Value;
        _module = await CanvasModules.ImportAsync(js, "js/pasteInterop.js");
        _self ??= DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("attach", board, catcher, _self, new
        {
            maxImageBytes = o.MaxImageBytes,
            maxFileBytes = o.MaxFileBytes,
            maxTextChars = o.MaxTextChars,
            maxHtmlChars = o.MaxHtmlChars,
            maxItems = o.MaxPasteItems,
        });
    }

    /// <summary>
    /// The world point a menu or long-press was opened at. A Paste button pressed within ten seconds lands there;
    /// a keyboard paste never does, because Ctrl+V after a right-click means "paste in view".
    /// </summary>
    public void SetMenuPoint(CanvasPoint? point)
    {
        _menuPoint = point;
        _menuPointAt = UtcNow();
    }

    [JSInvokable]
    public async Task OnPasteEnvelope(PasteInbound inbound)
    {
        try
        {
            await PlaceAsync(inbound);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "A paste could not be placed.");
            Problems?.Invoke([CanvasCopy.Sentences.NothingToPaste]);
        }
    }

    [JSInvokable]
    public Task OnPasteRefused(string reason)
    {
        if (reason == "permission" && PermissionDenied is not null) PermissionDenied.Invoke();
        else Problems?.Invoke([reason == "permission" ? CanvasCopy.Sentences.Permission : CanvasCopy.Sentences.NothingToPaste]);
        return Task.CompletedTask;
    }

    /// <summary>Places a paste and returns how many blocks were added.</summary>
    internal async Task<int> PlaceAsync(PasteInbound inbound)
    {
        if (access is { CanEdit: false })
        {
            // A view-only board (R33) places nothing; any file the browser already stored is swept later.
            Problems?.Invoke([access.Reason ?? CanvasCopy.Sentences.ViewOnly]);
            return 0;
        }

        var o = options.Value;
        var now = UtcNow();
        var drop = inbound.BoardX is { } bx && inbound.BoardY is { } by
            ? ViewportMath.ClientToWorld(viewport.Current, bx, by)
            : (CanvasPoint?)null;

        var menu = inbound.Source is "button" or "photo" && now - _menuPointAt < TimeSpan.FromSeconds(10) ? _menuPoint : null;
        if (menu is not null) _menuPoint = null;

        var sameView = viewport.Current == _lastPasteView;
        _cascade = drop is null && menu is null && sameView && now - _lastPasteAt < CascadeWindow ? _cascade + 1 : 0;
        _lastPasteAt = now;
        _lastPasteView = viewport.Current;

        var anchor = drop ?? menu ?? Offset(viewport.WorldCentre(), _cascade);
        var plan = PasteClassifier.Classify(new PasteEnvelope(inbound.Items ?? [], anchor.X, anchor.Y, inbound.Source ?? "paste"), o);
        plan = WithoutDisabledMaps(plan, o);

        var (nodes, edges) = PastePlacer.Place(plan, 0, 0, now, PasteHtmlAllowList.Normalize);
        var problems = plan.Refusals.Select(r => r.Message).ToList();

        foreach (var (assetId, ext) in plan.UnusedAssets)
            await assets.DeleteAsync(assetId, ext);

        var placed = 0;
        if (nodes.Count > 0)
        {
            var internalCopy = plan.Intents.OfType<PasteIntent.Internal>().Any();
            Position(nodes, anchor, internalCopy && drop is null && menu is null ? SourceBounds(plan) : null);

            if (store.PasteMany(nodes, edges))
            {
                placed = nodes.Count;
                selection.SelectMany(nodes.Select(n => n.Id));
                announcer.Say(CanvasCopy.Sentences.Pasted(placed));
                Placed?.Invoke();
            }
            else
            {
                problems.Add(CanvasCopy.Sentences.BoardFull(o.MaxNodes));
                foreach (var asset in nodes.Select(n => n.Data).OfType<ImageData>().Where(d => d.AssetId is not null))
                    await assets.DeleteAsync(asset.AssetId!.Value, asset.OpfsExt);
                foreach (var asset in nodes.Select(n => n.Data).OfType<FileData>().Where(d => d.AssetId is not null))
                    await assets.DeleteAsync(asset.AssetId!.Value, asset.OpfsExt);
            }
        }

        var distinct = problems.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 0) Problems?.Invoke(distinct);
        return placed;
    }

    private static CanvasPoint Offset(CanvasPoint p, int steps) =>
        new(p.X + CanvasStore.CascadeOffset * steps, p.Y + CanvasStore.CascadeOffset * steps);

    /// <summary>A place pasted while maps are switched off becomes a note, and the person is told why.</summary>
    private static PastePlan WithoutDisabledMaps(PastePlan plan, CanvasEditorOptions o)
    {
        if (o.EnabledBlocks.Contains(CanvasNodeType.Map) || !plan.Intents.OfType<PasteIntent.Map>().Any()) return plan;
        var intents = plan.Intents
            .Select(i => i is PasteIntent.Map m
                ? new PasteIntent.Text(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{m.Latitude}, {m.Longitude}"))
                : i)
            .ToList();
        var refusals = plan.Refusals.Append(new PasteRefusal(PasteRefusalKind.NothingToPaste, CanvasCopy.Sentences.MapsNotEnabled)).ToList();
        return new PastePlan(intents, refusals) { UnusedAssets = plan.UnusedAssets };
    }

    private static WorldRect? SourceBounds(PastePlan plan)
    {
        var source = plan.Intents.OfType<PasteIntent.Internal>().SelectMany(i => i.Payload.Nodes).ToList();
        return source.Count == 0 ? null : WorldRect.Bounds(source.Select(CanvasHitTester.RectOf));
    }

    /// <summary>
    /// Moves the placed set: the board's own copy steps from its original while that is in view; everything
    /// else is centred on the anchor.
    /// </summary>
    private void Position(List<CanvasNode> nodes, CanvasPoint anchor, WorldRect? original)
    {
        var bounds = WorldRect.Bounds(nodes.Select(CanvasHitTester.RectOf));
        double left, top;

        if (original is { IsEmpty: false } source && source.Intersects(viewport.VisibleWorldRect()))
        {
            var step = CanvasStore.CascadeOffset * (_cascade + 1);
            left = source.X + step;
            top = source.Y + step;
        }
        else
        {
            left = anchor.X - bounds.Width / 2;
            top = anchor.Y - bounds.Height / 2;
        }

        var dx = left - bounds.X;
        var dy = top - bounds.Y;
        foreach (var node in nodes)
        {
            node.X += dx;
            node.Y += dy;
        }
    }

    /// <summary>
    /// The board's copy of the selection, asked for synchronously inside the browser's copy event, which is
    /// the only moment Safari lets a page write the clipboard. Null when nothing is selected.
    /// </summary>
    [JSInvokable]
    public ClipboardPayloadOut? GetClipboardPayload()
    {
        var picked = store.Document.Nodes.Where(n => selection.IsSelected(n.Id)).ToList();
        if (picked.Count == 0) return null;

        var ids = picked.Select(n => n.Id).ToHashSet();
        var payload = new CanvasClipboardPayload
        {
            Nodes = picked.Select(n => n.Clone()).ToList(),
            Edges = store.Document.Edges.Where(e => ids.Contains(e.FromNodeId) && ids.Contains(e.ToNodeId)).Select(e => e.Clone()).ToList(),
        };

        var html = new StringBuilder("<div data-ishcanvas>");
        foreach (var node in picked)
            html.Append("<p>").Append(WebUtility.HtmlEncode(NodeWords.Title(node))).Append("</p>");
        html.Append("</div>");

        return new ClipboardPayloadOut(CanvasSerializer.Serialize(payload, compact: true), html.ToString());
    }

    /// <summary>The copy half of a cut has reached the clipboard, so the selection can go.</summary>
    [JSInvokable]
    public void OnCutCopied()
    {
        if (access is { CanEdit: false }) return;
        var ids = selection.NodeIds.ToList();
        if (ids.Count > 0 && store.RemoveNodes(ids)) announcer.Say(Words.Deleted(ids.Count));
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("detach", _board);
                await _module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or JSException) { }
        }

        _self?.Dispose();
    }
}
