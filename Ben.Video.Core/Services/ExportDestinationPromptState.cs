namespace Ben.Video.Core.Services;

/// <summary>
/// What the "where should this render go?" window is doing right now.
/// </summary>
/// <remarks>
/// <para>Three destinations, a confirmation, a busy state and an error message, all in one small
/// window — and the thing it is holding is a render somebody may have waited half an hour for.
/// Closing it used to delete that render outright, with no confirmation and nothing to say it had
/// happened, so an accidental Escape threw away the whole export (2026-09-05 audit, export-17).
/// A state machine that can be asked questions without a browser is the difference between
/// knowing that never happens again and hoping so.</para>
///
/// <para>The rule underneath every transition: the file is deleted on purpose or not at all. A
/// failed upload leaves the prompt open with "Save to my machine" still live, because at that
/// moment the render is the only copy in existence.</para>
/// </remarks>
public sealed class ExportDestinationPromptState
{
    /// <summary>Whether a destination is being written to right now.</summary>
    /// <remarks>
    /// Publishing a large render is slow enough that a second click on another button would start
    /// a download of a file the upload is midway through reading, so the whole footer goes busy,
    /// not just the button that was clicked.
    /// </remarks>
    public bool IsBusy { get; private set; }

    /// <summary>The failure to show, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether the window is asking "discard it?" rather than offering destinations.</summary>
    public bool IsConfirmingDiscard { get; private set; }

    /// <summary>Whether the window has finished and should close.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Whether a button in the footer can be pressed.</summary>
    public bool CanAct => !IsBusy;

    /// <summary>
    /// Begins writing to a destination.
    /// </summary>
    /// <returns>
    /// False when something is already running, so a double click cannot start a second write.
    /// </returns>
    public bool BeginWork()
    {
        if (IsBusy) return false;

        IsBusy = true;
        Error  = null;
        return true;
    }

    /// <summary>The destination took the file: the window is done.</summary>
    public void Succeeded()
    {
        IsBusy              = false;
        Error               = null;
        IsConfirmingDiscard = false;
        IsClosed            = true;
    }

    /// <summary>
    /// The destination refused it.
    /// </summary>
    /// <remarks>
    /// The window stays open and every other destination stays available. The render has not been
    /// touched at this point, so "Save to my machine" is a live escape hatch rather than a dead
    /// button next to a lost file.
    /// </remarks>
    public void Failed(string message)
    {
        IsBusy = false;
        Error  = string.IsNullOrWhiteSpace(message)
            ? "That didn't work. You can still save it to your machine."
            : message;
    }

    /// <summary>
    /// Somebody asked to throw the render away — by pressing Discard, or by closing the window.
    /// </summary>
    /// <returns>False while a destination is being written to, when closing means nothing yet.</returns>
    public bool AskBeforeDiscarding()
    {
        if (IsBusy) return false;

        IsConfirmingDiscard = true;
        return true;
    }

    /// <summary>Backs out of the confirmation, leaving every destination as it was.</summary>
    public void KeepIt() => IsConfirmingDiscard = false;

    /// <summary>
    /// Confirms the discard.
    /// </summary>
    /// <returns>
    /// True when the file should actually be deleted — only ever after
    /// <see cref="AskBeforeDiscarding"/>, so a stray call cannot delete a render nobody was asked
    /// about.
    /// </returns>
    public bool ConfirmDiscard()
    {
        if (!IsConfirmingDiscard) return false;

        IsConfirmingDiscard = false;
        IsClosed            = true;
        return true;
    }

    /// <summary>Puts the window back to its opening state for the next export.</summary>
    public void Reopen()
    {
        IsBusy              = false;
        Error               = null;
        IsConfirmingDiscard = false;
        IsClosed            = false;
    }
}
