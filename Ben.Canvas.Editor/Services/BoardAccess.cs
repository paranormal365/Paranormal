namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Whether the open board may be changed (R33). People who can read a case but not edit it get a view-only
/// board: pan, zoom, select, read, open links and export, but no editing, pasting, saving or publishing.
/// </summary>
/// <remarks>
/// The server stays the authority - every write is checked there. This only stops the screen from letting
/// somebody spend an hour on changes the server will refuse. A board kept only on this device is always editable.
/// </remarks>
public sealed class BoardAccess
{
    public bool CanEdit { get; private set; } = true;

    /// <summary>Why the board is view-only, in a sentence, or null while it is editable.</summary>
    public string? Reason { get; private set; }

    public event Action? Changed;

    public void Set(bool canEdit, string? reason = null)
    {
        var why = canEdit ? null : reason ?? Core.Text.CanvasCopy.Sentences.ViewOnly;
        if (CanEdit == canEdit && Reason == why) return;
        CanEdit = canEdit;
        Reason = why;
        Changed?.Invoke();
    }
}
