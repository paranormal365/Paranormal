using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Walking a board a card at a time.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16: "presentation mode like miro where you can create the cards like slides." The
/// rules worth holding down here are the ones a room would notice: it starts where you were looking,
/// it never runs off either end, and it changes nothing — so somebody who may only read a board can
/// still be the one driving the meeting.
/// </remarks>
public sealed class PresentationStateTests
{
    private static (PresentationState Walk, Ben.Canvas.Core.Commands.CanvasStore Store, CanvasNode[] Cards) Board(int cards)
    {
        var nodes = Enumerable.Range(0, cards)
            .Select(i => TestBoards.Node(CanvasNodeType.Card, 0, i * 400))
            .ToArray();
        var store = TestBoards.StoreWith(nodes);
        return (new PresentationState(store), store, nodes);
    }

    [Fact]
    public void Nothing_is_running_until_it_is_started()
    {
        var (walk, _, _) = Board(2);

        Assert.False(walk.Active);
        Assert.Null(walk.Current);
        Assert.False(walk.HasNext);
        Assert.False(walk.HasPrevious);
    }

    [Fact]
    public void An_empty_board_refuses_to_start()
    {
        var walk = new PresentationState(TestBoards.Store());

        Assert.False(walk.Start());
        Assert.False(walk.Active);
    }

    [Fact]
    public void It_starts_at_the_first_card_and_walks_to_the_last()
    {
        var (walk, _, cards) = Board(3);

        Assert.True(walk.Start());
        Assert.Equal(cards[0].Id, walk.Current!.Id);
        Assert.False(walk.HasPrevious);

        Assert.True(walk.Next());
        Assert.Equal(cards[1].Id, walk.Current!.Id);

        Assert.True(walk.Next());
        Assert.Equal(cards[2].Id, walk.Current!.Id);
        Assert.False(walk.HasNext);

        // Off the end changes nothing rather than wrapping: a room reads a wrap as a mistake.
        Assert.False(walk.Next());
        Assert.Equal(cards[2].Id, walk.Current!.Id);
    }

    [Fact]
    public void It_does_not_run_off_the_front_either()
    {
        var (walk, _, cards) = Board(2);
        walk.Start();

        Assert.False(walk.Previous());
        Assert.Equal(cards[0].Id, walk.Current!.Id);
    }

    /// <summary>"Click a card, then present" has to start on the card that was clicked.</summary>
    [Fact]
    public void It_starts_on_the_card_it_was_given()
    {
        var (walk, _, cards) = Board(4);

        Assert.True(walk.Start(cards[2].Id));

        Assert.Equal(2, walk.Index);
        Assert.Equal(cards[2].Id, walk.Current!.Id);
    }

    [Fact]
    public void A_card_that_is_not_on_this_board_starts_at_the_beginning()
    {
        var (walk, _, cards) = Board(2);

        walk.Start(Guid.NewGuid());

        Assert.Equal(cards[0].Id, walk.Current!.Id);
    }

    /// <summary>On a board of groups the stops are groups, so a card starts the walk at the one holding it.</summary>
    [Fact]
    public void A_card_inside_a_group_starts_at_its_group()
    {
        var first = TestBoards.Node(CanvasNodeType.Card, 0, 0);
        var second = TestBoards.Node(CanvasNodeType.Card, 0, 900);
        var store = TestBoards.StoreWith(first, second);
        var early = new CanvasGroup { Label = "Early", X = -50, Y = -50, Width = 500, Height = 400 };
        var late = new CanvasGroup { Label = "Late", X = -50, Y = 850, Width = 500, Height = 400 };
        store.Document.Groups.AddRange([early, late]);
        second.GroupId = late.Id;

        var walk = new PresentationState(store);
        Assert.True(walk.Start(second.Id));

        Assert.True(walk.Current!.IsGroup);
        Assert.Equal(late.Id, walk.Current.Id);
    }

    [Fact]
    public void Stopping_clears_everything_and_is_safe_to_repeat()
    {
        var (walk, _, _) = Board(2);
        walk.Start();

        walk.Stop();
        walk.Stop();

        Assert.False(walk.Active);
        Assert.Null(walk.Current);
        Assert.Equal(0, walk.Count);
    }

    /// <summary>
    /// A walk touches nothing: no command, no undo entry, no edit.
    /// </summary>
    /// <remarks>
    /// The reason this matters is not tidiness. A board can be presented by somebody who may only
    /// read it, and the meeting where a case is talked through is exactly that room — so if any part
    /// of presenting wrote to the document, the feature would be refused where it is needed most.
    /// </remarks>
    [Fact]
    public void Presenting_changes_nothing_on_the_board()
    {
        var (walk, store, _) = Board(3);
        var before = Ben.Canvas.Core.Serialization.CanvasSerializer.Serialize(store.Document, compact: true);

        walk.Start();
        walk.Next();
        walk.Previous();
        walk.MoveTo(2);
        walk.Stop();

        Assert.Equal(before, Ben.Canvas.Core.Serialization.CanvasSerializer.Serialize(store.Document, compact: true));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void The_rectangle_follows_a_card_that_is_moved_mid_walk()
    {
        var (walk, store, cards) = Board(2);
        walk.Start();

        store.MoveNodes([cards[0].Id], 250, 125);

        var rect = walk.CurrentRect();
        Assert.NotNull(rect);
        Assert.Equal(cards[0].X - Ben.Canvas.Core.Presentation.SlideOrder.Padding, rect!.Value.X);
    }

    /// <summary>A card deleted while it is on screen has no rectangle, which is how the editor knows to stop.</summary>
    [Fact]
    public void A_card_deleted_mid_walk_has_no_rectangle()
    {
        var (walk, store, cards) = Board(2);
        walk.Start();

        store.RemoveNodes([cards[0].Id]);

        Assert.Null(walk.CurrentRect());
    }

    [Fact]
    public void Only_the_card_being_shown_is_on_stage()
    {
        var (walk, _, cards) = Board(2);
        walk.Start();

        Assert.True(walk.IsOnStage(cards[0]));
        Assert.False(walk.IsOnStage(cards[1]));
    }

    /// <summary>With nobody presenting, every card is on stage — so the class costs nothing the rest of the time.</summary>
    [Fact]
    public void Everything_is_on_stage_when_nobody_is_presenting()
    {
        var (walk, _, cards) = Board(2);

        Assert.All(cards, c => Assert.True(walk.IsOnStage(c)));
    }

    [Fact]
    public void A_group_being_shown_puts_its_own_cards_on_stage()
    {
        var inside = TestBoards.Node(CanvasNodeType.Card, 0, 0);
        var outside = TestBoards.Node(CanvasNodeType.Card, 0, 900);
        var store = TestBoards.StoreWith(inside, outside);
        var group = new CanvasGroup { Label = "One", X = -50, Y = -50, Width = 500, Height = 400 };
        store.Document.Groups.Add(group);
        inside.GroupId = group.Id;

        var walk = new PresentationState(store);
        walk.Start();

        Assert.True(walk.IsOnStage(inside));
        Assert.False(walk.IsOnStage(outside));
    }
}
