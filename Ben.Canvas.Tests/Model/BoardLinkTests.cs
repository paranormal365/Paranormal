using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// A card that names another board on the case, and optionally one card on it.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-18:</b> "like the add file from case, we have add research note from case
/// where you could drop in a link to another page of notes… a family tree in one page and in another
/// page create all news articles and create a link to open the other page to one of the cards… and a
/// back button to go back."</para>
///
/// <para><b>It carries an id and a remembered title, and nothing else.</b> Not a copy of the target's
/// contents, not a preview — because only a PUBLISHED board may be linked, and a card that cached
/// anything would be a way to read a draft through a published board. The title is what it was when
/// picked, so a card still reads sensibly when the target has been renamed or deleted.</para>
///
/// <para><b>A missing card is not an error.</b> Ben: "if there is a link to a card in a different
/// page, and the card has been removed, default to opening the other page and not focusing in on that
/// card." So the target block id is a hint, resolved when the link is followed, and nothing on this
/// board changes when a card disappears from another one.</para>
/// </remarks>
public sealed class BoardLinkTests
{
    private static CanvasNode Board(Action<BoardData>? set = null)
    {
        var node = TestBoards.Node(CanvasNodeType.Board);
        var data = (BoardData)node.Data;
        set?.Invoke(data);
        return node;
    }

    private static BoardData RoundTrip(Action<BoardData> set)
    {
        var node = Board(set);
        var (read, problem) = CanvasSerializer.Parse(
            CanvasSerializer.Serialize(new CanvasDocument { Nodes = [node] }));
        Assert.True(problem is null, problem);
        return (BoardData)read!.Nodes.Single().Data;
    }

    [Fact]
    public void A_new_board_card_names_nothing_yet()
    {
        var data = (BoardData)BlockRegistry.Get(CanvasNodeType.Board).CreateDefaultData(DateTime.UtcNow);

        Assert.Equal(Guid.Empty, data.DocumentId);
        Assert.Null(data.NodeId);
    }

    [Fact]
    public void Its_kind_is_written_as_board()
    {
        Assert.Contains("\"kind\": \"board\"",
            CanvasSerializer.Serialize(new CanvasDocument { Nodes = [Board()] }));
    }

    [Fact]
    public void A_board_card_round_trips_the_board_it_names()
    {
        var target = Guid.NewGuid();

        var data = RoundTrip(d => { d.DocumentId = target; d.Title = "Previous owners"; });

        Assert.Equal(target, data.DocumentId);
        Assert.Equal("Previous owners", data.Title);
    }

    [Fact]
    public void A_board_card_can_name_one_card_on_that_board()
    {
        var board = Guid.NewGuid();
        var card = Guid.NewGuid();

        var data = RoundTrip(d =>
        {
            d.DocumentId = board;
            d.Title = "News articles";
            d.NodeId = card;
            d.NodeTitle = "The 1974 fire";
        });

        Assert.Equal(card, data.NodeId);
        Assert.Equal("The 1974 fire", data.NodeTitle);
    }

    /// <summary>
    /// The whole board is a real choice, and the picker offers it first. Null means "the whole board",
    /// which is why the field is nullable rather than defaulted to something.
    /// </summary>
    [Fact]
    public void Naming_no_card_means_the_whole_board()
    {
        Assert.Null(RoundTrip(d => d.DocumentId = Guid.NewGuid()).NodeId);
    }

    /// <summary>
    /// A card carries no content of its target. This is the rule that makes the published-only
    /// invariant worth anything: if a card cached the target's words, a published board could leak a
    /// draft's contents even with every gate in place.
    /// </summary>
    [Fact]
    public void A_board_card_carries_no_content_of_its_target()
    {
        var properties = typeof(BoardData).GetProperties()
            .Where(p => p.DeclaringType == typeof(BoardData))
            .Select(p => p.Name)
            .Order()
            .ToList();

        // Four fields, and a new one has to be argued for here before it is added.
        Assert.Equal(["DocumentId", "NodeId", "NodeTitle", "Title"], properties);
    }

    /// <summary>Titles are clamped, because a renamed board can be called anything.</summary>
    [Fact]
    public void Remembered_titles_are_clamped_on_read()
    {
        var data = RoundTrip(d =>
        {
            d.DocumentId = Guid.NewGuid();
            d.Title = new string('x', 500);
            d.NodeTitle = new string('y', 500);
        });

        Assert.Equal(BoardData.MaxTitleLength, data.Title.Length);
        Assert.Equal(BoardData.MaxTitleLength, data.NodeTitle!.Length);
    }

    [Fact]
    public void A_blank_card_title_reads_as_nothing()
    {
        Assert.Null(RoundTrip(d => { d.DocumentId = Guid.NewGuid(); d.NodeTitle = "  "; }).NodeTitle);
    }

    [Fact]
    public void Clone_is_deep_enough_for_undo()
    {
        var original = new BoardData { DocumentId = Guid.NewGuid(), Title = "Tree", NodeId = Guid.NewGuid() };

        var copy = (BoardData)original.Clone();
        copy.Title = "changed";
        copy.NodeId = null;

        Assert.Equal("Tree", original.Title);
        Assert.NotNull(original.NodeId);
    }

    /// <summary>
    /// The published picture shows the board it points at, so a printed deck still says where a link
    /// went — and names the card when it names one.
    /// </summary>
    [Fact]
    public void The_snapshot_says_which_board_it_points_at()
    {
        var node = Board(d => { d.DocumentId = Guid.NewGuid(); d.Title = "News articles"; d.NodeTitle = "The 1974 fire"; });

        var scene = Core.Persistence.BoardSnapshot.Build(new CanvasDocument { Nodes = [node] });

        Assert.Equal("News articles", scene.Blocks.Single().Title);
        Assert.Contains("The 1974 fire", scene.Blocks.Single().Lines);
    }
}
