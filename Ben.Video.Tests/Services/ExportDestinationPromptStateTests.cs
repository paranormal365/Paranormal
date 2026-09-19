using Ben.Video.Core.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The window that decides where a finished render goes — and, once, whether it survives.
/// </summary>
/// <remarks>
/// Closing it deleted the export outright, with no confirmation and nothing to say it had
/// happened, so an accidental Escape threw away an hour's render (2026-09-05 audit, export-17).
/// </remarks>
public sealed class ExportDestinationPromptStateTests
{
    [Fact]
    public void A_fresh_prompt_offers_every_destination()
    {
        var state = new ExportDestinationPromptState();

        Assert.True(state.CanAct);
        Assert.False(state.IsBusy);
        Assert.False(state.IsConfirmingDiscard);
        Assert.False(state.IsClosed);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Working_disables_the_whole_footer()
    {
        var state = new ExportDestinationPromptState();

        Assert.True(state.BeginWork());
        Assert.True(state.IsBusy);
        Assert.False(state.CanAct);
    }

    /// <summary>
    /// Publishing a large render is slow enough to invite a second click, which would start a
    /// download of a file the upload is midway through reading.
    /// </summary>
    [Fact]
    public void A_second_destination_cannot_start_while_one_is_running()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();

        Assert.False(state.BeginWork());
    }

    [Fact]
    public void A_destination_that_took_the_file_closes_the_window()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();

        state.Succeeded();

        Assert.True(state.IsClosed);
        Assert.False(state.IsBusy);
        Assert.Null(state.Error);
    }

    /// <summary>
    /// The render is untouched when an upload fails, so every other destination has to stay live.
    /// A prompt that closed on failure would leave somebody believing the video reached the server
    /// while the only copy was already gone.
    /// </summary>
    [Fact]
    public void A_failed_destination_keeps_the_window_and_the_other_destinations()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();

        state.Failed("Upload failed: the server said 500.");

        Assert.False(state.IsClosed);
        Assert.True(state.CanAct);
        Assert.Contains("500", state.Error);
    }

    /// <summary>An empty message still has to read as a failure with a way out.</summary>
    [Fact]
    public void A_failure_with_nothing_to_say_still_says_something()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();

        state.Failed("   ");

        Assert.Contains("save it to your machine", state.Error);
    }

    [Fact]
    public void Retrying_after_a_failure_clears_the_message()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();
        state.Failed("nope");

        Assert.True(state.BeginWork());
        Assert.Null(state.Error);
    }

    // ── Discarding ────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point: closing asks, it does not delete.
    /// </summary>
    [Fact]
    public void Closing_the_window_asks_first()
    {
        var state = new ExportDestinationPromptState();

        Assert.True(state.AskBeforeDiscarding());
        Assert.True(state.IsConfirmingDiscard);
        Assert.False(state.IsClosed);
    }

    [Fact]
    public void Confirming_is_what_deletes_the_render()
    {
        var state = new ExportDestinationPromptState();
        state.AskBeforeDiscarding();

        Assert.True(state.ConfirmDiscard());
        Assert.True(state.IsClosed);
    }

    /// <summary>
    /// Nothing can delete a render that nobody was asked about, however the call arrives.
    /// </summary>
    [Fact]
    public void A_confirm_that_was_never_asked_for_deletes_nothing()
    {
        var state = new ExportDestinationPromptState();

        Assert.False(state.ConfirmDiscard());
        Assert.False(state.IsClosed);
    }

    [Fact]
    public void Backing_out_of_the_question_leaves_everything_as_it_was()
    {
        var state = new ExportDestinationPromptState();
        state.AskBeforeDiscarding();

        state.KeepIt();

        Assert.False(state.IsConfirmingDiscard);
        Assert.False(state.IsClosed);
        Assert.True(state.CanAct);
    }

    /// <summary>
    /// An upload in flight owns the file. Closing the window mid-upload cannot start a discard
    /// underneath it.
    /// </summary>
    [Fact]
    public void Closing_during_an_upload_asks_nothing_and_deletes_nothing()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();

        Assert.False(state.AskBeforeDiscarding());
        Assert.False(state.IsConfirmingDiscard);
        Assert.False(state.ConfirmDiscard());
    }

    /// <summary>
    /// The component is reused for the next export, so a prompt that closed on a discard must not
    /// open again already asking to discard.
    /// </summary>
    [Fact]
    public void The_next_export_opens_a_clean_prompt()
    {
        var state = new ExportDestinationPromptState();
        state.BeginWork();
        state.Failed("nope");
        state.AskBeforeDiscarding();
        state.ConfirmDiscard();

        state.Reopen();

        Assert.False(state.IsClosed);
        Assert.False(state.IsConfirmingDiscard);
        Assert.True(state.CanAct);
        Assert.Null(state.Error);
    }
}
