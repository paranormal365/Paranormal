using System.Text.RegularExpressions;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Blocks;
using Ben.Canvas.Editor.Components.Board;
using Ben.Canvas.Editor.Components.Chrome;
using Ben.Canvas.Editor.Components.Nodes;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Canvas.Tests.Components;

/// <summary>What each kind of block shows at rest, and the controls it offers while being edited.</summary>
public sealed class BlockRendererTests
{
    private static Task<string> Render<TRenderer>(CanvasNode node, bool editing = false) where TRenderer : BlockRendererBase =>
        RenderHelper.RenderAsync<TRenderer>(new Dictionary<string, object?>
        {
            [nameof(BlockRendererBase.Node)] = node,
            [nameof(BlockRendererBase.Data)] = node.Data,
            [nameof(BlockRendererBase.Editing)] = editing,
        });

    // ── Notes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_note_links_web_addresses_and_nothing_else()
    {
        var node = TestBoards.Node(CanvasNodeType.Text);
        ((TextData)node.Data).Text = "Seen near https://example.com/porch. Call Sarah.";
        var html = await Render<TextNode>(node);
        Assert.Contains("href=\"https://example.com/porch\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
        Assert.Single(Regex.Matches(html, "<a "));
    }

    [Fact]
    public async Task A_note_never_renders_markup()
    {
        var node = TestBoards.Node(CanvasNodeType.Text);
        ((TextData)node.Data).Text = "<b>x</b><img src=x onerror=alert(1)>";
        var raw = await RenderRaw<TextNode>(node);
        Assert.DoesNotContain("<b>", raw);
        Assert.DoesNotContain("<img", raw);
    }

    [Fact]
    public async Task Edit_mode_shows_a_labelled_textarea()
    {
        var html = await Render<TextNode>(TestBoards.Node(CanvasNodeType.Text), editing: true);
        Assert.Contains("<textarea", html);
        Assert.Contains("aria-label=\"Note text\"", html);
    }

    // ── Files and images ────────────────────────────────────────────────

    [Fact]
    public async Task A_file_shows_its_size_with_a_dot_under_fr_FR()
    {
        var node = TestBoards.Node(CanvasNodeType.File);
        var file = (FileData)node.Data;
        file.FileName = "evp-recording.mp3";
        file.Size = (long)(1.5 * 1024 * 1024);
        string html = "";
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        try { html = await Render<FileNode>(node); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        Assert.Contains("1.5 MB", html);
        Assert.Contains("#music", html);
    }

    [Theory]
    [InlineData("a.pdf", "file-text")]
    [InlineData("b.MOV", "video")]
    [InlineData("c.zip", "file")]
    [InlineData("d.png", "image")]
    public void A_file_icon_follows_its_extension(string name, string icon) => Assert.Equal(icon, FileNode.IconFor(name));

    [Fact]
    public async Task An_image_without_a_source_says_what_to_do()
    {
        var html = await Render<ImageNode>(TestBoards.Node(CanvasNodeType.Image));
        Assert.Contains("No picture yet. Paste or drop one here.", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public async Task Image_edit_mode_labels_caption_and_fit()
    {
        var node = TestBoards.Node(CanvasNodeType.Image);
        var html = await Render<ImageNode>(node, editing: true);
        Assert.Contains($"for=\"bc-cap-{node.Id:N}\"", html);
        Assert.Contains($"id=\"bc-cap-{node.Id:N}\"", html);
        Assert.Contains("Fill the box", html);
    }

    // ── Cards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_evidence_card_lists_its_four_fields_in_order()
    {
        var html = await Render<CardNode>(TestBoards.Node(CanvasNodeType.Card));
        var labels = Regex.Matches(html, "<dt[^>]*>([^<]+)</dt>").Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.Equal(["Description", "Date", "Category", "Verified"], labels);
    }

    [Fact]
    public async Task A_ticked_checkbox_says_yes_with_a_glyph()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        ((CardData)node.Data).Fields["verified"] = "true";
        var html = await Render<CardNode>(node);
        Assert.Contains("#check-square", html);
        Assert.Contains("Yes", html);
    }

    [Fact]
    public async Task A_card_date_is_shown_from_iso()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        ((CardData)node.Data).Fields["date"] = "2026-09-14";
        Assert.Contains("Sep 14, 2026", await Render<CardNode>(node));
    }

    [Fact]
    public async Task An_unknown_template_still_shows_what_was_typed()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        var card = (CardData)node.Data;
        card.TemplateId = "witness";
        card.Fields["name"] = "Mrs Porter";
        var html = await Render<CardNode>(node);
        Assert.Contains("This card uses a template this editor does not have.", html);
        Assert.Contains("Mrs Porter", html);
    }

    [Fact]
    public async Task Edit_mode_renders_the_right_control_per_kind()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        var html = await Render<CardNode>(node, editing: true);
        Assert.Matches($@"id=""bc-card-{node.Id:N}-description""", html);
        Assert.Matches($@"type=""date""[^>]*|[^>]*type=""date""", html);
        Assert.Contains("<select", html);
        Assert.Contains(">Witness<", html);
        Assert.Contains("type=\"checkbox\"", html);
        foreach (Match forAttr in Regex.Matches(html, @"for=""([^""]+)"""))
            Assert.Contains($"id=\"{forAttr.Groups[1].Value}\"", html);
    }

    // ── Messages ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sarah Mitchell", "SM")]
    [InlineData("  emma  ", "E")]
    [InlineData("", "?")]
    [InlineData("Dr Jane Q Public", "DJ")]
    public void Initials_come_from_the_first_two_words(string author, string initials) => Assert.Equal(initials, MessageNode.Initials(author));

    [Fact]
    public async Task A_message_at_rest_shows_clean_markup_and_an_iso_time()
    {
        var node = TestBoards.Node(CanvasNodeType.Message);
        var message = (MessageData)node.Data;
        message.Author = "Sarah Mitchell";
        message.Html = "<p>Heard <b>footsteps</b><img src=x onerror=alert(1)></p>";
        var raw = await RenderRaw<MessageNode>(node);
        Assert.Contains("<b>footsteps</b>", raw);
        Assert.DoesNotContain("onerror", raw);
        Assert.Contains("datetime=\"2026-09-14T18:00:00", raw);
        Assert.DoesNotContain("k-editor", raw);
    }

    // ── Maps and links ──────────────────────────────────────────────────

    [Fact]
    public async Task A_map_shows_its_address_and_pin_count()
    {
        var node = TestBoards.Node(CanvasNodeType.Map);
        var map = (MapData)node.Data;
        map.Address = "Shelby Street Bridge, Nashville";
        map.Latitude = 36.16;
        map.Longitude = -86.77;
        for (var i = 0; i < 5; i++) map.Pins.Add(new MapPin { Title = $"Pin {i}" });
        var html = await Render<MapNode>(node);
        Assert.Contains("Shelby Street Bridge, Nashville", html);
        Assert.Contains("36.16000, -86.77000", html);
        Assert.Contains("and 2 more", html);
    }

    [Fact]
    public async Task A_map_without_a_place_says_so() =>
        Assert.Contains("No place set yet", await Render<MapNode>(TestBoards.Node(CanvasNodeType.Map)));

    [Fact]
    public async Task A_foreign_link_gets_the_away_bar_and_its_host()
    {
        var node = TestBoards.Node(CanvasNodeType.Link);
        ((LinkData)node.Data).Url = "https://example.com/a/b";
        var html = await Render<LinkNode>(node);
        Assert.Contains("bc-link--away", html);
        Assert.Contains("example.com", html);
        Assert.Contains("aria-label=\"Open example.com in a new tab\"", html);
    }

    [Fact]
    public async Task An_our_records_link_has_no_away_bar()
    {
        var node = TestBoards.Node(CanvasNodeType.Link);
        var link = (LinkData)node.Data;
        link.Url = "https://ishaunted.com/cases/1";
        link.Tier = LinkPreviewTier.OurRecords;
        Assert.DoesNotContain("bc-link--away", await Render<LinkNode>(node));
    }

    [Fact]
    public async Task A_javascript_url_gets_no_open_link()
    {
        var node = TestBoards.Node(CanvasNodeType.Link);
        ((LinkData)node.Data).Url = "javascript:alert(1)";
        Assert.DoesNotContain("bc-link__open", await Render<LinkNode>(node));
    }

    [Fact]
    public void The_image_goes_through_the_webapi_proxy() =>
        Assert.Equal("https://ishaunted.com/webapi/api/link-unfurl/image?url=https%3A%2F%2Fexample.com%2Fa.png",
            LinkNode.ImageProxyUrl("https://ishaunted.com/webapi/", "https://example.com/a.png"));

    [Fact]
    public void No_proxy_address_without_an_https_source() =>
        Assert.Null(LinkNode.ImageProxyUrl("https://ishaunted.com/webapi", "http://example.com/a.png"));

    // ── Tables ──────────────────────────────────────────────────────────

    private static CanvasNode Table(bool header = true, params string[][] rows)
    {
        var node = TestBoards.Node(CanvasNodeType.Table);
        node.Data = new TableData { HasHeaderRow = header, Rows = [.. rows.Select(r => r.ToList())] };
        return node;
    }

    /// <summary>
    /// At rest a table is a real table element, so it reads as a grid to a screen reader and to anyone
    /// copying it back out — not a stack of divs that merely looks like one.
    /// </summary>
    [Fact]
    public async Task A_table_at_rest_is_a_table_element_with_a_header_row()
    {
        var html = await Render<TableNode>(Table(true, ["Name", "Born"], ["Walt", "1901"]));

        Assert.Contains("<table", html);
        Assert.Contains("<th", html);
        Assert.Contains("Walt", html);
        Assert.Equal(2, Regex.Matches(html, "<tr").Count);
    }

    /// <summary>A table with the header switched off has no header cells at all.</summary>
    [Fact]
    public async Task A_table_without_a_header_row_has_no_header_cells()
    {
        var html = await Render<TableNode>(Table(false, ["Walt", "1901"], ["Roy", "1893"]));

        Assert.DoesNotContain("<th", html);
    }

    /// <summary>
    /// Cell text is text. A pasted grid can carry anything, and a table that rendered markup would be
    /// the same hole the note was closed for.
    /// </summary>
    [Fact]
    public async Task A_table_never_renders_markup()
    {
        var raw = await RenderRaw<TableNode>(Table(false, ["<b>bold</b>", "<img src=x>"]));

        Assert.DoesNotContain("<b>", raw);
        Assert.DoesNotContain("<img", raw);
    }

    /// <summary>
    /// Every cell input names where it is. A grid of unlabelled boxes is unusable with a screen reader,
    /// and the repo's own guard only checks that a label points at an id — not that it says anything.
    /// </summary>
    [Fact]
    public async Task Every_table_cell_in_edit_names_its_row_and_column()
    {
        var html = await Render<TableNode>(Table(true, ["Name", "Born"], ["Walt", "1901"]), editing: true);

        Assert.Equal(4, Regex.Matches(html, "<input").Count);
        Assert.Contains("aria-label=\"Name, row 1\"", html);
        Assert.Contains("aria-label=\"Born, row 1\"", html);
    }

    /// <summary>
    /// A table with no header names its columns by position, because "row 1" alone does not say which
    /// cell of the row a person is in.
    /// </summary>
    [Fact]
    public async Task A_headerless_table_names_its_cells_by_column_number()
    {
        var html = await Render<TableNode>(Table(false, ["Walt", "1901"]), editing: true);

        Assert.Contains("aria-label=\"Column 1, row 1\"", html);
    }

    /// <summary>
    /// The row and column controls are only offered while editing, and each says what it does — they are
    /// icon buttons, which the markup guard requires to carry a label.
    /// </summary>
    [Fact]
    public async Task Editing_offers_labelled_row_and_column_controls()
    {
        var html = await Render<TableNode>(Table(true, ["Name", "Born"], ["Walt", "1901"]), editing: true);

        Assert.Contains("Add row", html);
        Assert.Contains("Add column", html);
        Assert.Contains("Remove row", html);
        Assert.Contains("Remove column", html);
    }

    [Fact]
    public async Task A_table_at_rest_offers_no_controls()
    {
        var html = await Render<TableNode>(Table(true, ["Name", "Born"], ["Walt", "1901"]));

        Assert.DoesNotContain("Add row", html);
        Assert.DoesNotContain("<input", html);
    }

    // ── Shapes ──────────────────────────────────────────────────────────

    private static CanvasNode Shape(ShapeKind kind, string text = "")
    {
        var node = TestBoards.Node(CanvasNodeType.Shape);
        node.Data = new ShapeData { Kind = kind, Text = text };
        return node;
    }

    /// <summary>
    /// Each kind draws as itself. The class is what the CSS hangs the outline on, so it is the thing
    /// worth asserting — a diamond that renders with the rectangle's class is a rectangle.
    /// </summary>
    [Theory]
    [InlineData(ShapeKind.Rectangle, "bc-shape--rectangle")]
    [InlineData(ShapeKind.Ellipse, "bc-shape--ellipse")]
    [InlineData(ShapeKind.Diamond, "bc-shape--diamond")]
    public async Task A_shape_draws_as_its_kind(ShapeKind kind, string expected)
    {
        Assert.Contains(expected, await Render<ShapeNode>(Shape(kind, "Theme")));
    }

    [Fact]
    public async Task A_shape_shows_its_words()
    {
        Assert.Contains("Wellness", await Render<ShapeNode>(Shape(ShapeKind.Ellipse, "Wellness")));
    }

    /// <summary>A shape's text is text, for the reason a note's is.</summary>
    [Fact]
    public async Task A_shape_never_renders_markup()
    {
        var raw = await RenderRaw<ShapeNode>(Shape(ShapeKind.Rectangle, "<b>bold</b><img src=x>"));

        Assert.DoesNotContain("<b>", raw);
        Assert.DoesNotContain("<img", raw);
    }

    /// <summary>
    /// A shape with nothing written in it is still a shape — the moodboard's bubbles are placed before
    /// they are named — so it draws rather than showing the note's "double-click to write" line.
    /// </summary>
    [Fact]
    public async Task An_empty_shape_still_draws()
    {
        var html = await Render<ShapeNode>(Shape(ShapeKind.Ellipse));

        Assert.Contains("bc-shape--ellipse", html);
    }

    [Fact]
    public async Task Editing_offers_the_words_and_the_kind()
    {
        var html = await Render<ShapeNode>(Shape(ShapeKind.Diamond, "Married"), editing: true);

        Assert.Contains("<textarea", html);
        Assert.Contains("aria-label=\"Shape text\"", html);
        Assert.Contains("<select", html);
        Assert.Contains("Diamond", html);
    }

    [Fact]
    public void Every_type_maps_to_its_own_renderer()
    {
        Assert.Equal(typeof(CardNode), BlockRendererMap.RendererFor(CanvasNodeType.Card));
        Assert.Equal(11, BlockRendererMap.All.Values.Distinct().Count());
    }

    private static async Task<string> RenderRaw<TRenderer>(CanvasNode node) where TRenderer : BlockRendererBase
    {
        // Without entity decoding, so escaped text cannot be mistaken for markup.
        await using var session = await RenderSession.StartAsync();
        return await session.RenderRawAsync<TRenderer>(new Dictionary<string, object?>
        {
            [nameof(BlockRendererBase.Node)] = node,
            [nameof(BlockRendererBase.Data)] = node.Data,
            [nameof(BlockRendererBase.Editing)] = false,
        });
    }
}

/// <summary>An edit session commits one undo step, and nothing when nothing changed.</summary>
public sealed class EditSessionTests
{
    [Fact]
    public async Task Opening_and_closing_an_edit_without_changes_records_nothing()
    {
        await using var session = await RenderSession.StartAsync();
        var store = session.Services.GetRequiredService<CanvasStore>();
        var selection = session.Services.GetRequiredService<SelectionState>();
        var node = TestBoards.Node(CanvasNodeType.Card);
        store.Load(new CanvasDocument { Nodes = [node] });
        await session.RenderAsync<CanvasBoard>();

        await session.InvokeAsync(() => { selection.BeginEdit(node.Id); return Task.CompletedTask; });
        Assert.Contains("bc-node--editing", await session.HtmlAsync());
        await session.InvokeAsync(() => { selection.EndEdit(); return Task.CompletedTask; });

        Assert.DoesNotContain("bc-node--editing", await session.HtmlAsync());
        Assert.False(store.CanUndo);
    }

    [Fact]
    public async Task Edit_mode_shows_the_editing_controls_inside_the_block()
    {
        await using var session = await RenderSession.StartAsync();
        var store = session.Services.GetRequiredService<CanvasStore>();
        var node = TestBoards.Node(CanvasNodeType.Map);
        store.Load(new CanvasDocument { Nodes = [node] });
        await session.RenderAsync<CanvasBoard>();
        await session.InvokeAsync(() => { session.Services.GetRequiredService<SelectionState>().BeginEdit(node.Id); return Task.CompletedTask; });
        Assert.Contains("Coordinates", await session.HtmlAsync());
    }
}

/// <summary>Connector and group sections of the properties panel.</summary>
public sealed class ConnectorAndGroupPropertiesTests
{
    [Fact]
    public async Task A_selected_connector_offers_label_colour_arrow_and_sides()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var edge = new CanvasEdge { FromNodeId = a.Id, ToNodeId = b.Id };
        var html = await RenderHelper.RenderWithBoardAsync<PropertiesPanel>(new CanvasDocument { Nodes = [a, b], Edges = [edge] },
            arrange: sp => sp.GetRequiredService<SelectionState>().SelectEdge(edge.Id));
        foreach (var id in new[] { "bc-prop-edge-label", "bc-prop-edge-arrow", "bc-prop-edge-from", "bc-prop-edge-to" })
            Assert.Contains($"for=\"{id}\"", html);
        Assert.Contains("Arrows at both ends", html);
        Assert.Contains("aria-label=\"Colour Red\"", html);
        Assert.DoesNotMatch(@"style=""[^""]*#[0-9a-fA-F]{3,6}", html);
    }

    [Fact]
    public async Task A_selected_group_offers_name_colour_and_ungroup()
    {
        var node = TestBoards.Node();
        var group = new CanvasGroup { Label = "Basement", Width = 400, Height = 300 };
        node.GroupId = group.Id;
        var html = await RenderHelper.RenderWithBoardAsync<PropertiesPanel>(new CanvasDocument { Nodes = [node], Groups = [group] },
            arrange: sp => sp.GetRequiredService<SelectionState>().SelectGroup(group.Id));
        Assert.Contains("value=\"Basement\"", html);
        Assert.Contains("Colour Green", html);
        Assert.Contains("Ungroup", html);
        Assert.Contains("1 item(s)", html);
    }
}
