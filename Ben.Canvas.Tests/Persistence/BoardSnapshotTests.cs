using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;

namespace Ben.Canvas.Tests.Persistence;

/// <summary>
/// The published picture's scene (R11): every block whatever the board's size, a canvas Safari will draw, map boxes
/// as their address (R35), connectors with their arrowheads, and a card's filled fields as words.
/// </summary>
public sealed class BoardSnapshotTests
{
    private static CanvasNode Card(double x, double y, string title = "Card") =>
        new() { Type = CanvasNodeType.Card, X = x, Y = y, Width = 280, Height = 200, Data = new CardData { Title = title } };

    [Fact]
    public void A_board_bigger_than_the_screen_publishes_every_block()
    {
        var document = new CanvasDocument { Nodes = Enumerable.Range(0, 500).Select(i => Card(i % 25 * 400, i / 25 * 300, $"Card {i}")).ToList() };

        var scene = BoardSnapshot.Build(document);

        Assert.Equal(500, scene.Blocks.Count);
    }

    [Fact]
    public void A_huge_board_stays_within_the_long_edge_and_the_pixel_area_safari_draws()
    {
        var document = new CanvasDocument { Nodes = [Card(0, 0), Card(40_000, 25_000)] };

        var scene = BoardSnapshot.Build(document);

        Assert.True(Math.Max(scene.PixelWidth, scene.PixelHeight) <= BoardSnapshot.MaxLongEdge);
        Assert.True((long)scene.PixelWidth * scene.PixelHeight <= BoardSnapshot.MaxPixels);
    }

    [Fact]
    public void A_small_board_is_drawn_at_twice_the_size_with_a_margin()
    {
        var scene = BoardSnapshot.Build(new CanvasDocument { Nodes = [Card(100, 50)] });

        Assert.Equal(2, scene.Scale);
        Assert.Equal(100 - BoardSnapshot.Margin, scene.X);
        Assert.Equal((int)((280 + 2 * BoardSnapshot.Margin) * 2), scene.PixelWidth);
    }

    [Fact]
    public void A_map_box_is_its_address_and_coordinates_never_map_imagery()
    {
        var map = new CanvasNode { Type = CanvasNodeType.Map, Width = 360, Height = 260, Data = new MapData { Address = "Shelby Street Bridge, Nashville", Latitude = 36.1612, Longitude = -86.7717 } };

        var block = Assert.Single(BoardSnapshot.Build(new CanvasDocument { Nodes = [map] }).Blocks);

        Assert.Equal(["Shelby Street Bridge, Nashville", "36.16120, -86.77170"], block.Lines);
        Assert.Null(block.ImageUrl);
        Assert.Null(block.AssetId);
    }

    [Fact]
    public void A_card_lists_its_filled_fields_as_words()
    {
        var card = Card(0, 0, "Cold spot");
        ((CardData)card.Data).Fields["witnessName"] = "Sarah";
        ((CardData)card.Data).Fields["verified"] = "";

        var block = Assert.Single(BoardSnapshot.Build(new CanvasDocument { Nodes = [card] }).Blocks);

        Assert.Equal("Cold spot", block.Title);
        Assert.Equal(["Witness name: Sarah"], block.Lines);
    }

    [Theory]
    [InlineData(EdgeArrow.None, 0)]
    [InlineData(EdgeArrow.End, 1)]
    [InlineData(EdgeArrow.Both, 2)]
    public void A_connector_carries_one_head_per_arrow_end(EdgeArrow arrow, int heads)
    {
        var a = Card(0, 0);
        var b = Card(600, 0);
        var document = new CanvasDocument { Nodes = [a, b], Edges = [new CanvasEdge { FromNodeId = a.Id, ToNodeId = b.Id, Arrow = arrow, Label = "heard at 3am" }] };

        var connector = Assert.Single(BoardSnapshot.Build(document).Connectors);

        Assert.Equal(heads, connector.Heads.Count);
        Assert.Equal("heard at 3am", connector.Label);
        Assert.StartsWith("M 280 100 C", connector.Path);
    }

    [Fact]
    public void An_image_block_carries_its_picture_ids_for_the_editor_to_resolve()
    {
        var asset = Guid.NewGuid();
        var upload = Guid.NewGuid();
        var image = new CanvasNode { Type = CanvasNodeType.Image, Width = 320, Height = 240, Data = new ImageData { AssetId = asset, OpfsExt = ".png", UploadFileId = upload } };

        var block = Assert.Single(BoardSnapshot.Build(new CanvasDocument { Nodes = [image] }).Blocks);

        Assert.Equal((asset, ".png", upload), (block.AssetId, block.Ext, block.UploadFileId));
    }
    /// <summary>
    /// A shape's own shape reaches the published picture.
    /// </summary>
    /// <remarks>
    /// Every other block is drawn as a titled rounded rectangle, which is right for all of them but
    /// this one: a shape IS its outline. Without the kind travelling, a published moodboard's four
    /// theme bubbles came out as four grey rectangles and a published diamond came out as the same
    /// rectangle. Found by the templates walk on 2026-09-18; a picture is the only thing that could
    /// have found it.
    /// </remarks>
    [Theory]
    [InlineData(ShapeKind.Rectangle, "rectangle")]
    [InlineData(ShapeKind.Ellipse, "ellipse")]
    [InlineData(ShapeKind.Diamond, "diamond")]
    public void A_shape_block_carries_which_shape_it_is(ShapeKind kind, string expected)
    {
        var shape = new CanvasNode
        {
            Type = CanvasNodeType.Shape,
            Width = 160,
            Height = 160,
            Data = new ShapeData { Kind = kind, Text = "Why now" },
        };

        var block = Assert.Single(BoardSnapshot.Build(new CanvasDocument { Nodes = [shape] }).Blocks);

        Assert.Equal(expected, block.Shape);
        Assert.Equal("Why now", block.Title);
    }

    /// <summary>And nothing else claims to be a shape, or every block would be drawn as one.</summary>
    [Fact]
    public void Nothing_but_a_shape_carries_a_shape()
    {
        var document = new CanvasDocument
        {
            Nodes =
            [
                Card(0, 0),
                new CanvasNode { Type = CanvasNodeType.Text, Width = 220, Height = 120, Data = new TextData { Text = "note" } },
                new CanvasNode { Type = CanvasNodeType.Table, Width = 420, Height = 200, Data = new TableData { Rows = [["a", "b"], ["c", "d"]] } },
            ],
        };

        Assert.All(BoardSnapshot.Build(document).Blocks, b => Assert.Null(b.Shape));
    }
}
