using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// One serializer reads everything the editor writes, and refuses everything else with a sentence.
/// </summary>
public sealed class CanvasSerializerTests
{
    private static CanvasDocument FullBoard()
    {
        var document = new CanvasDocument { Title = "Porch hunt" };
        foreach (var type in Enum.GetValues<CanvasNodeType>())
            document.Nodes.Add(TestBoards.Node(type, x: document.Nodes.Count * 400));
        document.Edges.Add(new CanvasEdge { FromNodeId = document.Nodes[0].Id, ToNodeId = document.Nodes[1].Id, Label = "heard at 3am" });
        document.Groups.Add(new CanvasGroup { Label = "Basement", Width = 400, Height = 300 });
        document.NextZ = 10;
        return document;
    }

    [Fact]
    public void Enums_are_written_as_names_so_inserting_a_value_cannot_shift_them()
    {
        var document = new CanvasDocument();
        document.Nodes.Add(TestBoards.Node(CanvasNodeType.Map));
        document.Nodes.Add(TestBoards.Node(CanvasNodeType.Link));
        var json = CanvasSerializer.Serialize(document);
        Assert.Contains("\"type\": \"Map\"", json);
        Assert.Contains("\"tier\": \"None\"", json);
    }

    [Fact]
    public void A_document_this_writes_is_a_document_this_reads()
    {
        var original = FullBoard();
        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(original));
        Assert.Null(problem);
        Assert.NotNull(read);
        Assert.Equal(original.Id, read.Id);
        Assert.Equal(7, read.Nodes.Count);
        Assert.Equal("heard at 3am", Assert.Single(read.Edges).Label);
        Assert.Equal("Basement", Assert.Single(read.Groups).Label);
    }

    [Theory]
    [MemberData(nameof(NodeDataTests.AllTypes), MemberType = typeof(NodeDataTests))]
    public void Every_node_kind_round_trips(CanvasNodeType type)
    {
        var document = new CanvasDocument();
        document.Nodes.Add(TestBoards.Node(type));
        var read = CanvasSerializer.Parse(CanvasSerializer.Serialize(document, compact: true)).Document!;
        Assert.Equal(type, read.Nodes[0].Type);
        Assert.Equal(document.Nodes[0].Data.GetType(), read.Nodes[0].Data.GetType());
    }

    [Fact]
    public void Files_written_with_different_property_case_still_read()
    {
        var json = CanvasSerializer.Serialize(FullBoard()).Replace("\"nodes\"", "\"Nodes\"").Replace("\"title\"", "\"TITLE\"");
        var (read, problem) = CanvasSerializer.Parse(json);
        Assert.Null(problem);
        Assert.Equal("Porch hunt", read!.Title);
        Assert.Equal(7, read.Nodes.Count);
    }

    [Theory]
    [InlineData("{\"hello\":\"world\"}")]
    [InlineData("{\"nodes\":[]}")]
    [InlineData("[]")]
    public void Json_that_is_not_a_board_is_refused(string json)
    {
        var (read, problem) = CanvasSerializer.Parse(json);
        Assert.Null(read);
        Assert.Equal("That file is valid JSON, but it is not a canvas board.", problem);
    }

    [Fact]
    public void An_empty_file_says_it_is_empty() => Assert.Equal("That file is empty.", CanvasSerializer.Parse("  ").Problem);

    [Fact]
    public void Something_that_is_not_json_is_refused_with_a_reason()
    {
        var (read, problem) = CanvasSerializer.Parse("this is a shopping list");
        Assert.Null(read);
        Assert.StartsWith("That file is not readable as a board:", problem);
        Assert.EndsWith(".", problem);
    }

    [Fact]
    public void A_board_from_a_newer_editor_is_refused_and_says_so()
    {
        var json = CanvasSerializer.Serialize(FullBoard()).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 9");
        var (read, problem) = CanvasSerializer.Parse(json);
        Assert.Null(read);
        Assert.Contains("newer version", problem);
        Assert.Contains("format 9", problem);
    }

    [Fact]
    public void A_real_board_parses_and_is_stamped_with_the_current_format()
    {
        var json = CanvasSerializer.Serialize(FullBoard()).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 0");
        var (read, _) = CanvasSerializer.Parse(json);
        Assert.Equal(CanvasDocument.CurrentSchemaVersion, read!.SchemaVersion);
    }

    [Fact]
    public void Unknown_node_kind_is_refused_not_silently_blanked()
    {
        var json = CanvasSerializer.Serialize(FullBoard()).Replace("\"kind\": \"text\"", "\"kind\": \"sticker\"");
        var (read, problem) = CanvasSerializer.Parse(json);
        Assert.Null(read);
        Assert.NotNull(problem);
    }

    [Fact]
    public void Null_is_omitted_from_output()
    {
        var json = CanvasSerializer.Serialize(new CanvasDocument());
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Compact_output_has_no_indentation()
    {
        var json = CanvasSerializer.Serialize(FullBoard(), compact: true);
        Assert.DoesNotContain("\n", json);
        Assert.NotNull(CanvasSerializer.Parse(json).Document);
    }

    [Fact]
    public void Coordinates_round_trip_under_de_DE_culture() => TestBoards.InCulture("de-DE", () =>
    {
        var document = new CanvasDocument();
        document.Nodes.Add(TestBoards.Node(CanvasNodeType.Card, x: 12.5, y: -3.25));
        var json = CanvasSerializer.Serialize(document);
        Assert.Contains("12.5", json);
        var read = CanvasSerializer.Parse(json).Document!;
        Assert.Equal(12.5, read.Nodes[0].X);
        Assert.Equal(-3.25, read.Nodes[0].Y);
    });
}

/// <summary>Every read brings a board up to what this build expects, safely and idempotently.</summary>
public sealed class CanvasDocumentMigrationsTests
{
    [Fact]
    public void An_edge_to_a_missing_node_is_dropped()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node();
        document.Nodes.Add(node);
        document.Edges.Add(new CanvasEdge { FromNodeId = node.Id, ToNodeId = Guid.NewGuid() });
        Assert.Empty(CanvasDocumentMigrations.Upgrade(document).Edges);
    }

    [Fact]
    public void A_stale_group_id_is_cleared()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node();
        node.GroupId = Guid.NewGuid();
        document.Nodes.Add(node);
        Assert.Null(CanvasDocumentMigrations.Upgrade(document).Nodes[0].GroupId);
    }

    [Fact]
    public void NextZ_is_raised_above_every_existing_z()
    {
        var document = new CanvasDocument { NextZ = 0 };
        var node = TestBoards.Node();
        node.Z = 41;
        document.Nodes.Add(node);
        document.Groups.Add(new CanvasGroup { Z = 7, Width = 400, Height = 300 });
        Assert.Equal(42, CanvasDocumentMigrations.Upgrade(document).NextZ);
    }

    [Fact]
    public void The_version_is_stamped_once()
    {
        var document = new CanvasDocument { SchemaVersion = 0 };
        Assert.Equal(1, CanvasDocumentMigrations.Upgrade(document).SchemaVersion);
    }

    [Fact]
    public void Upgrade_is_idempotent()
    {
        var document = new CanvasDocument();
        document.Nodes.Add(TestBoards.Node(CanvasNodeType.Message));
        ((MessageData)document.Nodes[0].Data).Html = "<b>Hi<img src=x onerror=alert(1)>";
        document.Nodes.Add(TestBoards.Node(CanvasNodeType.Text, width: 3, height: 3));
        var once = CanvasSerializer.Serialize(CanvasDocumentMigrations.Upgrade(document));
        var twice = CanvasSerializer.Serialize(CanvasDocumentMigrations.Upgrade(CanvasDocumentMigrations.Upgrade(document)));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void A_zero_size_node_gets_its_block_minimum()
    {
        var document = new CanvasDocument();
        document.Nodes.Add(TestBoards.Node(CanvasNodeType.Map, width: 0, height: 0));
        var node = CanvasDocumentMigrations.Upgrade(document).Nodes[0];
        Assert.True(node.Width >= BlockRegistry.Get(CanvasNodeType.Map).MinWidth);
        Assert.True(node.Height >= BlockRegistry.Get(CanvasNodeType.Map).MinHeight);
    }

    [Fact]
    public void A_small_group_is_raised_to_its_minimum()
    {
        var document = new CanvasDocument();
        document.Groups.Add(new CanvasGroup { Width = 10, Height = 10 });
        var group = CanvasDocumentMigrations.Upgrade(document).Groups[0];
        Assert.Equal(300, group.Width);
        Assert.Equal(200, group.Height);
    }

    [Fact]
    public void An_invalid_colour_key_is_cleared()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node();
        node.ColorKey = "#ff0000";
        document.Nodes.Add(node);
        Assert.Null(CanvasDocumentMigrations.Upgrade(document).Nodes[0].ColorKey);
    }

    [Fact]
    public void Null_lists_become_empty()
    {
        var document = new CanvasDocument { Nodes = null!, Edges = null!, Groups = null! };
        var upgraded = CanvasDocumentMigrations.Upgrade(document);
        Assert.NotNull(upgraded.Nodes);
        Assert.NotNull(upgraded.Edges);
        Assert.NotNull(upgraded.Groups);
    }

    [Fact]
    public void An_imported_message_loses_its_script_handlers()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Message);
        ((MessageData)node.Data).Html = "<p onclick=\"steal()\">Seen <b>here</b></p><img src=x onerror=alert(1)><script>alert(2)</script>";
        document.Nodes.Add(node);

        var json = CanvasSerializer.Serialize(document);
        var html = ((MessageData)CanvasSerializer.Parse(json).Document!.Nodes[0].Data).Html;

        Assert.Equal("<p>Seen <b>here</b></p>", html);
    }

    [Fact]
    public void A_javascript_link_address_is_dropped()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Link);
        ((LinkData)node.Data).Url = "javascript:alert(1)";
        ((LinkData)node.Data).ImageSourceUrl = "http://example.com/a.png";
        document.Nodes.Add(node);

        var link = (LinkData)CanvasDocumentMigrations.Upgrade(document).Nodes[0].Data;
        Assert.Equal("", link.Url);
        Assert.Null(link.ImageSourceUrl);
    }
}
