using Ben.Service.Models.Entities;

namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// Which notes belong to a stretch of a recording.
/// </summary>
/// <remarks>
/// <para>Notes are stored against a time range rather than against a clip id, so "the notes for
/// this region" is a question about numbers, and the answer has to be the same wherever it is
/// asked. It was not: the region explorer had the rule inline and nothing else had it at all, so a
/// note written while listening to a region was visible only inside the panel it was typed in.
/// Save that region as a clip and the note was gone from every view of it (2026-09-06 audio walk,
/// finding M).</para>
///
/// <para>Extracted so the explorer and the Saved Clips list agree, and so the tolerance below is
/// stated once.</para>
/// </remarks>
public static class RegionNoteMatching
{
    /// <summary>
    /// How far two region boundaries may differ and still be the same region.
    /// </summary>
    /// <remarks>
    /// A region's bounds make a round trip through the browser as floating-point seconds and come
    /// back a hair different, so exact equality finds nothing. Fifty milliseconds is far below what
    /// anybody can place a boundary to by dragging, and far above the drift.
    /// </remarks>
    public const double ToleranceSeconds = 0.05;

    /// <summary>Whether <paramref name="note"/> is about the range from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <remarks>
    /// Either the note was written about this very region, or it is a point note whose moment falls
    /// inside it — a note pinned at 1:22 belongs to every region that contains 1:22.
    /// </remarks>
    public static bool IsAbout(UploadFileRegionNoteRecord note, double start, double end)
        => (Math.Abs(note.RegionStart - start) < ToleranceSeconds
            && Math.Abs(note.RegionEnd - end) < ToleranceSeconds)
        || (note.TimeOffset is { } moment && moment >= start && moment <= end);

    /// <summary>The notes about one range, in the order they were written.</summary>
    public static IReadOnlyList<UploadFileRegionNoteRecord> For(
        IEnumerable<UploadFileRegionNoteRecord> notes, double start, double end)
        => [.. notes.Where(n => IsAbout(n, start, end)).OrderBy(n => n.DateCreated)];
}
