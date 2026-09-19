using System.Text.RegularExpressions;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Paste;
using Ben.Canvas.Core.Text;
using Ben.Canvas.Editor.Components;
using Ben.Canvas.Editor.Components.Board;
using Ben.Canvas.Editor.Components.Chrome;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Components;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// View-only boards (R33): a case reader can pan, zoom, select, read and export, but every way of changing the
/// board is refused in C# - buttons, keys, drags, double-taps, paste and cut - and the controls that would change
/// it are not offered.
/// </summary>
public sealed class BoardAccessTests
{
    [Fact]
    public void A_board_is_editable_until_told_otherwise_and_says_why_when_not()
    {
        var access = new BoardAccess();
        var changes = 0;
        access.Changed += () => changes++;

        Assert.True(access.CanEdit);
        access.Set(false);
        access.Set(false);

        Assert.False(access.CanEdit);
        Assert.Equal(CanvasCopy.Sentences.ViewOnly, access.Reason);
        Assert.Equal(1, changes);
    }

    /// <summary>Every action named in the editor's switch either changes the board or is on the allowed list.</summary>
    [Fact]
    public void Every_editing_action_is_refused_and_moving_around_is_not()
    {
        var source = File.ReadAllText(Path.Combine(RepoFiles.EditorRoot(), "Components", "CanvasEditor.razor.Actions.cs"));
        var actions = Regex.Matches(source, "case \"([a-z-]+)\":").Select(m => m.Groups[1].Value).Distinct().ToList();
        string[] allowed = ["zoom-in", "zoom-out", "zoom-reset", "fit", "toggle-props", "help", "add-menu", "more", "sheet-close",
            "resolve-conflict", "conflict-theirs", "conflict-export", "export", "save", "add-card-here", "add-text-here",
            // Presenting is reading: it moves the camera and nothing else, and the meeting where a case
            // is talked through is exactly the room where the person driving may only read it.
            "present", "present-next", "present-previous", "present-stop",
            // Going back to the board a link was followed from is reading too, and for the same
            // reason: a reader walking across published research is exactly who board links are for,
            // so a view-only board must be able to follow one and return. The save it does on the way
            // is refused by the save path's own guard, not by this list.
            "board-back"];

        Assert.All(actions.Except(allowed), a => Assert.True(CanvasEditor.ChangesTheBoard(a), $"{a} changes the board but a view-only board would allow it."));
        Assert.All(["zoom-in", "fit", "export", "help", "more", "add-menu"], a => Assert.False(CanvasEditor.ChangesTheBoard(a), $"{a} must stay allowed."));
        Assert.True(CanvasEditor.ChangesTheBoard("add-card"));
        Assert.True(CanvasEditor.ChangesTheBoard("add-card-here"));
    }

    /// <summary>
    /// The presenting actions are allowed on a view-only board, and the editor names them as a set.
    /// </summary>
    /// <remarks>
    /// The allow-list above would also pass if somebody quietly dropped presenting from the editor
    /// altogether. This says the four are there, are refused by nothing, and are the same four the
    /// editor calls presenting.
    /// </remarks>
    [Fact]
    public void Presenting_is_allowed_on_a_board_somebody_may_only_read()
    {
        string[] presenting = ["present", "present-next", "present-previous", "present-stop"];

        Assert.All(presenting, a => Assert.True(CanvasEditor.IsPresenting(a), $"{a} is no longer one of the presenting actions."));
        Assert.All(presenting, a => Assert.False(CanvasEditor.ChangesTheBoard(a), $"{a} would be refused on a board somebody may only read."));

        var source = File.ReadAllText(Path.Combine(RepoFiles.EditorRoot(), "Components", "CanvasEditor.razor.Actions.cs"));
        Assert.All(presenting, a => Assert.Contains($"case \"{a}\":", source, StringComparison.Ordinal));

        Assert.False(CanvasEditor.IsPresenting("delete"));
    }

    private static (BoardGestureBridge Bridge, Core.Commands.CanvasStore Store, BoardAccess Access, AnnouncerService Announcer) Bridge()
    {
        var store = TestBoards.Store();
        var access = new BoardAccess();
        var announcer = new AnnouncerService();
        var bridge = new BoardGestureBridge(store, new SelectionState(), new CanvasViewportState(), announcer,
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions()), new NoJs(), null, access);
        return (bridge, store, access, announcer);
    }

    [Fact]
    public async Task A_drag_or_resize_on_a_view_only_board_moves_nothing()
    {
        var (bridge, store, access, announcer) = Bridge();
        var node = TestBoards.Node();
        store.AddNode(node);
        access.Set(false);

        var context = await bridge.BeginMove(new MoveBegin([node.Id], []));
        await bridge.OnMoveEnd(new MoveEnd([node.Id], [], 120, 80));
        var resize = await bridge.BeginResize(new ResizeBegin(node.Id, null, "br"));

        Assert.Empty(context.NodeIds);
        Assert.Null(resize);
        Assert.Equal(0, store.FindNode(node.Id)!.X);
        Assert.Equal(CanvasCopy.Sentences.ViewOnly, announcer.Text.Trim('​'));
    }

    [Fact]
    public async Task A_double_tap_or_connector_on_a_view_only_board_adds_nothing()
    {
        var (bridge, store, access, _) = Bridge();
        var a = TestBoards.Node();
        var b = TestBoards.Node();
        store.AddNode(a);
        store.AddNode(b);
        access.Set(false);

        await bridge.OnDoubleTap(new TapInfo(null, null, null, 500, 500, false, false, "mouse", false));
        await bridge.OnConnectEnd(new ConnectEnd(a.Id, "Right", b.Id, "Left", 0, 0));

        Assert.Equal(2, store.Document.Nodes.Count);
        Assert.Empty(store.Document.Edges);
    }

    [Fact]
    public async Task Paste_and_cut_change_nothing_on_a_view_only_board()
    {
        var js = new FakeModuleJs();
        _ = new FakeAssets(js.Module("js/opfsInterop.js"));
        var assets = new CanvasAssetStore(js, NullLogger<CanvasAssetStore>.Instance);
        await assets.StartAsync();
        var store = TestBoards.Store();
        var selection = new SelectionState();
        var access = new BoardAccess();
        var viewport = new CanvasViewportState();
        viewport.SetBoardSize(1000, 800);
        var paste = new PasteService(store, selection, viewport, assets, new AnnouncerService(),
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions()), js, NullLogger<PasteService>.Instance, access);
        var problems = new List<string>();
        paste.Problems += problems.AddRange;
        var node = TestBoards.Node();
        store.AddNode(node);
        selection.Select(node.Id);
        access.Set(false);

        var placed = await paste.PlaceAsync(new PasteInbound([new PasteItem("string", "text/plain", Text: "a note")], "paste", null, null));
        paste.OnCutCopied();

        Assert.Equal(0, placed);
        Assert.Single(store.Document.Nodes);
        Assert.Equal([CanvasCopy.Sentences.ViewOnly], problems);
    }

    [Fact]
    public async Task A_view_only_rail_offers_nothing_to_add_or_paste()
    {
        var html = await RenderHelper.RenderAsync<ToolRail>(new Dictionary<string, object?> { ["ReadOnly"] = true });

        Assert.DoesNotMatch("data-bc-action=\"add-(card|text|message|map|image|link|menu)\"", html);
        Assert.DoesNotContain("data-bc-action=\"paste\"", html);
        Assert.Contains("data-bc-action=\"more\"", html);
    }

    [Fact]
    public async Task A_view_only_empty_board_offers_nothing_to_add()
    {
        var html = await RenderHelper.RenderAsync<BoardEmptyState>(new Dictionary<string, object?> { ["Visible"] = true, ["ReadOnly"] = true });

        Assert.Contains(CanvasCopy.Titles.EmptyBoard, html);
        Assert.DoesNotContain("data-bc-action", html);
    }

    [Fact]
    public async Task A_view_only_properties_panel_disables_every_control()
    {
        var html = await RenderHelper.RenderAsync<PropertiesPanel>(new Dictionary<string, object?> { ["ReadOnly"] = true });

        Assert.Matches("<fieldset class=\"bc-props__fields\" disabled", html);
    }
}
