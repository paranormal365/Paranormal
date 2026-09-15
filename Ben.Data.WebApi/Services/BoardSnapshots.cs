using Ben.Data.Common.Enums;
using Ben.Data.WebApi.SeedData;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The rule that keeps a published case board picture inside the case (canvas plan review R22).
/// </summary>
/// <remarks>
/// <para><b>What the picture is.</b> <c>POST api/canvas-documents/{id}/publish</c> stores a PNG of
/// the team's working board as a case file. It bakes in whatever the board held — witness names, a
/// client's home address, pinned places, somebody's phone number on a card — and nobody reviewed it
/// as a picture before it was made. So it is filed under its own upload type,
/// <see cref="UploadFileTypeSeeder.BoardSnapshotFileTypeId"/>, and it stays inside the case.</para>
///
/// <para><b>Where it is refused.</b> A case file reaches a visitor only through a timeline entry that
/// is <see cref="CaseTimelineVisibility.Public"/> on a public case, and through the public-page media
/// rule built on that (<c>CaseMediaPublication</c>). The first is refused with
/// <see cref="StaysInsideTheCase"/> when an entry holding a snapshot is made Public; the second never
/// offers or renders a snapshot at all, so a row that got there some other way — an older row, a
/// hand edit — still does not publish. Sharing the entry with the case's client stays allowed: the
/// client is inside the case.</para>
///
/// <para>No endpoint today attaches an existing file to a timeline entry (entries get files only by
/// a client uploading a new one), so the entry refusal is the guard for the day one is added, and
/// the publication filter is the guard that holds regardless.</para>
/// </remarks>
public static class BoardSnapshots
{
    /// <summary>What an investigator is told when they try to make one public.</summary>
    public const string StaysInsideTheCase =
        "A board snapshot stays inside the case. It shows everything the board held — names, "
        + "addresses, pinned places — so it cannot go on a public timeline entry. Remove the snapshot "
        + "from this entry, or attach a picture you have checked instead.";

    /// <summary>Whether any file on this timeline entry is a board snapshot.</summary>
    public static Task<bool> EntryHoldsOneAsync(BenDataContext db, Guid timelineEntryId, CancellationToken ct)
        => db.CaseTimelineEntryFiles.AsNoTracking()
            .AnyAsync(f => f.CaseTimelineEntryId == timelineEntryId
                        && f.UploadFile.UploadFileTypeId == UploadFileTypeSeeder.BoardSnapshotFileTypeId, ct);
}
