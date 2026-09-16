using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ben.Canvas.Tests.Services;

/// <summary>Selection never mixes blocks and connectors, and never points at something undo removed.</summary>
public sealed class SelectionStateTests
{
    [Fact]
    public void Selecting_without_shift_replaces_the_selection()
    {
        var s = new SelectionState();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        s.Select(a);
        s.Select(b);
        Assert.Equal([b], s.NodeIds);
    }

    [Fact]
    public void Shift_adds_and_a_second_toggle_removes()
    {
        var s = new SelectionState();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        s.Select(a);
        s.Toggle(b);
        Assert.Equal(2, s.NodeIds.Count);
        s.Toggle(b);
        Assert.Equal([a], s.NodeIds);
    }

    [Fact]
    public void Selecting_an_edge_clears_the_nodes()
    {
        var s = new SelectionState();
        s.Select(Guid.NewGuid());
        var edge = Guid.NewGuid();
        s.SelectEdge(edge);
        Assert.Empty(s.NodeIds);
        Assert.Equal(edge, s.EdgeId);
        s.Select(Guid.NewGuid());
        Assert.Null(s.EdgeId);
    }

    [Fact]
    public void Prune_drops_ids_that_undo_removed()
    {
        var node = TestBoards.Node();
        var store = TestBoards.Store();
        store.AddNode(node);
        var s = new SelectionState();
        s.Select(node.Id);
        store.Undo();
        s.Prune(store.Document);
        Assert.Empty(s.NodeIds);
        Assert.Null(s.FocusedNodeId);
    }

    [Fact]
    public void OnChanged_is_not_raised_when_nothing_changed()
    {
        var s = new SelectionState();
        var id = Guid.NewGuid();
        s.Select(id);
        var raised = 0;
        s.OnChanged += () => raised++;
        s.Select(id);
        s.Clear();
        s.Clear();
        Assert.Equal(1, raised);
    }
}

/// <summary>The camera C# keeps matches the maths the board uses.</summary>
public sealed class CanvasViewportStateTests
{
    [Fact]
    public void Zoom_about_the_centre_keeps_the_centre_world_point()
    {
        var v = new CanvasViewportState();
        v.SetBoardSize(1000, 600);
        v.Set(new CanvasViewport(-120, 40, 0.8));
        var before = v.WorldCentre();
        v.ZoomAboutCentre(1.25);
        var after = v.WorldCentre();
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void Set_clamps_zoom_to_the_range()
    {
        var v = new CanvasViewportState();
        v.Set(new CanvasViewport(0, 0, 99));
        Assert.Equal(4, v.Current.Zoom);
    }

    [Fact]
    public void Fit_of_nodes_spread_3000px_puts_every_rect_inside_the_board()
    {
        var v = new CanvasViewportState();
        v.SetBoardSize(1280, 800);
        var nodes = new[] { TestBoards.Node(x: -1500, y: -200), TestBoards.Node(x: 1500, y: 900) };
        v.Fit(WorldRect.Bounds(nodes.Select(CanvasHitTester.RectOf)));
        foreach (var n in nodes)
        {
            var tl = ViewportMath.WorldToClient(v.Current, n.X, n.Y);
            var br = ViewportMath.WorldToClient(v.Current, n.X + n.Width, n.Y + n.Height);
            Assert.True(tl.X >= 0 && tl.Y >= 0 && br.X <= 1280 && br.Y <= 800);
        }
    }

    [Fact]
    public void Reset_zoom_keeps_the_centre()
    {
        var v = new CanvasViewportState();
        v.SetBoardSize(800, 600);
        v.Set(new CanvasViewport(33, -70, 2.5));
        var before = v.WorldCentre();
        v.ResetZoom();
        Assert.Equal(1, v.Current.Zoom);
        Assert.Equal(before.X, v.WorldCentre().X, 6);
    }
}

/// <summary>A screen reader hears a repeated announcement, not silence.</summary>
public sealed class AnnouncerServiceTests
{
    [Fact]
    public void Saying_the_same_words_twice_still_changes_the_text()
    {
        var a = new AnnouncerService();
        a.Say("Moved to 1, 2.");
        var first = a.Text;
        a.Say("Moved to 1, 2.");
        Assert.NotEqual(first, a.Text);
        a.Say("Moved to 1, 2.");
        Assert.Equal(first, a.Text);
    }
}

/// <summary>
/// The C# half of the gesture contract: one undo entry per gesture, C# snapping, and a flag that can never
/// stay stuck.
/// </summary>
public sealed class BoardGestureBridgeTests
{
    private static (BoardGestureBridge Bridge, CanvasStore Store, SelectionState Selection, CanvasViewportState Viewport, AnnouncerService Announcer) Build(
        CanvasStore store, CanvasEditorOptions? options = null)
    {
        var selection = new SelectionState();
        var viewport = new CanvasViewportState();
        var announcer = new AnnouncerService();
        var bridge = new BoardGestureBridge(store, selection, viewport, announcer, Microsoft.Extensions.Options.Options.Create(options ?? new CanvasEditorOptions()), new NoJs());
        return (bridge, store, selection, viewport, announcer);
    }

    [Fact]
    public async Task A_move_end_becomes_one_undo_entry()
    {
        var node = TestBoards.Node();
        var (bridge, store, _, _, _) = Build(TestBoards.StoreWith(node));
        await bridge.BeginMove(new MoveBegin([node.Id], []));
        await bridge.OnMoveEnd(new MoveEnd([node.Id], [], 120, 80));
        Assert.Equal((120d, 80d), (node.X, node.Y));
        Assert.True(store.Undo());
        Assert.False(store.CanUndo);
    }

    [Fact]
    public async Task A_move_end_is_resnapped_by_csharp_not_trusted_from_js()
    {
        var dragged = TestBoards.Node(x: 0, y: 0, width: 200, height: 100);
        var other = TestBoards.Node(x: 600, y: 500, width: 200, height: 100);
        var (bridge, _, _, _, _) = Build(TestBoards.StoreWith(dragged, other));
        var context = await bridge.BeginMove(new MoveBegin([dragged.Id], []));
        Assert.Contains(600, context.GuidesX);
        await bridge.OnMoveEnd(new MoveEnd([dragged.Id], [], 603, 0));
        Assert.Equal(600, dragged.X);
    }

    [Fact]
    public async Task The_move_context_names_the_connectors_to_redraw()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 500);
        var store = TestBoards.StoreWith(a, b);
        var edge = store.Connect(a.Id, b.Id, CanvasSide.Right)!;
        var (bridge, _, _, _, _) = Build(store);
        var context = await bridge.BeginMove(new MoveBegin([a.Id], []));
        var info = Assert.Single(context.Edges);
        Assert.Equal((edge.Id, "Right", (string?)null), (info.Id, info.FromSide, info.ToSide));
    }

    [Fact]
    public async Task Gesture_active_is_true_between_begin_and_end_and_false_after_cancel()
    {
        var node = TestBoards.Node();
        var (bridge, _, _, viewport, _) = Build(TestBoards.StoreWith(node));
        await bridge.BeginMove(new MoveBegin([node.Id], []));
        Assert.True(viewport.GestureActive);
        await bridge.OnGestureCancelled();
        Assert.False(viewport.GestureActive);
        Assert.Equal(0, node.X);
    }

    [Fact]
    public async Task A_resize_cannot_go_below_the_block_minimum()
    {
        var node = TestBoards.Node(CanvasNodeType.Card, width: 300, height: 200);
        var (bridge, _, _, _, _) = Build(TestBoards.StoreWith(node));
        var context = await bridge.BeginResize(new ResizeBegin(node.Id, null, "br"));
        Assert.Equal((220d, 140d), (context!.MinWidth, context.MinHeight));
        await bridge.OnResizeEnd(new ResizeEnd(node.Id, null, "br", -900, -900, false));
        Assert.Equal((220d, 140d), (node.Width, node.Height));
    }

    [Fact]
    public async Task A_tap_on_empty_board_clears_selection()
    {
        var node = TestBoards.Node();
        var (bridge, _, selection, _, _) = Build(TestBoards.StoreWith(node));
        selection.Select(node.Id);
        await bridge.OnTap(new TapInfo(null, null, null, 5000, 5000, false, false, "mouse", false));
        Assert.Empty(selection.NodeIds);
    }

    [Fact]
    public async Task A_tap_near_an_edge_selects_it_when_no_node_is_hit()
    {
        var a = TestBoards.Node(x: 0, y: 0, width: 100, height: 100);
        var b = TestBoards.Node(x: 600, y: 0, width: 100, height: 100);
        var store = TestBoards.StoreWith(a, b);
        var edge = store.Connect(a.Id, b.Id)!;
        var (bridge, _, selection, _, _) = Build(store);
        await bridge.OnTap(new TapInfo(null, null, null, 350, 54, false, false, "mouse", false));
        Assert.Equal(edge.Id, selection.EdgeId);
    }

    [Fact]
    public async Task Shift_tap_toggles()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var (bridge, _, selection, _, _) = Build(TestBoards.StoreWith(a, b));
        await bridge.OnTap(new TapInfo(a.Id, null, null, 0, 0, false, false, "mouse", false));
        await bridge.OnTap(new TapInfo(b.Id, null, null, 0, 0, true, false, "mouse", false));
        Assert.Equal(2, selection.NodeIds.Count);
    }

    [Fact]
    public async Task Connect_end_on_the_same_node_adds_nothing()
    {
        var a = TestBoards.Node();
        var (bridge, store, _, _, _) = Build(TestBoards.StoreWith(a));
        await bridge.OnConnectEnd(new ConnectEnd(a.Id, "Right", a.Id, null, 0, 0));
        Assert.Empty(store.Document.Edges);
    }

    [Fact]
    public async Task Connect_end_keeps_the_start_side_and_leaves_the_far_side_automatic()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 500);
        var (bridge, store, selection, _, announcer) = Build(TestBoards.StoreWith(a, b));
        await bridge.OnConnectEnd(new ConnectEnd(a.Id, "Bottom", b.Id, null, 0, 0));
        var edge = Assert.Single(store.Document.Edges);
        Assert.Equal((CanvasSide.Bottom, (CanvasSide?)null), (edge.FromSide!.Value, edge.ToSide));
        Assert.Equal(edge.Id, selection.EdgeId);
        Assert.StartsWith("Connected", announcer.Text);
    }

    [Fact]
    public async Task Marquee_is_additive_with_shift()
    {
        var a = TestBoards.Node(x: 0);
        var b = TestBoards.Node(x: 1000);
        var (bridge, _, selection, _, _) = Build(TestBoards.StoreWith(a, b));
        selection.Select(b.Id);
        await bridge.OnMarquee(new MarqueeEnd(-10, -10, 50, 50, true));
        Assert.Equal(2, selection.NodeIds.Count);
    }

    [Fact]
    public async Task Double_tap_on_empty_adds_one_text_node_and_asks_to_edit_it()
    {
        var (bridge, store, _, _, _) = Build(TestBoards.Store());
        Guid? edited = null;
        bridge.EditRequested += id => edited = id;
        await bridge.OnDoubleTap(new TapInfo(null, null, null, 300, 300, false, false, "mouse", false));
        var node = Assert.Single(store.Document.Nodes);
        Assert.Equal(CanvasNodeType.Text, node.Type);
        Assert.Equal(node.Id, edited);
    }

    [Fact]
    public async Task A_throwing_callback_still_clears_gesture_active()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        var (bridge, _, _, viewport, _) = Build(store);
        await bridge.BeginMove(new MoveBegin([node.Id], []));
        store.OnChange += _ => throw new InvalidOperationException("listener failed");
        await bridge.OnMoveEnd(new MoveEnd([node.Id], [], 10, 10));
        Assert.False(viewport.GestureActive);
    }

    [Fact]
    public async Task A_tap_on_a_locked_block_while_dragging_says_it_is_locked()
    {
        var node = TestBoards.Node();
        node.Locked = true;
        var (bridge, _, _, _, announcer) = Build(TestBoards.StoreWith(node));
        await bridge.OnTap(new TapInfo(node.Id, null, null, 0, 0, false, false, "mouse", true));
        Assert.Contains("locked", announcer.Text);
    }
}

/// <summary>The board services come from the one registration call a host makes.</summary>
public sealed class BoardServiceRegistrationTests
{
    [Fact]
    public async Task Every_board_service_resolves_from_AddBenCanvasEditor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime, NoJs>();
        services.AddBenCanvasEditor(o => o.HistoryDepth = 7);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Equal(7, scope.ServiceProvider.GetRequiredService<CanvasStore>().HistoryDepth);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SelectionState>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CanvasViewportState>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AnnouncerService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<BoardGestureBridge>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<KeyboardShortcutService>());
    }
}
