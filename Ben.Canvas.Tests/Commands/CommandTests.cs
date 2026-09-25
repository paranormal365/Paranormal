using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Commands;

/// <summary>A composite undoes its parts in reverse, so later parts that depend on earlier ones unwind cleanly.</summary>
public sealed class CompositeCommandTests
{
    private sealed class Recording(string name, List<string> log) : IEditorCommand
    {
        public string Description => name;
        public void Execute() => log.Add("do " + name);
        public void Undo() => log.Add("undo " + name);
    }

    [Fact]
    public void Undo_runs_in_reverse_order()
    {
        var log = new List<string>();
        var composite = new CompositeCommand("three", [new Recording("a", log), new Recording("b", log), new Recording("c", log)]);
        composite.Execute();
        composite.Undo();
        Assert.Equal(["do a", "do b", "do c", "undo c", "undo b", "undo a"], log);
    }
}

/// <summary>Undo and redo put every kind of change back exactly, one step per action.</summary>
public sealed class CanvasStoreUndoRedoTests
{
    private sealed class Counter : IEditorCommand
    {
        public int Value;
        public string Description => "Count";
        public void Execute() => Value++;
        public void Undo() => Value--;
    }

    // ── Mechanics ───────────────────────────────────────────────────────

    [Fact]
    public void History_is_capped_at_fifty()
    {
        var store = TestBoards.Store();
        var counter = new Counter();
        for (var i = 0; i < 60; i++) store.Execute(counter, CanvasChangeKind.Document);
        var undone = 0;
        while (store.Undo()) undone++;
        Assert.Equal(50, undone);
    }

    [Fact]
    public void History_is_capped_at_the_configured_depth()
    {
        var store = TestBoards.Store(historyDepth: 3);
        var counter = new Counter();
        for (var i = 0; i < 10; i++) store.Execute(counter, CanvasChangeKind.Document);
        var undone = 0;
        while (store.Undo()) undone++;
        Assert.Equal(3, undone);
    }

    [Fact]
    public void A_new_command_clears_redo()
    {
        var store = TestBoards.Store();
        var counter = new Counter();
        store.Execute(counter, CanvasChangeKind.Document);
        store.Undo();
        Assert.True(store.CanRedo);
        store.Execute(counter, CanvasChangeKind.Document);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void Loading_a_document_clears_history_and_is_not_an_edit()
    {
        var store = TestBoards.Store();
        store.AddNode(TestBoards.Node());
        var counter = store.ChangeCounter;
        var generation = store.LoadGeneration;
        var kinds = new List<CanvasChangeKind>();
        store.OnChange += c => kinds.Add(c.Kind);

        store.Load(new CanvasDocument());

        Assert.False(store.CanUndo);
        Assert.Equal(counter, store.ChangeCounter);
        Assert.Equal(generation + 1, store.LoadGeneration);
        Assert.Equal([CanvasChangeKind.Reset], kinds);
    }

    [Fact]
    public void Undo_description_names_the_action()
    {
        var store = TestBoards.Store();
        store.AddNode(TestBoards.Node(CanvasNodeType.Map));
        Assert.Equal("Add map", store.UndoDescription);
        store.Undo();
        Assert.Equal("Add map", store.RedoDescription);
    }

    [Fact]
    public void Undo_bumps_the_version_of_every_touched_node()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var store = TestBoards.StoreWith(a, b);
        store.MoveNodes([a.Id, b.Id], 10, 0);
        var va = store.VersionOf(a.Id);
        var vb = store.VersionOf(b.Id);
        store.Undo();
        Assert.True(store.VersionOf(a.Id) > va);
        Assert.True(store.VersionOf(b.Id) > vb);
    }

    [Fact]
    public void A_freshly_loaded_board_answers_version_zero_rather_than_throwing()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        Assert.Equal(0, store.VersionOf(node.Id));
    }

    [Fact]
    public void Paint_order_is_by_z_then_id()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node();
        var c = TestBoards.Node();
        var store = TestBoards.StoreWith(a, b, c);
        store.BringToFront([a.Id]);
        Assert.Equal([b.Id, c.Id, a.Id], store.NodesInPaintOrder().Select(n => n.Id));
    }

    // ── Blocks ──────────────────────────────────────────────────────────

    [Fact]
    public void Adding_a_node_assigns_the_next_z()
    {
        var store = TestBoards.StoreWith(TestBoards.Node(), TestBoards.Node());
        var node = TestBoards.Node();
        Assert.True(store.AddNode(node));
        Assert.Equal(2, node.Z);
    }

    [Fact]
    public void Adding_past_the_node_limit_is_refused()
    {
        var store = TestBoards.Store(maxNodes: 2);
        Assert.True(store.AddNode(TestBoards.Node()));
        Assert.True(store.AddNode(TestBoards.Node()));
        Assert.False(store.AddNode(TestBoards.Node()));
        Assert.Equal(2, store.Document.Nodes.Count);
    }

    [Fact]
    public void Undoing_a_move_after_a_resize_keeps_the_new_size()
    {
        var node = TestBoards.Node(x: 10, y: 10, width: 300, height: 200);
        var store = TestBoards.StoreWith(node);
        store.ResizeNode(node.Id, 10, 10, 500, 400);
        store.MoveNodes([node.Id], 100, 50);
        store.Undo();
        Assert.Equal((10d, 10d, 500d, 400d), (node.X, node.Y, node.Width, node.Height));
    }

    [Fact]
    public void A_resize_below_the_block_minimum_is_raised_to_it()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        var store = TestBoards.StoreWith(node);
        store.ResizeNode(node.Id, 0, 0, 5, 5);
        Assert.Equal(220, node.Width);
        Assert.Equal(140, node.Height);
    }

    [Fact]
    public void A_locked_node_does_not_move_or_resize()
    {
        var node = TestBoards.Node();
        node.Locked = true;
        var store = TestBoards.StoreWith(node);
        Assert.False(store.MoveNodes([node.Id], 10, 10));
        Assert.False(store.ResizeNode(node.Id, 0, 0, 900, 900));
        Assert.Equal((0d, 0d), (node.X, node.Y));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Bring_to_front_gives_a_z_above_every_other_node()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node();
        var c = TestBoards.Node();
        var store = TestBoards.StoreWith(a, b, c);
        store.BringToFront([a.Id]);
        Assert.True(a.Z > b.Z && a.Z > c.Z);
        store.Undo();
        Assert.Equal(0, a.Z);
    }

    [Fact]
    public void Send_to_back_gives_a_z_below_every_other_node()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node();
        var c = TestBoards.Node();
        var store = TestBoards.StoreWith(a, b, c);
        store.SendToBack([b.Id, c.Id]);
        Assert.True(b.Z < a.Z && c.Z < a.Z);
        Assert.True(b.Z < c.Z, "Relative order of the moved blocks is kept.");
    }

    [Fact]
    public void Editing_card_fields_is_undoable_and_the_undo_copy_is_not_shared()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        var store = TestBoards.StoreWith(node);
        store.UpdateNodeData(node.Id, d => ((CardData)d).Fields["description"] = "cold spot");
        Assert.Equal("cold spot", ((CardData)node.Data).Fields["description"]);

        ((CardData)node.Data).Fields["description"] = "tampered after the command";
        store.Undo();
        Assert.False(((CardData)node.Data).Fields.ContainsKey("description"));
        store.Redo();
        Assert.Equal("cold spot", ((CardData)node.Data).Fields["description"]);
    }

    [Fact]
    public void An_edit_that_changes_nothing_pushes_nothing()
    {
        var node = TestBoards.Node(CanvasNodeType.Text);
        var store = TestBoards.StoreWith(node);
        Assert.False(store.UpdateNodeData(node.Id, _ => { }));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Removing_two_nodes_is_one_undo_step()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node();
        var store = TestBoards.StoreWith(a, b);
        store.RemoveNodes([a.Id, b.Id]);
        Assert.Empty(store.Document.Nodes);
        Assert.Equal("Remove 2 items", store.UndoDescription);
        store.Undo();
        Assert.Equal([a.Id, b.Id], store.Document.Nodes.Select(n => n.Id));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Redo_reapplies_a_removed_node()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        store.RemoveNodes([a.Id]);
        store.Undo();
        store.Redo();
        Assert.Empty(store.Document.Nodes);
    }

    [Fact]
    public void Colour_and_lock_are_undoable()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        store.SetColor([node.Id], "3");
        store.SetLocked([node.Id], true);
        Assert.Equal(("3", true), (node.ColorKey, node.Locked));
        store.Undo();
        Assert.False(node.Locked);
        store.Undo();
        Assert.Null(node.ColorKey);
    }

    [Fact]
    public void A_colour_that_is_not_a_palette_key_is_refused()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        Assert.False(store.SetColor([node.Id], "#ff0000"));
    }

    // ── Connectors, paste and duplicate ─────────────────────────────────

    [Fact]
    public void Removing_a_node_takes_its_edges_and_undo_brings_both_back()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var c = TestBoards.Node(x: 800);
        var store = TestBoards.StoreWith(a, b, c);
        var ab = store.Connect(a.Id, b.Id)!;
        var bc = store.Connect(b.Id, c.Id)!;

        store.RemoveNodes([b.Id]);
        Assert.Empty(store.Document.Edges);

        store.Undo();
        Assert.Equal([ab.Id, bc.Id], store.Document.Edges.Select(e => e.Id));
        Assert.Equal([a.Id, b.Id, c.Id], store.Document.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Redo_reapplies_a_removed_node_with_its_edges()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var store = TestBoards.StoreWith(a, b);
        store.Connect(a.Id, b.Id);
        store.RemoveNodes([a.Id]);
        store.Undo();
        store.Redo();
        Assert.Single(store.Document.Nodes);
        Assert.Empty(store.Document.Edges);
    }

    [Fact]
    public void Connect_refuses_a_self_loop()
    {
        var a = TestBoards.Node();
        Assert.Null(TestBoards.StoreWith(a).Connect(a.Id, a.Id));
    }

    [Fact]
    public void Connect_refuses_a_duplicate()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var store = TestBoards.StoreWith(a, b);
        Assert.NotNull(store.Connect(a.Id, b.Id));
        Assert.Null(store.Connect(a.Id, b.Id));
    }

    [Fact]
    public void Paste_of_three_nodes_is_one_undo_step()
    {
        var store = TestBoards.Store();
        Assert.True(store.PasteMany([TestBoards.Node(), TestBoards.Node(), TestBoards.Node()], []));
        Assert.Equal(3, store.Document.Nodes.Count);
        store.Undo();
        Assert.Empty(store.Document.Nodes);
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void A_paste_that_would_pass_the_limit_places_nothing()
    {
        var store = TestBoards.Store(maxNodes: 2);
        Assert.False(store.PasteMany([TestBoards.Node(), TestBoards.Node(), TestBoards.Node()], []));
        Assert.Empty(store.Document.Nodes);
    }

    [Fact]
    public void Duplicate_remaps_internal_edges_to_the_copies()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var outside = TestBoards.Node(x: 800);
        var store = TestBoards.StoreWith(a, b, outside);
        store.Connect(a.Id, b.Id);
        store.Connect(b.Id, outside.Id);

        var copies = store.Duplicate([a.Id, b.Id]);

        Assert.Equal(2, copies.Count);
        var copyEdge = Assert.Single(store.Document.Edges, e => copies.Contains(e.FromNodeId) || copies.Contains(e.ToNodeId));
        Assert.True(copies.Contains(copyEdge.FromNodeId) && copies.Contains(copyEdge.ToNodeId));
        var copyOfA = store.FindNode(copies[0])!;
        Assert.Equal(a.X + 24, copyOfA.X);
        Assert.Equal(a.Y + 24, copyOfA.Y);
        Assert.NotSame(a.Data, copyOfA.Data);
    }

    [Fact]
    public void Editing_a_connector_label_is_undoable()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var store = TestBoards.StoreWith(a, b);
        var edge = store.Connect(a.Id, b.Id)!;
        store.UpdateEdge(edge.Id, e => { e.Label = "heard at 3am"; e.Arrow = EdgeArrow.Both; });
        Assert.Equal(("heard at 3am", EdgeArrow.Both), (edge.Label, edge.Arrow));
        store.Undo();
        Assert.Equal((null, EdgeArrow.End), (edge.Label, edge.Arrow));
    }

    [Fact]
    public void Every_change_is_announced_and_counted()
    {
        var store = TestBoards.Store();
        var changes = new List<CanvasChange>();
        store.OnChange += changes.Add;
        var node = TestBoards.Node();
        store.AddNode(node);
        store.MoveNodes([node.Id], 5, 5);
        Assert.Equal(2, store.ChangeCounter);
        Assert.Equal([CanvasChangeKind.Document, CanvasChangeKind.NodeGeometry], changes.Select(c => c.Kind));
        Assert.Equal([node.Id], changes[1].NodeIds!);
    }

    [Fact]
    public void History_keeps_pictures_of_removed_blocks_alive()
    {
        var node = TestBoards.Node(CanvasNodeType.Image);
        var asset = Guid.NewGuid();
        ((ImageData)node.Data).AssetId = asset;
        var store = TestBoards.StoreWith(node);
        store.RemoveNodes([node.Id]);
        Assert.Contains(asset, store.AssetIdsHeldByHistory());
    }
}

/// <summary>Groups keep their members, their rectangle and their history straight.</summary>
public sealed class CanvasStoreGroupTests
{
    [Fact]
    public void Moving_a_group_moves_its_members()
    {
        var a = TestBoards.Node(x: 0, y: 0);
        var b = TestBoards.Node(x: 400, y: 0);
        var store = TestBoards.StoreWith(a, b);
        var group = store.Group([a.Id, b.Id])!;
        var gx = group.X;

        store.MoveGroup(group.Id, 50, 20);
        Assert.Equal((50d, 20d, 450d, gx + 50), (a.X, a.Y, b.X, group.X));
        store.Undo();
        Assert.Equal((0d, 400d, gx), (a.X, b.X, group.X));
    }

    [Fact]
    public void Resizing_a_group_does_not_move_members()
    {
        var a = TestBoards.Node(x: 100, y: 100);
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id])!;
        store.ResizeGroup(group.Id, group.X - 100, group.Y, group.Width + 400, group.Height + 300);
        Assert.Equal((100d, 100d), (a.X, a.Y));
        Assert.Equal(group.Id, a.GroupId);
    }

    [Fact]
    public void Deleting_a_group_keeps_members()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id])!;
        store.Ungroup(group.Id);
        Assert.Empty(store.Document.Groups);
        Assert.Single(store.Document.Nodes);
        Assert.Null(a.GroupId);
        store.Undo();
        Assert.Equal(group.Id, a.GroupId);
    }

    [Fact]
    public void Grouping_captures_previous_group_ids_so_undo_restores_them()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var store = TestBoards.StoreWith(a, b);
        var first = store.Group([a.Id])!;
        store.Group([a.Id, b.Id]);
        store.Undo();
        Assert.Equal(first.Id, a.GroupId);
        Assert.Null(b.GroupId);
    }

    [Fact]
    public void A_new_group_is_at_least_its_minimum_size()
    {
        var a = TestBoards.Node(CanvasNodeType.Text, width: 160, height: 80);
        var group = TestBoards.StoreWith(a).Group([a.Id])!;
        Assert.True(group.Width >= BlockRegistry.GroupMinWidth);
        Assert.True(group.Height >= BlockRegistry.GroupMinHeight);
        Assert.True(CanvasHitTester.RectOf(group).Contains(a.X + a.Width / 2, a.Y + a.Height / 2));
    }

    [Fact]
    public void An_empty_group_survives_undo_of_its_last_member_leaving()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id])!;
        store.AssignGroup(a.Id, null);
        Assert.Single(store.Document.Groups);
        store.Undo();
        Assert.Equal(group.Id, a.GroupId);
    }

    [Fact]
    public void Renaming_to_blank_uses_the_default_label()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id], "Basement")!;
        store.RenameGroup(group.Id, "  ");
        Assert.Equal("Group", group.Label);
    }

    [Fact]
    public void Renaming_to_the_same_label_pushes_nothing()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id], "Basement")!;
        Assert.False(store.RenameGroup(group.Id, "Basement"));
        Assert.Equal("Group", store.UndoDescription);
    }

    [Fact]
    public void Changing_a_group_colour_is_one_undo_step()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id])!;
        store.SetGroupColor(group.Id, "2");
        Assert.Equal("2", group.ColorKey);
        store.Undo();
        Assert.Null(group.ColorKey);
    }

    [Fact]
    public void A_group_cannot_shrink_below_three_hundred_by_two_hundred()
    {
        var a = TestBoards.Node();
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id])!;
        store.ResizeGroup(group.Id, 0, 0, 10, 10);
        Assert.Equal((300d, 200d), (group.Width, group.Height));
    }
}

/// <summary>
/// A drag moves blocks freely, records one undo step at the end, and decides group membership by where
/// each block's centre landed.
/// </summary>
public sealed class CanvasStoreLiveDragTests
{
    [Fact]
    public void A_drag_pushes_one_undo_entry_not_one_per_move()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        var session = store.BeginMove([node.Id], [], SnapGuides.Empty, 6, snapEnabled: false);
        for (var i = 1; i <= 50; i++) session.Move(i, i);
        Assert.True(session.Commit());
        Assert.Equal((50d, 50d), (node.X, node.Y));
        Assert.True(store.Undo());
        Assert.Equal((0d, 0d), (node.X, node.Y));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void A_drag_that_ends_where_it_started_pushes_nothing()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        var session = store.BeginMove([node.Id], [], SnapGuides.Empty, 6, false);
        session.Move(30, 30);
        session.Move(0, 0);
        Assert.False(session.Commit());
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Cancel_restores_every_node()
    {
        var a = TestBoards.Node(x: 10, y: 20);
        var b = TestBoards.Node(x: 400, y: 20);
        var store = TestBoards.StoreWith(a, b);
        var session = store.BeginMove([a.Id, b.Id], [], SnapGuides.Empty, 6, false);
        session.Move(100, 100);
        session.Cancel();
        Assert.Equal((10d, 20d, 400d, 20d), (a.X, a.Y, b.X, b.Y));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void A_live_move_bumps_the_version_of_each_dragged_node()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        var before = store.VersionOf(node.Id);
        var session = store.BeginMove([node.Id], [], SnapGuides.Empty, 6, false);
        session.Move(40, 0);
        var moved = store.VersionOf(node.Id);
        session.Commit();
        Assert.True(moved > before);
        Assert.True(store.VersionOf(node.Id) > moved);
    }

    [Fact]
    public void Members_of_a_selected_group_move_with_it()
    {
        var a = TestBoards.Node(x: 50, y: 50);
        var store = TestBoards.StoreWith(a);
        var group = store.Group([a.Id])!;
        var gx = group.X;
        var session = store.BeginMove([], [group.Id], SnapGuides.Empty, 6, false);
        session.Move(70, 0);
        session.Commit();
        Assert.Equal((120d, gx + 70), (a.X, group.X));
        Assert.Equal(group.Id, a.GroupId);
    }

    [Fact]
    public void Origin_is_the_pointer_not_the_node_edge()
    {
        var node = TestBoards.Node(x: 333, y: 222);
        var store = TestBoards.StoreWith(node);
        var session = store.BeginMove([node.Id], [], SnapGuides.Empty, 6, false);
        session.Move(17, -9);
        session.Commit();
        Assert.Equal((350d, 213d), (node.X, node.Y));
    }

    [Fact]
    public void Snap_guide_is_drawn_at_the_snapped_position()
    {
        var node = TestBoards.Node(x: 0, y: 0, width: 200, height: 100);
        var store = TestBoards.StoreWith(node);
        var guides = new SnapGuides([500], []);
        var session = store.BeginMove([node.Id], [], guides, 6, snapEnabled: true);
        var result = session.Move(297, 40);
        Assert.Equal(500, result.GuideX);
        Assert.Equal(300, result.SnappedDx);
        Assert.Equal(500, node.X + node.Width);
        Assert.Null(result.GuideY);
    }

    [Fact]
    public void Snapping_off_returns_the_raw_delta()
    {
        var node = TestBoards.Node(x: 0, y: 0, width: 200, height: 100);
        var store = TestBoards.StoreWith(node);
        var session = store.BeginMove([node.Id], [], new SnapGuides([500], []), 6, snapEnabled: false);
        var result = session.Move(297, 40);
        Assert.Equal(297, result.SnappedDx);
        Assert.Null(result.GuideX);
    }

    [Fact]
    public void A_second_BeginMove_cancels_the_first()
    {
        var node = TestBoards.Node();
        var store = TestBoards.StoreWith(node);
        var first = store.BeginMove([node.Id], [], SnapGuides.Empty, 6, false);
        first.Move(80, 80);
        var second = store.BeginMove([node.Id], [], SnapGuides.Empty, 6, false);
        Assert.False(first.IsActive);
        Assert.Equal((0d, 0d), (node.X, node.Y));
        second.Move(10, 0);
        second.Commit();
        Assert.Equal(10, node.X);
    }

    [Fact]
    public void A_locked_node_is_left_behind()
    {
        var free = TestBoards.Node();
        var locked = TestBoards.Node(x: 400);
        locked.Locked = true;
        var store = TestBoards.StoreWith(free, locked);
        var session = store.BeginMove([free.Id, locked.Id], [], SnapGuides.Empty, 6, false);
        session.Move(30, 0);
        session.Commit();
        Assert.Equal((30d, 400d), (free.X, locked.X));
    }

    [Fact]
    public void Dropping_a_node_into_a_group_joins_it_in_the_same_undo_step()
    {
        var member = TestBoards.Node(x: 0, y: 0);
        var outsider = TestBoards.Node(CanvasNodeType.Text, x: 2000, y: 2000, width: 160, height: 80);
        var store = TestBoards.StoreWith(member, outsider);
        var group = store.Group([member.Id])!;

        var session = store.BeginMove([outsider.Id], [], SnapGuides.Empty, 6, false);
        session.Move(group.X + 40 - 2000, group.Y + 40 - 2000);
        session.Commit();
        Assert.Equal(group.Id, outsider.GroupId);

        store.Undo();
        Assert.Null(outsider.GroupId);
        Assert.Equal((2000d, 2000d), (outsider.X, outsider.Y));
    }

    [Fact]
    public void Dragging_a_node_out_of_a_group_clears_it()
    {
        var member = TestBoards.Node(x: 0, y: 0);
        var store = TestBoards.StoreWith(member);
        store.Group([member.Id]);
        var session = store.BeginMove([member.Id], [], SnapGuides.Empty, 6, false);
        session.Move(5000, 5000);
        session.Commit();
        Assert.Null(member.GroupId);
    }

    [Fact]
    public void Moving_a_group_does_not_reassign_its_own_members()
    {
        var member = TestBoards.Node(x: 0, y: 0);
        var store = TestBoards.StoreWith(member);
        var group = store.Group([member.Id])!;
        var session = store.BeginMove([member.Id], [group.Id], SnapGuides.Empty, 6, false);
        session.Move(3000, 0);
        session.Commit();
        Assert.Equal(group.Id, member.GroupId);
    }

    [Fact]
    public void A_resize_session_respects_the_block_minimum()
    {
        var node = TestBoards.Node(CanvasNodeType.Card, width: 300, height: 200);
        var store = TestBoards.StoreWith(node);
        var session = store.BeginResize(node.Id, "br")!;
        var rect = session.Move(-1000, -1000, keepAspect: false);
        Assert.Equal((220d, 140d), (rect.Width, rect.Height));
    }

    [Fact]
    public void Resize_commit_is_one_undo_entry()
    {
        var node = TestBoards.Node(CanvasNodeType.Card, width: 300, height: 200);
        var store = TestBoards.StoreWith(node);
        var session = store.BeginResize(node.Id, "r")!;
        session.Move(10, 0, false);
        session.Move(40, 0, false);
        Assert.True(session.Commit());
        Assert.Equal(340, node.Width);
        store.Undo();
        Assert.Equal(300, node.Width);
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void A_block_that_does_not_resize_has_no_resize_session()
    {
        var file = TestBoards.Node(CanvasNodeType.File);
        var link = TestBoards.Node(CanvasNodeType.Link);
        var store = TestBoards.StoreWith(file, link);
        Assert.Null(store.BeginResize(file.Id, "r"));
        Assert.Null(store.BeginResize(link.Id, "b"));
        Assert.NotNull(store.BeginResize(link.Id, "r"));
    }
}
