using System.Text.RegularExpressions;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Editor.Blocks;
using Ben.Canvas.Editor.Components;
using Ben.Canvas.Editor.Components.Board;
using Ben.Canvas.Editor.Components.Chrome;
using Ben.Canvas.Editor.Components.Overlays;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Canvas.Tests.Components;

/// <summary>The board's markup contract: what the gesture script and screen readers rely on.</summary>
public sealed class CanvasBoardRenderTests
{
    private static CanvasDocument TwoNodesOneEdge(out CanvasNode a, out CanvasNode b)
    {
        var document = new CanvasDocument();
        a = TestBoards.Node(x: 0);
        a.Z = 1;
        b = TestBoards.Node(x: 500);
        b.Z = 0;
        document.Nodes.AddRange([a, b]);
        document.Edges.Add(new CanvasEdge { FromNodeId = a.Id, ToNodeId = b.Id });
        document.NextZ = 2;
        return document;
    }

    [Fact]
    public async Task A_document_with_two_nodes_and_one_edge_renders_two_blocks_and_one_connector()
    {
        var html = await RenderHelper.RenderWithBoardAsync<CanvasBoard>(TwoNodesOneEdge(out _, out _));
        Assert.Equal(2, Regex.Matches(html, @"class=""bc-node bc-node--").Count);
        Assert.Single(Regex.Matches(html, @"<g [^>]*data-bc-edge="""));
        Assert.Single(Regex.Matches(html, @"class=""bc-edge__hit"""));
    }

    [Fact]
    public async Task Nodes_render_in_z_order_without_inline_z_index()
    {
        var document = TwoNodesOneEdge(out var a, out var b);
        var html = await RenderHelper.RenderWithBoardAsync<CanvasBoard>(document);
        Assert.True(html.IndexOf($"data-bc-node=\"{b.Id}\"", StringComparison.Ordinal) < html.IndexOf($"data-bc-node=\"{a.Id}\"", StringComparison.Ordinal),
            "The block with the lower Z must come first in the DOM.");
        Assert.DoesNotContain("z-index", html);
    }

    [Fact]
    public async Task JS_owned_classes_are_absent_at_render_time()
    {
        var html = await RenderHelper.RenderWithBoardAsync<CanvasBoard>(TwoNodesOneEdge(out _, out _));
        foreach (var cls in new[] { "bc-node--dragging", "bc-node--resizing", "bc-board--gesture", "bc-guide--on", "bc-marquee--on" })
            Assert.DoesNotContain(cls, html);
    }

    [Fact]
    public async Task The_board_names_its_help()
    {
        var html = await RenderHelper.RenderWithBoardAsync<CanvasBoard>(new CanvasDocument());
        Assert.Contains("role=\"application\"", html);
        Assert.Contains("aria-describedby=\"bc-board-help\"", html);
        Assert.Contains("id=\"bc-board-help\"", html);
    }

    [Fact]
    public async Task A_locked_node_says_so_in_words()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node();
        node.Locked = true;
        document.Nodes.Add(node);
        var html = await RenderHelper.RenderWithBoardAsync<CanvasBoard>(document);
        // "Card: Evidence", not "Card: Card": an untitled card is announced by what kind of card it is,
        // now that there is more than one kind (Ben, 2026-09-17).
        Assert.True(html.Contains("aria-label=\"Card: Evidence, locked\""), html);
        Assert.Contains("data-bc-locked=\"true\"", html);
    }

    [Fact]
    public async Task The_board_style_does_not_change_after_the_viewport_changes()
    {
        await using var session = await RenderSession.StartAsync();
        var first = StyleOf(await session.RenderAsync<CanvasBoard>());
        await session.InvokeAsync(() =>
        {
            session.Services.GetRequiredService<CanvasViewportState>().Set(new Core.Geometry.CanvasViewport(300, 200, 2));
            return Task.CompletedTask;
        });
        Assert.Equal(first, StyleOf(await session.HtmlAsync()));
    }

    [Fact]
    public async Task Board_does_not_render_while_a_gesture_is_active()
    {
        await using var session = await RenderSession.StartAsync();
        var store = session.Services.GetRequiredService<CanvasStore>();
        var node = TestBoards.Node();
        store.Load(new CanvasDocument { Nodes = [node] });
        await session.RenderAsync<CanvasBoard>();

        await session.InvokeAsync(() =>
        {
            session.Services.GetRequiredService<CanvasViewportState>().SetGestureActive(true);
            store.MoveNodes([node.Id], 777, 0);
            // A selection change asks the board to render; the gate must still hold it back.
            session.Services.GetRequiredService<SelectionState>().Select(node.Id);
            return Task.CompletedTask;
        });

        // The block itself, not the selection overlay, which may follow the selection.
        Assert.DoesNotMatch(@"class=""bc-node bc-node--card[^""]*""[^>]*style=""left:777px", await session.HtmlAsync());
    }

    private static string StyleOf(string html) => Regex.Match(html, @"class=""bc-board""[^>]*style=""([^""]*)""").Groups[1].Value;
}

/// <summary>Handles and ports appear only where a gesture can use them.</summary>
public sealed class SelectionOverlayTests
{
    private static async Task<string> RenderSelected(params CanvasNode[] nodes)
    {
        var document = new CanvasDocument();
        document.Nodes.AddRange(nodes);
        return await RenderHelper.RenderWithBoardAsync<SelectionOverlay>(document, arrange: sp =>
            sp.GetRequiredService<SelectionState>().SelectMany(nodes.Select(n => n.Id)));
    }

    [Fact]
    public async Task One_selected_resizable_node_gets_eight_handles_in_canonical_order()
    {
        var html = await RenderSelected(TestBoards.Node());
        var order = Regex.Matches(html, @"data-bc-handle=""([a-z]+)""").Select(m => m.Groups[1].Value);
        Assert.Equal(["tl", "t", "tr", "r", "br", "b", "bl", "l"], order);
    }

    [Fact]
    public async Task Two_selected_nodes_get_outlines_but_no_handles()
    {
        var html = await RenderSelected(TestBoards.Node(), TestBoards.Node(x: 400));
        Assert.DoesNotContain("data-bc-handle", html);
        Assert.Equal(2, Regex.Matches(html, "bc-selection--outline").Count);
    }

    [Fact]
    public async Task A_locked_node_gets_no_handles()
    {
        var node = TestBoards.Node();
        node.Locked = true;
        Assert.DoesNotContain("data-bc-handle", await RenderSelected(node));
    }

    [Fact]
    public async Task A_file_node_is_not_resizable_so_has_no_handles() =>
        Assert.DoesNotContain("data-bc-handle", await RenderSelected(TestBoards.Node(CanvasNodeType.File)));

    [Fact]
    public async Task A_link_shows_only_side_handles()
    {
        var html = await RenderSelected(TestBoards.Node(CanvasNodeType.Link));
        Assert.Equal(["r", "l"], Regex.Matches(html, @"data-bc-handle=""([a-z]+)""").Select(m => m.Groups[1].Value));
    }

    /// <summary>
    /// A side handle says both of the things it does, because it does two: clicked it makes the next
    /// block there, dragged it aims a connector (Ben, 2026-09-16: "Can we make it like Miro?").
    /// </summary>
    [Fact]
    public async Task Ports_have_spoken_names_and_side_values()
    {
        var html = await RenderSelected(TestBoards.Node());
        Assert.Contains("data-bc-port=\"Right\"", html);
        Assert.Contains("aria-label=\"Add a block to the right, or drag to connect\"", html);
        Assert.Equal(4, Regex.Matches(html, "data-bc-port=").Count);
    }
}

/// <summary>The zoom controls and the minimap.</summary>
public sealed class ZoomControlsTests
{
    [Fact]
    public async Task Every_zoom_button_has_a_name()
    {
        var html = await RenderHelper.RenderAsync<ZoomControls>();
        foreach (Match button in Regex.Matches(html, "<button[^>]*>"))
            Assert.Contains("aria-label=", button.Value);
        Assert.Equal(4, Regex.Matches(html, "<button").Count);
    }

    [Fact]
    public async Task The_reset_button_shows_the_zoom_as_a_percentage()
    {
        var html = await RenderHelper.RenderAsync<ZoomControls>(new Dictionary<string, object?> { [nameof(ZoomControls.Zoom)] = 0.5 });
        Assert.Contains("50%", html);
    }

    [Fact]
    public async Task The_minimap_draws_one_rect_per_node_plus_the_view()
    {
        var document = new CanvasDocument { Nodes = [TestBoards.Node(), TestBoards.Node(x: 900)] };
        var html = await RenderHelper.RenderWithBoardAsync<Minimap>(document);
        Assert.Equal(2, Regex.Matches(html, "bc-minimap__node").Count);
        Assert.Single(Regex.Matches(html, "bc-minimap__view"));
    }

    [Fact]
    public async Task The_minimap_is_left_out_when_the_option_is_off()
    {
        await using var session = await RenderSession.StartAsync(s => s.Configure<CanvasEditorOptions>(o => o.ShowMinimap = false));
        Assert.DoesNotContain("bc-minimap", await session.RenderAsync<CanvasEditor>());
    }
}

/// <summary>The live region, the save state and the empty state.</summary>
public sealed class ChromeStateTests
{
    [Fact]
    public async Task The_live_region_is_polite_and_atomic()
    {
        var html = await RenderHelper.RenderAsync<LiveAnnouncer>();
        Assert.Contains("id=\"bc-live\"", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("aria-atomic=\"true\"", html);
    }

    [Fact]
    public async Task It_shows_what_the_service_last_said()
    {
        await using var session = await RenderSession.StartAsync();
        session.Services.GetRequiredService<AnnouncerService>().Say("Moved to 3, 4.");
        Assert.Contains("Moved to 3, 4.", await session.RenderAsync<LiveAnnouncer>());
    }

    [Fact]
    public async Task Clean_is_a_status_region()
    {
        var html = await RenderHelper.RenderAsync<SaveStateIndicator>();
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("No changes yet", html);
    }

    [Fact]
    public async Task Server_conflict_offers_a_button_mentioning_newer_copy()
    {
        var html = await RenderHelper.RenderAsync<SaveStateIndicator>(new Dictionary<string, object?>
        {
            [nameof(SaveStateIndicator.State)] = new SaveState(SaveStateKind.ServerConflict),
        });
        Assert.Matches("<button[^>]*>[\\s\\S]*newer copy", html);
    }

    [Fact]
    public async Task Server_failed_offers_retry() =>
        Assert.Contains("tap to retry", await RenderHelper.RenderAsync<SaveStateIndicator>(new Dictionary<string, object?>
        {
            [nameof(SaveStateIndicator.State)] = new SaveState(SaveStateKind.ServerFailed),
        }));

    [Fact]
    public async Task Saved_to_case_uses_relative_time()
    {
        var now = new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);
        var html = await RenderHelper.RenderAsync<SaveStateIndicator>(new Dictionary<string, object?>
        {
            [nameof(SaveStateIndicator.State)] = new SaveState(SaveStateKind.SavedServer, now.AddMinutes(-2)),
            [nameof(SaveStateIndicator.NowUtc)] = now,
        });
        Assert.Contains("Saved to case 2 min ago", html);
    }

    [Fact]
    public async Task An_empty_board_says_so_and_offers_add_a_card()
    {
        var html = await RenderHelper.RenderAsync<BoardEmptyState>(new Dictionary<string, object?> { [nameof(BoardEmptyState.Visible)] = true });
        Assert.Contains("This board is empty", html);
        Assert.Contains("data-bc-action=\"add-card\"", html);
    }

    [Fact]
    public async Task A_board_with_nodes_renders_no_empty_state() =>
        Assert.DoesNotContain("bc-empty", await RenderHelper.RenderAsync<BoardEmptyState>(new Dictionary<string, object?> { [nameof(BoardEmptyState.Visible)] = false }));
}

/// <summary>The header, rail, panel and shortcut list.</summary>
public sealed class EditorChromeTests
{
    [Fact]
    public async Task Undo_is_disabled_with_nothing_to_undo()
    {
        var html = await RenderHelper.RenderAsync<CanvasHeaderBar>();
        Assert.Matches(@"data-bc-action=""undo""[^>]*disabled", html);
    }

    [Fact]
    public async Task Undo_title_names_the_last_action()
    {
        var html = await RenderHelper.RenderAsync<CanvasHeaderBar>(new Dictionary<string, object?>
        {
            [nameof(CanvasHeaderBar.CanUndo)] = true,
            [nameof(CanvasHeaderBar.UndoDescription)] = "Move 1 item",
        });
        Assert.Contains("title=\"Undo move 1 item\"", html);
    }

    [Fact]
    public async Task The_properties_toggle_controls_bc_props() =>
        Assert.Matches(@"aria-controls=""bc-props""", await RenderHelper.RenderAsync<CanvasHeaderBar>());

    [Fact]
    public async Task Disabled_blocks_are_not_in_the_rail()
    {
        await using var session = await RenderSession.StartAsync(s => s.Configure<CanvasEditorOptions>(o => o.EnabledBlocks.Remove(CanvasNodeType.Map)));
        var html = await session.RenderAsync<ToolRail>();
        Assert.DoesNotContain("add-map", html);
        Assert.Contains("add-card", html);
    }

    [Fact]
    public async Task Every_rail_button_has_a_name()
    {
        var html = await RenderHelper.RenderAsync<ToolRail>();
        var rail = Regex.Match(html, "<nav class=\"bc-rail\".*?</nav>", RegexOptions.Singleline).Value;
        var buttons = Regex.Matches(rail, "<button[^>]*>").Select(m => m.Value).ToList();
        // Seven block kinds, the case-files picker and Paste. The number is the assertion: a rail that
        // silently grew a button is a rail somebody added a kind to without naming it.
        Assert.Equal(9, buttons.Count);
        Assert.All(buttons, b => Assert.Matches("aria-label=\"(Add |Paste\")", b));
    }

    /// <summary>The phone bar carries exactly the five tools the phone layout was designed around.</summary>
    [Fact]
    public async Task The_phone_bar_has_add_paste_undo_redo_and_more_in_that_order()
    {
        var html = await RenderHelper.RenderAsync<ToolRail>(new Dictionary<string, object?> { ["CanUndo"] = true, ["CanRedo"] = false });
        var bar = Regex.Match(html, "<nav class=\"bc-bar\".*?</nav>", RegexOptions.Singleline).Value;
        var actions = Regex.Matches(bar, "data-bc-action=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["add-menu", "paste", "undo", "redo", "more"], actions);
        Assert.Matches("data-bc-action=\"redo\"[^>]*disabled", bar);
        Assert.All(Regex.Matches(bar, "<button[^>]*>").Select(m => m.Value), b => Assert.Contains("aria-label=", b));
    }

    /// <summary>
    /// A Blazor click on the Paste button would read the clipboard after a round trip through .NET, and iOS
    /// refuses a read that is not inside the tap itself.
    /// </summary>
    /// <remarks>A source check: the static renderer never writes Blazor event handlers into its HTML.</remarks>
    [Fact]
    public void The_paste_button_has_no_blazor_click()
    {
        var offenders = new List<string>();
        var found = 0;
        foreach (var file in RepoFiles.UiFiles("*.razor"))
        {
            foreach (Match button in Regex.Matches(RepoFiles.ReadWithoutComments(file), "<(button|a|label)[^>]*data-bc-action=\"paste\"[^>]*>", RegexOptions.Singleline))
            {
                found++;
                if (button.Value.Contains("@onclick", StringComparison.Ordinal)) offenders.Add(RepoFiles.Relative(file));
            }
        }

        Assert.True(found > 0, "There is no Paste button.");
        Assert.True(offenders.Count == 0, "These Paste buttons run a Blazor click, which loses the tap iOS needs:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public async Task One_selected_node_shows_labelled_position_and_size_inputs()
    {
        var node = TestBoards.Node();
        var html = await RenderHelper.RenderWithBoardAsync<PropertiesPanel>(new CanvasDocument { Nodes = [node] },
            arrange: sp => sp.GetRequiredService<SelectionState>().Select(node.Id));
        foreach (var id in new[] { "bc-prop-x", "bc-prop-y", "bc-prop-w", "bc-prop-h" })
        {
            Assert.Contains($"id=\"{id}\"", html);
            Assert.Contains($"for=\"{id}\"", html);
        }
    }

    [Fact]
    public async Task Nothing_selected_says_how_to_start() =>
        Assert.Contains("Select an item to see its properties.", await RenderHelper.RenderWithBoardAsync<PropertiesPanel>(new CanvasDocument()));

    [Fact]
    public async Task An_edge_selection_offers_delete_connector()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var edge = new CanvasEdge { FromNodeId = a.Id, ToNodeId = b.Id };
        var html = await RenderHelper.RenderWithBoardAsync<PropertiesPanel>(new CanvasDocument { Nodes = [a, b], Edges = [edge] },
            arrange: sp => sp.GetRequiredService<SelectionState>().SelectEdge(edge.Id));
        Assert.Contains("Delete connector", html);
    }

    [Fact]
    public void The_shortcut_list_has_ctrl_shift_l_for_lock_and_no_reserved_browser_chords()
    {
        var keys = ShortcutHelpRows();
        Assert.Contains(keys, k => k.Contains("Ctrl+Shift+L"));
        Assert.DoesNotContain(keys, k => Regex.IsMatch(k, @"Ctrl\+[1-8]\b|Ctrl\+L\b|Ctrl\+E\b"));
    }

    [Fact]
    public void Every_block_type_has_a_renderer()
    {
        foreach (var type in Enum.GetValues<CanvasNodeType>())
            Assert.True(BlockRendererMap.All.ContainsKey(type), $"{type} has no renderer.");
    }

    private static List<string> ShortcutHelpRows() =>
        typeof(ShortcutHelp).GetField("Rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) is (string Keys, string What)[] rows ? rows.Select(r => r.Keys).ToList() : [];
}

/// <summary>
/// The keyboard reaches every command, including keyboard-only resize and connect, and bare letters do
/// nothing unless the board has focus.
/// </summary>
public sealed class CanvasEditorKeyboardTests
{
    private static async Task<(RenderSession Session, CanvasEditor Editor, CanvasStore Store, SelectionState Selection, AnnouncerService Announcer)> Start(params CanvasNode[] nodes)
    {
        var session = await RenderSession.StartAsync();
        var store = session.Services.GetRequiredService<CanvasStore>();
        var document = new CanvasDocument();
        foreach (var node in nodes) { node.Z = document.NextZ++; document.Nodes.Add(node); }
        store.Load(document);
        await session.RenderAsync<EditorHarness>();
        var editor = (CanvasEditor)session.Services.GetRequiredService<HarnessRegistry>().Instance!;
        return (session, editor, store, session.Services.GetRequiredService<SelectionState>(), session.Services.GetRequiredService<AnnouncerService>());
    }

    private static Task Key(RenderSession session, CanvasEditor editor, string key, bool ctrl = false, bool shift = false, bool board = true) =>
        session.InvokeAsync(() => editor.OnEditorKeyDown(key, ctrl, shift, false, board));

    [Fact]
    public void Every_canvas_command_has_a_case()
    {
        var source = File.ReadAllText(Path.Combine(RepoFiles.EditorRoot(), "Components", "CanvasEditor.razor.Keyboard.cs"));
        var missing = Enum.GetNames<Core.Input.CanvasCommand>().Where(n => !source.Contains($"case CanvasCommand.{n}:", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "No case for: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Bare_keys_without_board_focus_do_nothing()
    {
        var (session, editor, store, _, _) = await Start();
        await using var _s = session;
        await Key(session, editor, "t", board: false);
        Assert.Empty(store.Document.Nodes);
        await Key(session, editor, "t");
        Assert.Single(store.Document.Nodes);
    }

    [Fact]
    public async Task Arrow_moves_the_selection_one_px_and_shift_ten()
    {
        var node = TestBoards.Node();
        var (session, editor, _, selection, announcer) = await Start(node);
        await using var _s = session;
        await session.InvokeAsync(() => { selection.Select(node.Id); return Task.CompletedTask; });
        await Key(session, editor, "ArrowRight");
        await Key(session, editor, "ArrowDown", shift: true);
        Assert.Equal((1d, 10d), (node.X, node.Y));
        Assert.Contains("Moved to 1, 10", announcer.Text);
    }

    [Fact]
    public async Task Resize_mode_grows_width_by_ten_with_shift_right_and_stops_at_the_minimum()
    {
        var node = TestBoards.Node(CanvasNodeType.Card, width: 230, height: 150);
        var (session, editor, _, selection, _) = await Start(node);
        await using var _s = session;
        await session.InvokeAsync(() => { selection.Select(node.Id); return Task.CompletedTask; });
        await Key(session, editor, "r");
        await Key(session, editor, "ArrowRight", shift: true);
        Assert.Equal(240, node.Width);
        await Key(session, editor, "ArrowLeft", shift: true);
        await Key(session, editor, "ArrowLeft", shift: true);
        await Key(session, editor, "ArrowLeft", shift: true);
        Assert.Equal(220, node.Width);
        await Key(session, editor, "Escape");
        await Key(session, editor, "ArrowRight");
        Assert.Equal(1, node.X);
    }

    [Fact]
    public async Task Connect_mode_enter_connects_to_the_nearest_node()
    {
        var a = TestBoards.Node(x: 0);
        var near = TestBoards.Node(x: 400);
        var far = TestBoards.Node(x: 4000);
        var (session, editor, store, selection, announcer) = await Start(a, far, near);
        await using var _s = session;
        await session.InvokeAsync(() => { selection.Select(a.Id); return Task.CompletedTask; });
        await Key(session, editor, "c");
        await Key(session, editor, "Enter");
        var edge = Assert.Single(store.Document.Edges);
        Assert.Equal((a.Id, near.Id), (edge.FromNodeId, edge.ToNodeId));
        Assert.StartsWith("Connected", announcer.Text);
    }

    [Fact]
    public async Task Escape_leaves_connect_mode_without_an_edge()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var (session, editor, store, selection, _) = await Start(a, b);
        await using var _s = session;
        await session.InvokeAsync(() => { selection.Select(a.Id); return Task.CompletedTask; });
        await Key(session, editor, "c");
        await Key(session, editor, "Escape");
        await Key(session, editor, "Enter");
        Assert.Empty(store.Document.Edges);
    }

    [Fact]
    public async Task Delete_on_a_selected_edge_disconnects_it()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var (session, editor, store, selection, _) = await Start(a, b);
        await using var _s = session;
        await session.InvokeAsync(() =>
        {
            selection.SelectEdge(store.Connect(a.Id, b.Id)!.Id);
            return Task.CompletedTask;
        });
        await Key(session, editor, "Delete");
        Assert.Empty(store.Document.Edges);
        Assert.Equal(2, store.Document.Nodes.Count);
    }

    [Fact]
    public async Task A_locked_node_is_not_nudged()
    {
        var node = TestBoards.Node();
        node.Locked = true;
        var (session, editor, _, selection, announcer) = await Start(node);
        await using var _s = session;
        await session.InvokeAsync(() => { selection.Select(node.Id); return Task.CompletedTask; });
        await Key(session, editor, "ArrowLeft");
        Assert.Equal(0, node.X);
        Assert.Contains("locked", announcer.Text);
    }

    [Fact]
    public async Task Ctrl_z_undoes_and_ctrl_shift_z_redoes()
    {
        var (session, editor, store, _, _) = await Start();
        await using var _s = session;
        await Key(session, editor, "n");
        Assert.Single(store.Document.Nodes);
        await Key(session, editor, "z", ctrl: true);
        Assert.Empty(store.Document.Nodes);
        await Key(session, editor, "Z", ctrl: true, shift: true);
        Assert.Single(store.Document.Nodes);
    }
}
