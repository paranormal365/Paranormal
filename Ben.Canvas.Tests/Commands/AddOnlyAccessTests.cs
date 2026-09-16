using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Commands;

/// <summary>
/// Adding to somebody else's board: their pieces stay as they are.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16: somebody who may edit the case "can edit the board additively", and only the author, a group
/// administrator or a site administrator may "change existing pieces". The server refuses such a save outright; these
/// cover the screen's half, so the work is stopped before it is done rather than after.
/// </remarks>
public sealed class AddOnlyAccessTests
{
    [Fact]
    public void A_piece_somebody_else_put_there_cannot_be_moved_or_removed()
    {
        var store = new CanvasStore();
        var theirs = TestBoards.Node();
        store.AddNode(theirs);

        var access = new BoardAccess();
        access.SetAddOnly([theirs.Id]);
        store.MayChangePiece = access.MayChange;

        var refusals = 0;
        store.ChangeRefused += () => refusals++;

        Assert.False(store.MoveNodes([theirs.Id], 40, 0));
        Assert.False(store.RemoveNodes([theirs.Id]));

        Assert.Equal(2, refusals);
        Assert.Single(store.Document.Nodes);
        Assert.Equal(0, store.Document.Nodes[0].X);
    }

    [Fact]
    public void A_piece_added_in_this_session_is_theirs_to_move()
    {
        var store = new CanvasStore();
        var theirs = TestBoards.Node();
        store.AddNode(theirs);

        var access = new BoardAccess();
        access.SetAddOnly([theirs.Id]);
        store.MayChangePiece = access.MayChange;

        var mine = TestBoards.Node();
        Assert.True(store.AddNode(mine));
        Assert.True(store.MoveNodes([mine.Id], 25, 5));

        Assert.Equal(25, store.Document.Nodes.Single(n => n.Id == mine.Id).X);
    }

    [Fact]
    public void A_board_they_may_edit_outright_answers_yes_to_everything()
    {
        var store = new CanvasStore();
        var node = TestBoards.Node();
        store.AddNode(node);

        var access = new BoardAccess();
        access.SetAddOnly([node.Id]);
        access.Set(canEdit: true);          // the server said full access after all
        store.MayChangePiece = access.MayChange;

        Assert.True(store.MoveNodes([node.Id], 12, 0));
        Assert.False(access.AddOnly);
    }

    [Fact]
    public void A_view_only_board_changes_nothing()
    {
        var access = new BoardAccess();
        access.Set(canEdit: false);

        Assert.False(access.MayChange(Guid.NewGuid()));
        Assert.NotNull(access.Reason);
    }
}
