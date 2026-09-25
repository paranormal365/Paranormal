using Ben.Canvas.Core.Templates;

namespace Ben.Canvas.Tests.Templates;

/// <summary>
/// Which of the three ways of arriving at the editor wins.
/// </summary>
/// <remarks>
/// A link can ask for a board by id, ask for a new board from a template, or ask for nothing — and this
/// device may separately have a board it had open last. All of those can be true at once, so the order
/// is a rule rather than an accident of the startup sequence, and it lives in one function so it can be
/// read back here instead of needing a browser to run.
/// </remarks>
public sealed class BoardOpenPolicyTests
{
    /// <summary>
    /// A template is something somebody clicked a second ago; the device copy is whatever they happened
    /// to leave open, possibly days back. If the older thing won, "New board from a template" would do
    /// nothing visible.
    /// </summary>
    [Fact]
    public void A_template_request_wins_over_the_last_open_board()
    {
        var plan = BoardOpenPolicy.For(documentId: null, templateId: "deck");

        Assert.Equal("deck", plan.Template);
        Assert.False(plan.RestoreDevice);
        Assert.Null(plan.SayUnknownTemplate);
    }

    /// <summary>
    /// And a board asked for by id beats the template, silently: the person got the board they clicked,
    /// and a warning about a template they never thought about would only puzzle them.
    /// </summary>
    [Fact]
    public void A_template_is_ignored_when_a_board_id_arrives()
    {
        var plan = BoardOpenPolicy.For(Guid.NewGuid(), "deck");

        Assert.Null(plan.Template);
        Assert.True(plan.RestoreDevice);
        Assert.Null(plan.SayUnknownTemplate);
    }

    /// <summary>
    /// A stale link can name a template this build dropped. Reopening their last board instead would
    /// look like the click did nothing, so it is still a NEW board — blank, with a sentence.
    /// </summary>
    [Fact]
    public void An_unknown_template_opens_a_blank_board_and_says_so()
    {
        var plan = BoardOpenPolicy.For(null, "moodboard-v2");

        Assert.Equal("moodboard-v2", plan.Template);
        Assert.False(plan.RestoreDevice);
        Assert.Equal("moodboard-v2", plan.SayUnknownTemplate);
        Assert.Empty(BoardTemplates.Create(plan.Template, null).Nodes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_template_asked_for_the_last_open_board_comes_back(string? id)
    {
        var plan = BoardOpenPolicy.For(null, id);

        Assert.True(plan.RestoreDevice);
        Assert.Null(plan.Template);
        Assert.Null(plan.SayUnknownTemplate);
    }

    /// <summary>A padded fragment names the template it looks like it names.</summary>
    [Fact]
    public void Surrounding_space_in_the_id_is_ignored()
    {
        var plan = BoardOpenPolicy.For(null, "  family-tree  ");

        Assert.Equal("family-tree", plan.Template);
        Assert.Null(plan.SayUnknownTemplate);
    }

    /// <summary>
    /// Blank is a template like any other: asking for it is a deliberate "start me an empty board", so
    /// it still beats the device copy rather than quietly reopening the last board.
    /// </summary>
    [Fact]
    public void Asking_for_blank_is_still_a_new_board()
    {
        var plan = BoardOpenPolicy.For(null, BoardTemplates.BlankId);

        Assert.Equal(BoardTemplates.BlankId, plan.Template);
        Assert.False(plan.RestoreDevice);
        Assert.Null(plan.SayUnknownTemplate);
    }
}
