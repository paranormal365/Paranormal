using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Whether one piece of evidence added straight to a place may be shown to a stranger (item 250).
/// </summary>
/// <remarks>
/// <para><b>Three conditions, all load-bearing.</b> The row must be
/// <see cref="FeedMediaReviewState.Approved"/>, its place must still be a
/// <see cref="PlaceKind.PublicLocation"/>, and the file asked for must be the one the row holds.
/// Drop any and the page becomes a way to read something nobody published.</para>
///
/// <para><b>The place kind is re-asked, never remembered.</b> Same discipline as
/// <see cref="ArchiveMediaPublication"/>, and for the same reason: a place later corrected to a
/// private residence has to take its evidence down with it, with nothing to remember and no page
/// for anybody to go and edit. The cheap version — setting <c>UploadFile.IsPublic</c> when the
/// screener approves — is a global, permanent flag that would outlive the correction and hand the
/// bytes to every other endpoint in the app at once.</para>
///
/// <para><b>One predicate, two callers.</b> The listing and the serving door read the rule from
/// here rather than each spelling it out. A listing that offers what the door refuses is a page of
/// broken frames; one that hides what it would serve is merely useless. Neither can happen while
/// there is one copy.</para>
/// </remarks>
public static class PlaceEvidencePublication
{
    /// <summary>
    /// Rows of one kind at this place that a stranger may currently be shown, newest first.
    /// </summary>
    /// <param name="kind">
    /// Required, and deliberately has no default. Evidence and pictures OF the building are
    /// different claims, and the page is useless if they mix — so there is no way to call this and
    /// get both by accident. A forgotten filter is how a daylight photograph of a staircase ends
    /// up counted as evidence somebody offered.
    /// </param>
    public static IQueryable<Source.Entities.PlaceEvidence> Showable(
        BenDataContext db, Guid placeId, PlaceMediaKind kind)
        => db.PlaceEvidence.AsNoTracking()
            .Where(e => e.PlaceId == placeId
                     && e.MediaKind == kind
                     && e.ReviewState == FeedMediaReviewState.Approved
                     && e.Place!.Kind == PlaceKind.PublicLocation)
            .OrderByDescending(e => e.DateCreated);

    /// <summary>
    /// True when an anonymous caller may receive this row's bytes.
    /// </summary>
    /// <remarks>
    /// The serving door does NOT ask the kind: both are published at the place and a reader
    /// fetching one is entitled to it either way. What the kind decides is which LIST a row
    /// appears in and whether it counts, not whether its bytes may be read.
    /// </remarks>
    public static Task<bool> MayServeAsync(
        BenDataContext db, Guid placeId, Guid evidenceId, CancellationToken ct)
        => db.PlaceEvidence.AsNoTracking()
            .AnyAsync(e => e.Id == evidenceId
                        && e.PlaceId == placeId
                        && e.ReviewState == FeedMediaReviewState.Approved
                        && e.Place!.Kind == PlaceKind.PublicLocation, ct);
}
