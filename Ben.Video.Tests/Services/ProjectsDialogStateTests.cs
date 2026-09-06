using Ben.Video.Core.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The Projects window's rename box, its delete confirmation and its error line.
/// </summary>
/// <remarks>
/// Deleting a saved project is a localStorage removal, not a store command, so there is no undo
/// waiting behind a misclick. Whether a delete can happen without a confirmation is a question
/// worth being able to ask without clicking one.
/// </remarks>
public sealed class ProjectsDialogStateTests
{
    private static readonly Guid RowA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RowB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_fresh_window_is_neither_renaming_nor_asking()
    {
        var state = new ProjectsDialogState();

        Assert.Null(state.EditingId);
        Assert.Null(state.ConfirmingDeleteId);
        Assert.Null(state.Error);
    }

    // ── Renaming ──────────────────────────────────────────────────────────────

    [Fact]
    public void Renaming_starts_from_the_name_the_project_already_has()
    {
        var state = new ProjectsDialogState();

        state.StartRename(RowA, "Basement EVP");

        Assert.True(state.IsEditing(RowA));
        Assert.False(state.IsEditing(RowB));
        Assert.Equal("Basement EVP", state.EditingName);
    }

    [Fact]
    public void Committing_says_which_project_to_rename_to_what()
    {
        var state = new ProjectsDialogState();
        state.StartRename(RowA, "Basement EVP");
        state.EditingName = "Basement EVP, take 2";

        var rename = state.CommitRename();

        Assert.Equal(RowA, rename!.Value.Id);
        Assert.Equal("Basement EVP, take 2", rename.Value.Name);
        Assert.Null(state.EditingId);
    }

    /// <summary>
    /// A project with no name cannot be found again, so a blank box does nothing rather than
    /// erasing the name.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public void A_blank_name_renames_nothing(string typed)
    {
        var state = new ProjectsDialogState();
        state.StartRename(RowA, "Basement EVP");
        state.EditingName = typed;

        Assert.Null(state.CommitRename());
        Assert.Null(state.EditingId);
    }

    [Fact]
    public void A_pasted_name_is_trimmed()
    {
        var state = new ProjectsDialogState();
        state.StartRename(RowA, "Basement EVP");
        state.EditingName = "  Attic  ";

        Assert.Equal("Attic", state.CommitRename()!.Value.Name);
    }

    [Fact]
    public void Committing_when_nothing_is_being_edited_renames_nothing() =>
        Assert.Null(new ProjectsDialogState().CommitRename());

    [Fact]
    public void Escaping_leaves_the_name_as_it_was()
    {
        var state = new ProjectsDialogState();
        state.StartRename(RowA, "Basement EVP");
        state.EditingName = "something else";

        state.CancelRename();

        Assert.Null(state.EditingId);
        Assert.Null(state.CommitRename());
    }

    // ── Deleting ──────────────────────────────────────────────────────────────

    [Fact]
    public void Deleting_asks_first()
    {
        var state = new ProjectsDialogState();

        state.AskToDelete(RowA);

        Assert.True(state.IsConfirmingDelete(RowA));
        Assert.False(state.IsConfirmingDelete(RowB));
    }

    [Fact]
    public void Confirming_the_row_that_was_asked_about_deletes_it()
    {
        var state = new ProjectsDialogState();
        state.AskToDelete(RowA);

        Assert.True(state.ConfirmDelete(RowA));
        Assert.Null(state.ConfirmingDeleteId);
    }

    /// <summary>
    /// The one that matters: nothing is deleted without a confirmation, however the call arrives.
    /// </summary>
    [Fact]
    public void A_delete_that_was_never_asked_about_does_not_happen() =>
        Assert.False(new ProjectsDialogState().ConfirmDelete(RowA));

    /// <summary>
    /// The list re-renders under the confirmation, so a click has to name the row it thought it
    /// was confirming.
    /// </summary>
    [Fact]
    public void Confirming_a_different_row_deletes_neither()
    {
        var state = new ProjectsDialogState();
        state.AskToDelete(RowA);

        Assert.False(state.ConfirmDelete(RowB));
        Assert.True(state.IsConfirmingDelete(RowA));
    }

    [Fact]
    public void Saying_no_keeps_the_project()
    {
        var state = new ProjectsDialogState();
        state.AskToDelete(RowA);

        state.CancelDelete();

        Assert.Null(state.ConfirmingDeleteId);
        Assert.False(state.ConfirmDelete(RowA));
    }

    /// <summary>
    /// Two open questions in one list is how somebody answers the wrong one.
    /// </summary>
    [Fact]
    public void Only_one_question_is_open_at_a_time()
    {
        var state = new ProjectsDialogState();

        state.AskToDelete(RowA);
        state.StartRename(RowB, "Attic");
        Assert.Null(state.ConfirmingDeleteId);

        state.AskToDelete(RowA);
        Assert.Null(state.EditingId);
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A row that stays in the list with no explanation is the silent-failure shape the audit kept
    /// finding.
    /// </summary>
    [Fact]
    public void A_failed_delete_says_so()
    {
        var state = new ProjectsDialogState();
        state.AskToDelete(RowA);
        state.ConfirmDelete(RowA);

        state.Failed("Couldn't delete \"Attic\": storage is full.");

        Assert.Contains("Attic", state.Error);
    }

    [Fact]
    public void A_failure_with_nothing_to_say_still_says_something()
    {
        var state = new ProjectsDialogState();

        state.Failed("  ");

        Assert.False(string.IsNullOrWhiteSpace(state.Error));
    }

    [Fact]
    public void Closing_leaves_nothing_pending_for_the_next_time_it_opens()
    {
        var state = new ProjectsDialogState();
        state.StartRename(RowA, "Attic");
        state.AskToDelete(RowB);
        state.Failed("nope");

        state.Reset();

        Assert.Null(state.EditingId);
        Assert.Null(state.ConfirmingDeleteId);
        Assert.Null(state.Error);
        Assert.Equal(string.Empty, state.EditingName);
    }
}
