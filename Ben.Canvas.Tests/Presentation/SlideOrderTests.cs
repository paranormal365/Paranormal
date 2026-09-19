using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Presentation;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Presentation;

/// <summary>
/// The order a board is walked through when somebody presents it.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16: "presentation mode like miro where you can create the cards like slides." The
/// cards are the slides, so the order has to come from the board itself. These are the three rules,
/// each one held down separately: groups win, then the arrows, then down the page.
/// </remarks>
public sealed class SlideOrderTests
{
    private static CanvasNode Card(double x, double y, string? title = null)
    {
        var node = TestBoards.Node(CanvasNodeType.Card, x, y);
        if (title is not null) ((CardData)node.Data).Title = title;
        return node;
    }

    private static CanvasDocument Board(IEnumerable<CanvasNode> nodes, IEnumerable<CanvasEdge>? edges = null,
                                       IEnumerable<CanvasGroup>? groups = null) =>
        new() { Nodes = [.. nodes], Edges = [.. edges ?? []], Groups = [.. groups ?? []] };

    private static CanvasEdge Joins(CanvasNode from, CanvasNode to) =>
        new() { FromNodeId = from.Id, ToNodeId = to.Id };

    private static string Name(CanvasNode n) => ((CardData)n.Data).Title;

    [Fact]
    public void An_empty_board_has_nothing_to_walk_through() =>
        Assert.Empty(SlideOrder.For(new CanvasDocument()));

    [Fact]
    public void Loose_cards_read_down_the_page_then_across()
    {
        var topLeft = Card(0, 0, "a");
        var topRight = Card(500, 0, "b");
        var lower = Card(100, 400, "c");

        var slides = SlideOrder.For(Board([lower, topRight, topLeft]), Name);

        Assert.Equal(["a", "b", "c"], slides.Select(s => s.Title));
    }

    /// <summary>
    /// The side-handle gesture writes a chain of joined cards; presenting reads it back as the order.
    /// That is the whole payoff of "create the cards like slides" — grow, grow, grow, then present.
    /// </summary>
    [Fact]
    public void A_chain_of_joined_cards_is_walked_from_its_start()
    {
        // Laid out so reading order would give exactly the wrong answer: the last card is highest.
        var first = Card(0, 300, "first");
        var second = Card(400, 200, "second");
        var third = Card(800, 0, "third");

        var slides = SlideOrder.For(Board([third, second, first], [Joins(first, second), Joins(second, third)]), Name);

        Assert.Equal(["first", "second", "third"], slides.Select(s => s.Title));
    }

    [Fact]
    public void A_chain_takes_its_place_from_where_it_starts()
    {
        var loose = Card(0, 0, "loose");
        var chainHead = Card(0, 600, "head");
        var chainTail = Card(400, 600, "tail");

        var slides = SlideOrder.For(Board([loose, chainHead, chainTail], [Joins(chainHead, chainTail)]), Name);

        Assert.Equal(["loose", "head", "tail"], slides.Select(s => s.Title));
    }

    /// <summary>A fork cannot be walked both ways, so it takes the one the board reads as first.</summary>
    [Fact]
    public void A_card_with_two_arrows_out_goes_to_the_higher_one()
    {
        var start = Card(0, 300, "start");
        var upper = Card(400, 100, "upper");
        var lower = Card(400, 500, "lower");

        var slides = SlideOrder.For(Board([start, lower, upper], [Joins(start, lower), Joins(start, upper)]), Name);

        Assert.Equal(["start", "upper", "lower"], slides.Select(s => s.Title));
    }

    /// <summary>Every card appears once, however the arrows are drawn.</summary>
    [Fact]
    public void A_ring_of_arrows_shows_each_card_once()
    {
        var a = Card(0, 0, "a");
        var b = Card(400, 0, "b");
        var c = Card(800, 0, "c");

        var slides = SlideOrder.For(Board([a, b, c], [Joins(a, b), Joins(b, c), Joins(c, a)]), Name);

        Assert.Equal(3, slides.Count);
        Assert.Equal(3, slides.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Groups_are_the_slides_when_a_board_has_any()
    {
        var inside = Card(60, 60, "a card");
        var lower = new CanvasGroup { Label = "Later", X = 0, Y = 900, Width = 600, Height = 400 };
        var upper = new CanvasGroup { Label = "First", X = 0, Y = 0, Width = 600, Height = 400 };

        var slides = SlideOrder.For(Board([inside], groups: [lower, upper]), Name);

        Assert.Equal(["First", "Later"], slides.Select(s => s.Title));
        Assert.All(slides, s => Assert.True(s.IsGroup));
    }

    [Fact]
    public void A_group_with_no_label_still_has_a_name_to_show() =>
        Assert.Equal("Group", Assert.Single(SlideOrder.For(
            Board([], groups: [new CanvasGroup { Width = 100, Height = 100 }]))).Title);

    /// <summary>A slide frames its card with room around it, or the card sits flush against the edge.</summary>
    [Fact]
    public void A_slide_leaves_room_around_what_it_shows()
    {
        var card = Card(100, 200);

        var slide = Assert.Single(SlideOrder.For(Board([card])));

        Assert.Equal(card.X - SlideOrder.Padding, slide.Rect.X);
        Assert.Equal(card.Y - SlideOrder.Padding, slide.Rect.Y);
        Assert.Equal(card.Width + 2 * SlideOrder.Padding, slide.Rect.Width);
        Assert.Equal(card.Height + 2 * SlideOrder.Padding, slide.Rect.Height);
    }

    /// <summary>A connector to a card that is no longer there cannot take the walk off the board.</summary>
    [Fact]
    public void An_arrow_to_a_card_that_is_gone_is_ignored()
    {
        var only = Card(0, 0, "only");
        var edges = new[] { new CanvasEdge { FromNodeId = only.Id, ToNodeId = Guid.NewGuid() } };

        var slides = SlideOrder.For(Board([only], edges), Name);

        Assert.Equal(["only"], slides.Select(s => s.Title));
    }

    [Fact]
    public void Without_a_namer_a_card_is_called_what_its_kind_is_called() =>
        Assert.Equal(Ben.Canvas.Core.Blocks.BlockRegistry.Get(CanvasNodeType.Card).DisplayName,
                     Assert.Single(SlideOrder.For(Board([Card(0, 0)]))).Title);
}
