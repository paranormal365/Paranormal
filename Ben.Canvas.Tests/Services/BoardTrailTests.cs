using Ben.Canvas.Editor.Services;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// The path across boards, which the header draws as breadcrumbs.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-18:</b> "Instead of a back button, what about creating breadcrumbs to
/// navigate." Walking the board that day found the Back button that preceded them never appearing at
/// all — every part of it was written and working, and nothing had subscribed to the event that
/// redraws the header. The path is now its own object so that its arithmetic has tests; the missing
/// subscription is held by <c>CanvasBoardLinkTests</c> in the browser, which is the only place it
/// could have been seen.</para>
/// </remarks>
public sealed class BoardTrailTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid C = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_fresh_path_is_empty_and_leads_nowhere()
    {
        var trail = new BoardTrail();

        Assert.Empty(trail.Stops);
        Assert.Null(trail.Last);
        Assert.Null(trail.GoTo(0));
    }

    [Fact]
    public void Leaving_a_board_records_where_you_were()
    {
        var trail = new BoardTrail();

        Assert.True(trail.Push(A, "Family tree"));

        Assert.Equal(["Family tree"], trail.Stops.Select(s => s.Title));
        Assert.Equal(A, trail.Last!.Value.ServerId);
    }

    /// <summary>
    /// A board that was never saved to the server has no id to come back to, so nothing is recorded —
    /// a crumb pointing at nothing would be a dead end in the middle of the path.
    /// </summary>
    [Theory]
    [InlineData(null)]
    public void A_board_with_no_server_copy_is_not_a_stop(Guid? id)
    {
        var trail = new BoardTrail();

        Assert.False(trail.Push(id, "Unsaved"));
        Assert.Empty(trail.Stops);
    }

    [Fact]
    public void An_empty_id_is_not_a_stop_either()
    {
        Assert.False(new BoardTrail().Push(Guid.Empty, "Nothing"));
    }

    [Fact]
    public void The_path_keeps_every_stop_in_the_order_they_were_left()
    {
        var trail = new BoardTrail();
        trail.Push(A, "Family tree");
        trail.Push(B, "Newspapers");

        Assert.Equal(["Family tree", "Newspapers"], trail.Stops.Select(s => s.Title));
    }

    /// <summary>
    /// A path is a WALK, not a hierarchy: going A to B and back to A records all three, because that
    /// is what happened and each Back has to undo one step of it.
    /// </summary>
    [Fact]
    public void Returning_to_a_board_the_long_way_round_is_still_a_stop()
    {
        var trail = new BoardTrail();
        trail.Push(A, "Family tree");
        trail.Push(B, "Newspapers");
        trail.Push(A, "Family tree");

        Assert.Equal(3, trail.Stops.Count);
    }

    [Fact]
    public void Going_to_a_stop_opens_it_and_drops_everything_after_it()
    {
        var trail = new BoardTrail();
        trail.Push(A, "Family tree");
        trail.Push(B, "Newspapers");
        trail.Push(C, "Deeds");

        var opened = trail.GoTo(1);

        Assert.Equal(B, opened!.Value.ServerId);
        Assert.Equal(["Family tree"], trail.Stops.Select(s => s.Title));
    }

    /// <summary>
    /// The stop returned to is dropped WITH what followed it: it becomes the board now open, so it is
    /// no longer somewhere to go back to. Leaving it would put the board you are on in its own path.
    /// </summary>
    [Fact]
    public void The_first_crumb_clears_the_path()
    {
        var trail = new BoardTrail();
        trail.Push(A, "Family tree");
        trail.Push(B, "Newspapers");

        Assert.Equal(A, trail.GoTo(0)!.Value.ServerId);
        Assert.Empty(trail.Stops);
    }

    /// <summary>Back is the last crumb, so the two agree by construction.</summary>
    [Fact]
    public void Back_and_the_last_crumb_are_the_same_stop()
    {
        var trail = new BoardTrail();
        trail.Push(A, "Family tree");
        trail.Push(B, "Newspapers");

        Assert.Equal(trail.Stops[^1], trail.Last!.Value);
        Assert.Equal(B, trail.GoTo(trail.Stops.Count - 1)!.Value.ServerId);
        Assert.Single(trail.Stops);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void A_stale_crumb_does_nothing(int index)
    {
        var trail = new BoardTrail();
        trail.Push(A, "Family tree");
        trail.Push(B, "Newspapers");

        Assert.Null(trail.GoTo(index));
        Assert.Equal(2, trail.Stops.Count);
    }

    [Fact]
    public void Clearing_says_whether_there_was_anything_to_clear()
    {
        var trail = new BoardTrail();

        Assert.False(trail.Clear());

        trail.Push(A, "Family tree");
        Assert.True(trail.Clear());
        Assert.Empty(trail.Stops);
    }
}
