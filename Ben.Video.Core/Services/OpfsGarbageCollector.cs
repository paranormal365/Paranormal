namespace Ben.Video.Editor.Services;

/// <summary>
/// Works out which stored media files nothing refers to any more.
/// </summary>
/// <remarks>
/// <para>Nothing ever freed a source. Every import writes a copy of the file into the browser's own
/// storage so the project can be reopened, and removing the clip, deleting the project, or simply
/// closing the tab left that copy behind forever. A few sessions with large footage quietly fill
/// the browser's quota, at which point saving starts failing — and until this phase, failing
/// silently (2026-09-05 audit, media-2 and persistence-12).</para>
///
/// <para>The safe rule is reconciliation, not reference counting: a file is deletable only when
/// <b>no</b> saved project and nothing currently open refers to it. Counting references as clips
/// come and go would have to understand the undo stack too, because a removed clip is one
/// keystroke away from coming back, and deleting its media in between would turn an undo into a
/// missing file.</para>
///
/// <para>Pure, because deleting somebody's media is the kind of decision that should be checkable
/// without a browser and without anything actually being deleted.</para>
/// </remarks>
public static class OpfsGarbageCollector
{
    /// <summary>
    /// The stored files nothing refers to.
    /// </summary>
    /// <param name="storedClipIds">Every media file in storage, by the clip id it is named for.</param>
    /// <param name="referencedClipIds">
    /// Every clip id mentioned by a saved project, by the project currently open, or by its media
    /// bin.
    /// </param>
    /// <returns>The ids safe to delete, in a stable order.</returns>
    public static IReadOnlyList<Guid> FindOrphans(
        IEnumerable<Guid> storedClipIds, IEnumerable<Guid> referencedClipIds)
    {
        ArgumentNullException.ThrowIfNull(storedClipIds);
        ArgumentNullException.ThrowIfNull(referencedClipIds);

        var referenced = referencedClipIds.ToHashSet();

        return storedClipIds
            .Distinct()
            .Where(id => !referenced.Contains(id))
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>
    /// Whether it is safe to sweep at all.
    /// </summary>
    /// <remarks>
    /// <para>If the list of projects could not be read, every file looks unreferenced — and
    /// sweeping on that basis would delete the media for every project the person has. A failure
    /// to read is not evidence of absence.</para>
    ///
    /// <para>This is the check that turns a housekeeping job into one that cannot destroy
    /// anything: it refuses rather than guesses.</para>
    /// </remarks>
    public static bool CanSweep(bool projectIndexWasRead, int knownProjectCount, int storedFileCount)
    {
        if (!projectIndexWasRead) return false;

        // Storage full of files and not one project to explain them means something did not load.
        // Real orphans accumulate a few at a time; this shape says the index is wrong.
        return knownProjectCount > 0 || storedFileCount == 0;
    }
}
