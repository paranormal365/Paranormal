using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Templates;

namespace Ben.Canvas.Tests.Templates;

/// <summary>
/// The three things a hand-written board gets wrong silently, and cannot get wrong through the builder.
/// </summary>
/// <remarks>
/// These are the counterparts to <see cref="BoardTemplateTests"/>: those assert the finished boards are
/// sound, which they are BECAUSE of the rules below. Without these tests the palette and minimum-size
/// assertions over there could never fail through any template, and a guard that cannot fail is not a
/// guard — so the rule is pinned where it is enforced.
/// </remarks>
public sealed class BoardBuilderTests
{
    /// <summary>
    /// Runs a layout through a builder the way <c>BoardTemplates.Create</c> does. The builder's
    /// constructor and <c>Finish</c> are internal on purpose — a board is built from a template or not
    /// at all — and the tests see them through <c>InternalsVisibleTo</c>.
    /// </summary>
    private static CanvasDocument Built(Action<BoardBuilder> layout)
    {
        var builder = new BoardBuilder("Probe");
        layout(builder);
        return builder.Finish(caseId: null);
    }

    /// <summary>
    /// A raw colour is refused outright. A literal would be right in one theme and invisible in the
    /// other, which is exactly what the palette exists to prevent — so this fails at build time rather
    /// than shipping a board that looks fine to whoever wrote it.
    /// </summary>
    [Theory]
    [InlineData("#ff0000")]
    [InlineData("red")]
    [InlineData("7")]
    [InlineData("")]
    public void A_template_may_not_name_a_colour(string key)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Built(b => b.Note(0, 0, "", colorKey: key)));
    }

    [Fact]
    public void A_palette_key_is_kept()
    {
        Assert.Equal("3", Assert.Single(Built(b => b.Note(0, 0, "", colorKey: "3")).Nodes).ColorKey);
    }

    /// <summary>
    /// A block asked for smaller than its kind allows is floored, not shipped small: a block under its
    /// minimum jumps the first time somebody drags its corner.
    /// </summary>
    [Fact]
    public void A_block_is_never_placed_under_its_own_minimum()
    {
        var node = Assert.Single(Built(b => b.Node(CanvasNodeType.Card, 0, 0, new CardData(), 10, 10)).Nodes);
        var descriptor = BlockRegistry.Get(CanvasNodeType.Card);

        Assert.Equal(descriptor.MinWidth, node.Width);
        Assert.Equal(descriptor.MinHeight, node.Height);
    }

    [Fact]
    public void A_panel_is_never_placed_under_the_group_minimum()
    {
        var group = Assert.Single(Built(b => b.Group("Small", 0, 0, 10, 10)).Groups);

        Assert.Equal(BlockRegistry.GroupMinWidth, group.Width);
        Assert.Equal(BlockRegistry.GroupMinHeight, group.Height);
    }

    [Fact]
    public void A_block_with_no_size_asked_for_takes_its_kinds_own()
    {
        var node = Assert.Single(Built(b => b.Node(CanvasNodeType.Table, 0, 0, new TableData())).Nodes);
        var descriptor = BlockRegistry.Get(CanvasNodeType.Table);

        Assert.Equal(descriptor.DefaultWidth, node.Width);
        Assert.Equal(descriptor.DefaultHeight, node.Height);
    }

    /// <summary>
    /// Paint depths are handed out in the order things are placed, and the counter is left ahead of
    /// them, so the first block somebody adds lands on top rather than behind the template.
    /// </summary>
    [Fact]
    public void Paint_depths_run_in_placement_order_and_the_counter_follows()
    {
        var document = Built(b =>
        {
            var group = b.Group("Panel", 0, 0, 300, 200);
            b.Note(10, 10, "first", groupId: group);
            b.Note(10, 120, "second", groupId: group);
        });

        Assert.Equal([0], document.Groups.Select(g => g.Z));
        Assert.Equal([1, 2], document.Nodes.Select(n => n.Z));
        Assert.Equal(3, document.NextZ);
    }

    /// <summary>A panel is a panel unless a template says otherwise: an outline is the exception.</summary>
    [Fact]
    public void A_group_is_a_panel_by_default()
    {
        Assert.Equal(GroupFill.Panel, Assert.Single(Built(b => b.Group("P", 0, 0, 300, 200)).Groups).Fill);
        Assert.Equal(GroupFill.Outline,
            Assert.Single(Built(b => b.Group("P", 0, 0, 300, 200, fill: GroupFill.Outline)).Groups).Fill);
    }

    /// <summary>A connector remembers every style choice, or undo cannot put one back.</summary>
    [Fact]
    public void A_connector_keeps_the_style_it_was_given()
    {
        var document = Built(b =>
        {
            var from = b.Note(0, 0, "a");
            var to = b.Note(400, 0, "b");
            b.Edge(from, to, EdgeRoute.Elbow, EdgeLine.Dashed,
                fromMarker: EdgeMarker.Diamond, toMarker: EdgeMarker.Dot, label: "m.");
        });

        var edge = Assert.Single(document.Edges);

        Assert.Equal(EdgeRoute.Elbow, edge.Route);
        Assert.Equal(EdgeLine.Dashed, edge.Line);
        Assert.Equal(EdgeMarker.Diamond, edge.FromMarker);
        Assert.Equal(EdgeMarker.Dot, edge.ToMarker);
        Assert.Equal("m.", edge.Label);
    }
}
