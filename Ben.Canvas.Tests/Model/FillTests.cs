using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// Filling a block with its colour, and turning a group into a panel.
/// </summary>
/// <remarks>
/// <para><b>What these two fields are for.</b> Of the four boards Ben sent, the moodboard is coloured
/// section panels with an image collage, and the deck is slide-sized frames with coloured backgrounds.
/// Neither needs a new kind of block: both are what already exists, filled in rather than outlined.</para>
///
/// <para><b>A sticky is a fill, not a kind.</b> <c>TextNode</c> already calls itself "a sticky note of
/// plain text"; what it lacked was a background. Every block already carries a palette colour drawn as
/// a 3 px bar, so a fill turns that same colour into the background — and it works for a card or a
/// picture caption too, which a new StickyData never would.</para>
///
/// <para><b>A panel is a group fill.</b> <c>CanvasGroup</c> already has bounds, a label and a colour;
/// a panel is those drawn solid with the label as a title bar. That matters beyond looks:
/// <c>SlideOrder</c>'s first rule is that groups win, so a deck built from panels presents correctly
/// with nothing else built.</para>
///
/// <para><b>The default reproduces today exactly.</b> Every existing board must open unchanged, which
/// is why the enums start at the value that means "as it was" and the schema stays at 1.</para>
/// </remarks>
public sealed class FillTests
{
    // ── a block's fill ───────────────────────────────────────────────────

    [Fact]
    public void A_block_is_barred_unless_it_says_otherwise()
    {
        Assert.Equal(NodeFill.Bar, new CanvasNode().Fill);
    }

    /// <summary>
    /// A board written before the field existed must open exactly as it did. This is the whole reason
    /// the schema does not move.
    /// </summary>
    [Fact]
    public void A_board_with_no_fill_field_reads_as_bar()
    {
        var document = new CanvasDocument { Nodes = [TestBoards.Node(CanvasNodeType.Text)] };
        var json = CanvasSerializer.Serialize(document);

        // The property is removed entirely, which is what a board written before the field existed
        // looks like on disk.
        var stripped = System.Text.RegularExpressions.Regex.Replace(json, "\\s*\"fill\": \"[A-Za-z]+\",?", "");
        var (read, problem) = CanvasSerializer.Parse(stripped);

        Assert.True(problem is null, problem);
        Assert.Equal(NodeFill.Bar, read!.Nodes.Single().Fill);
    }

    [Theory]
    [InlineData(NodeFill.Bar)]
    [InlineData(NodeFill.Solid)]
    public void A_blocks_fill_round_trips(NodeFill fill)
    {
        var node = TestBoards.Node(CanvasNodeType.Text);
        node.Fill = fill;
        var document = new CanvasDocument { Nodes = [node] };

        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));

        Assert.True(problem is null, problem);
        Assert.Equal(fill, read!.Nodes.Single().Fill);
    }

    /// <summary>A fill this build does not know reads as the bar, for the shape style's reason.</summary>
    [Fact]
    public void An_unknown_block_fill_reads_as_bar()
    {
        var node = TestBoards.Node(CanvasNodeType.Card);
        node.Fill = NodeFill.Solid;
        var json = CanvasSerializer.Serialize(new CanvasDocument { Nodes = [node] })
            .Replace("\"Solid\"", "\"Gradient\"");

        var (read, problem) = CanvasSerializer.Parse(json);

        Assert.True(problem is null, problem);
        Assert.Equal(NodeFill.Bar, read!.Nodes.Single().Fill);
    }

    [Fact]
    public void A_fill_survives_a_clone_so_undo_keeps_it()
    {
        var node = TestBoards.Node(CanvasNodeType.Text);
        node.Fill = NodeFill.Solid;

        Assert.Equal(NodeFill.Solid, node.Clone().Fill);
    }

    /// <summary>
    /// The published picture has to show a filled block as filled, or a moodboard publishes as a page
    /// of white boxes.
    /// </summary>
    [Fact]
    public void A_filled_block_carries_its_fill_to_the_snapshot()
    {
        var node = TestBoards.Node(CanvasNodeType.Text);
        node.Fill = NodeFill.Solid;
        node.ColorKey = "3";

        var scene = BoardSnapshot.Build(new CanvasDocument { Nodes = [node] });

        Assert.True(scene.Blocks.Single().Filled);
        Assert.Equal("3", scene.Blocks.Single().ColorKey);
    }

    // ── a group's fill ───────────────────────────────────────────────────

    [Fact]
    public void A_group_is_outlined_unless_it_says_otherwise()
    {
        Assert.Equal(GroupFill.Outline, new CanvasGroup().Fill);
    }

    [Theory]
    [InlineData(GroupFill.Outline)]
    [InlineData(GroupFill.Panel)]
    public void A_groups_fill_round_trips(GroupFill fill)
    {
        var document = new CanvasDocument
        {
            Groups = [new CanvasGroup { Label = "physically", Width = 400, Height = 300, Fill = fill }],
        };

        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));

        Assert.True(problem is null, problem);
        Assert.Equal(fill, read!.Groups.Single().Fill);
    }

    [Fact]
    public void An_unknown_group_fill_reads_as_outline()
    {
        var json = CanvasSerializer.Serialize(new CanvasDocument
        {
            Groups = [new CanvasGroup { Width = 400, Height = 300, Fill = GroupFill.Panel }],
        }).Replace("\"Panel\"", "\"Frosted\"");

        var (read, problem) = CanvasSerializer.Parse(json);

        Assert.True(problem is null, problem);
        Assert.Equal(GroupFill.Outline, read!.Groups.Single().Fill);
    }

    /// <summary>A panel keeps its name: the moodboard's sections and the deck's slides are titled.</summary>
    [Fact]
    public void A_panel_group_keeps_its_label_as_a_title()
    {
        var document = new CanvasDocument
        {
            Groups = [new CanvasGroup { Label = "spiritually", Width = 400, Height = 300, Fill = GroupFill.Panel }],
        };

        var (read, _) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));

        Assert.Equal("spiritually", read!.Groups.Single().Label);
        Assert.Equal(GroupFill.Panel, read.Groups.Single().Fill);
    }

    [Fact]
    public void A_panel_group_carries_its_fill_to_the_snapshot()
    {
        var scene = BoardSnapshot.Build(new CanvasDocument
        {
            Groups = [new CanvasGroup { Label = "mentally", Width = 400, Height = 300, Fill = GroupFill.Panel, ColorKey = "2" }],
        });

        Assert.Equal(GroupFill.Panel, scene.Groups.Single().Fill);
        Assert.Equal("2", scene.Groups.Single().ColorKey);
    }

    [Fact]
    public void A_groups_fill_survives_a_clone()
    {
        var group = new CanvasGroup { Fill = GroupFill.Panel, Width = 400, Height = 300 };

        Assert.Equal(GroupFill.Panel, group.Clone().Fill);
    }
}
