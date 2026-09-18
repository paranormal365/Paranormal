using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// A shape: a rectangle, an ellipse or a diamond with words in the middle of it.
/// </summary>
/// <remarks>
/// <para><b>What it is for.</b> Two of the four boards Ben sent are built from shapes rather than
/// cards — the moodboard's circle "theme" bubbles, and the family tree's diamond marriage markers. A
/// card is a thing with fields; a shape is a thing with a colour and a word.</para>
///
/// <para><b>Three kinds and no more.</b> Rectangle, ellipse, diamond covers every shape in the four
/// boards. A shape library is a different product, and the moment this enum starts carrying stars and
/// arrows it stops being a board piece and starts being a drawing tool.</para>
///
/// <para><b>An unknown kind reads as a rectangle</b> rather than refusing the board. A shape is
/// decoration: a board that will not open because a newer editor wrote "star" would be a much worse
/// outcome than a star drawn as a box.</para>
/// </remarks>
public sealed class ShapeDataTests
{
    [Fact]
    public void A_new_shape_is_an_empty_rectangle()
    {
        var shape = (ShapeData)BlockRegistry.Get(CanvasNodeType.Shape).CreateDefaultData(DateTime.UtcNow);

        Assert.Equal(ShapeKind.Rectangle, shape.Kind);
        Assert.Equal("", shape.Text);
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Diamond)]
    public void A_shape_round_trips_its_kind_and_its_words(ShapeKind kind)
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Shape);
        node.Data = new ShapeData { Kind = kind, Text = "Wellness" };
        document.Nodes.Add(node);

        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));

        Assert.True(problem is null, problem);
        var shape = Assert.IsType<ShapeData>(read!.Nodes.Single().Data);
        Assert.Equal(kind, shape.Kind);
        Assert.Equal("Wellness", shape.Text);
    }

    /// <summary>
    /// Its discriminator is part of the file format; a rename makes every board carrying a shape
    /// unreadable.
    /// </summary>
    [Fact]
    public void Its_kind_is_written_as_shape()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Shape);
        node.Data = new ShapeData { Text = "Theme" };
        document.Nodes.Add(node);

        Assert.Contains("\"kind\": \"shape\"", CanvasSerializer.Serialize(document));
    }

    /// <summary>
    /// The shape kind is written by name, so inserting a value into the enum later cannot silently
    /// turn every diamond on every board into an ellipse.
    /// </summary>
    [Fact]
    public void The_shape_kind_is_written_by_name_not_by_number()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Shape);
        node.Data = new ShapeData { Kind = ShapeKind.Diamond };
        document.Nodes.Add(node);

        Assert.Contains("\"Diamond\"", CanvasSerializer.Serialize(document));
    }

    [Fact]
    public void Clone_is_deep_enough_to_keep_history_honest()
    {
        var original = new ShapeData { Kind = ShapeKind.Ellipse, Text = "Simplicity" };

        var copy = (ShapeData)original.Clone();
        copy.Text = "changed";
        copy.Kind = ShapeKind.Diamond;

        Assert.Equal("Simplicity", original.Text);
        Assert.Equal(ShapeKind.Ellipse, original.Kind);
    }

    /// <summary>
    /// A board from a newer editor opens, with the shape it does not know drawn as a box. Refusing the
    /// whole board over a decoration would be the worse trade.
    /// </summary>
    [Fact]
    public void An_unknown_shape_kind_reads_as_a_rectangle()
    {
        var json = CanvasSerializer.Serialize(new CanvasDocument
        {
            Nodes = [new CanvasNode { Type = CanvasNodeType.Shape, Data = new ShapeData { Text = "x" } }],
        }).Replace("\"Rectangle\"", "\"Star\"");

        var (read, problem) = CanvasSerializer.Parse(json);

        Assert.True(problem is null, problem);
        Assert.Equal(ShapeKind.Rectangle, ((ShapeData)read!.Nodes.Single().Data).Kind);
    }
}
