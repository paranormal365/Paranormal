namespace Ben.Video.Core.Services;

/// <summary>
/// What the Projects window is doing to the row somebody is pointing at.
/// </summary>
/// <remarks>
/// <para>The list holds every project on this computer, and two of the three things it offers are
/// irreversible from inside the window: a delete removes the only copy of somebody's edit, and a
/// rename overwrites the name they find it by. Both were guarded by a pair of nullable ids and a
/// message field read straight out of a Razor component, where the only way to ask whether a
/// delete could happen without a confirmation was to click one (2026-09-05 audit, phase 11).</para>
///
/// <para>The rules, stated once: a delete happens only for the row that was confirmed, a rename
/// only for the row being edited and only with a name that is not blank, and any failure has to
/// end up on screen — a row that stays in the list with no explanation is the silent-failure shape
/// the audit kept finding.</para>
/// </remarks>
public sealed class ProjectsDialogState
{
    /// <summary>The row whose name is being edited, or null.</summary>
    public Guid? EditingId { get; private set; }

    /// <summary>The name being typed. Bound to the text box, so it is settable.</summary>
    public string EditingName { get; set; } = string.Empty;

    /// <summary>The row asking "delete this?", or null.</summary>
    public Guid? ConfirmingDeleteId { get; private set; }

    /// <summary>The failure to show above the list, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether <paramref name="rowId"/> is currently showing its rename box.</summary>
    public bool IsEditing(Guid rowId) => EditingId == rowId;

    /// <summary>Whether <paramref name="rowId"/> is currently asking to be deleted.</summary>
    public bool IsConfirmingDelete(Guid rowId) => ConfirmingDeleteId == rowId;

    /// <summary>
    /// Starts renaming a row, seeded with the name it already has.
    /// </summary>
    /// <remarks>
    /// Editing one row cancels a delete another row was asking about: two questions open at once
    /// in the same list is how somebody answers the wrong one.
    /// </remarks>
    public void StartRename(Guid rowId, string currentName)
    {
        EditingId          = rowId;
        EditingName        = currentName ?? string.Empty;
        ConfirmingDeleteId = null;
    }

    /// <summary>
    /// Ends the rename and says which project to rename to what.
    /// </summary>
    /// <returns>
    /// The row and its new name, or null when there is nothing to do — no row is being edited, the
    /// name is blank, or it did not change.
    /// </returns>
    public (Guid Id, string Name)? CommitRename()
    {
        var id   = EditingId;
        var name = EditingName?.Trim();

        EditingId = null;

        return id is { } rowId && !string.IsNullOrWhiteSpace(name) ? (rowId, name) : null;
    }

    /// <summary>Abandons the rename, leaving the project's name as it was.</summary>
    public void CancelRename() => EditingId = null;

    /// <summary>
    /// Asks about deleting a row.
    /// </summary>
    /// <remarks>Only one row can be asking at a time, and never while that row is being renamed.</remarks>
    public void AskToDelete(Guid rowId)
    {
        ConfirmingDeleteId = rowId;
        EditingId          = null;
    }

    /// <summary>Backs out of the question.</summary>
    public void CancelDelete() => ConfirmingDeleteId = null;

    /// <summary>
    /// Confirms the delete of <paramref name="rowId"/>.
    /// </summary>
    /// <returns>
    /// True when that row is the one that was asked about — so a stale click on another row, or a
    /// call that never went through the question, deletes nothing.
    /// </returns>
    public bool ConfirmDelete(Guid rowId)
    {
        if (ConfirmingDeleteId != rowId) return false;

        ConfirmingDeleteId = null;
        Error              = null;
        return true;
    }

    /// <summary>Records a failure, which stays on screen until the next attempt.</summary>
    public void Failed(string message) =>
        Error = string.IsNullOrWhiteSpace(message) ? "That didn't work." : message;

    /// <summary>Clears the failure before trying something else.</summary>
    public void ClearError() => Error = null;

    /// <summary>Puts the window back to its opening state.</summary>
    public void Reset()
    {
        EditingId          = null;
        EditingName        = string.Empty;
        ConfirmingDeleteId = null;
        Error              = null;
    }
}
