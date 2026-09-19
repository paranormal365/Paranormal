namespace Ben.Video.Editor.Services;

/// <summary>
/// Whether closing the tab should ask first.
/// </summary>
/// <remarks>
/// <para>Nothing asked. Closing the tab, or following a link out of the editor, took whatever was
/// unsaved with it — and because the browser gives no warning of its own for a page that has not
/// registered one, there was no moment at which anybody could have noticed (2026-09-05 audit, F9).
/// </para>
///
/// <para>Two things are worth stopping for and they are not the same. Unsaved edits are the obvious
/// one. The other is a render in progress: it lives entirely in the tab, so leaving does not
/// background it, it destroys it — and a long export is exactly the thing somebody wanders off
/// during.</para>
///
/// <para>What must NOT trigger it is a clean project sitting idle, because a browser that warns
/// every time gets its warning ignored.</para>
/// </remarks>
public static class UnloadGuardPolicy
{
    /// <summary>Whether to ask before the page goes away.</summary>
    /// <param name="hasUnsavedChanges">Edits that are not in storage yet.</param>
    /// <param name="autosavePending">An autosave that has been scheduled and has not run.</param>
    /// <param name="renderRunning">An export or preview render under way in this tab.</param>
    public static bool ShouldGuard(bool hasUnsavedChanges, bool autosavePending, bool renderRunning) =>
        hasUnsavedChanges || autosavePending || renderRunning;

    /// <summary>
    /// What to tell the person, when the browser lets a page say anything at all.
    /// </summary>
    /// <remarks>
    /// Most browsers now ignore this text and show their own wording, so it is written for the ones
    /// that do not rather than relied upon by the ones that do.
    /// </remarks>
    public static string Reason(bool hasUnsavedChanges, bool renderRunning) => renderRunning
        ? "A render is still running in this tab. Leaving now cancels it."
        : hasUnsavedChanges
            ? "This project has unsaved changes."
            : "This project has changes that have not finished saving.";
}
