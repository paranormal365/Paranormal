using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// What the evidence at one place adds up to (item 250).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-21: <i>"we can count the number of files of evidence public — which even
/// events or investigations with public evidence is there as well. We can count the number of
/// votes. Then positive, negative, unknown and the average of each."</i></para>
///
/// <para><b>One file counts once, whatever route it arrived by</b> — Ben's own decision, and the
/// only reason this is a service rather than three counts added together. Evidence reaches a place
/// three ways: added straight to it, published in a field session, or offered by a guest at an
/// event. The same upload can appear on more than one of those paths, so the routes are gathered
/// as a set of file ids and de-duplicated before anything is counted. Adding three counts would
/// report a place as having more evidence than it has, and the figure nobody can check is the one
/// nobody trusts.</para>
///
/// <para><b>Every route re-asks its own publication rule</b> rather than trusting a flag, so a
/// retracted session, a withdrawn contribution or a place corrected to a private residence all
/// drop out of the total on the next read with nothing to migrate.</para>
///
/// <para><b>No ranking.</b> Ben decided 2026-09-21 that a place reports its own figures and the
/// site does not order one property against another: Cragfont is a real building with real owners
/// and "the third most haunted house in Tennessee" is a claim about them. So this answers for one
/// place and there is deliberately no method here that takes a list.</para>
/// </remarks>
public static class PlaceEvidenceTally
{
    /// <summary>
    /// Every file that counts as this place's evidence, once each.
    /// </summary>
    /// <remarks>
    /// Public because the figures and the vote lookups must be over the same set. Two callers
    /// each assembling "the evidence here" is two answers, and the one that drifts is the one
    /// nobody is looking at.
    /// </remarks>
    public static async Task<IReadOnlyCollection<Guid>> FileIdsAsync(
        BenDataContext db, Guid placeId, CancellationToken ct)
    {
        // Added straight to the place.
        var added = await PlaceEvidencePublication.Showable(db, placeId, PlaceMediaKind.Evidence)
            .Select(e => e.UploadFileId)
            .ToListAsync(ct);

        // Published in a field session recorded here. The same four conditions
        // ArchiveMediaPublication serves bytes under, asked again rather than remembered.
        var sessions = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.PlaceId == placeId
                     && s.PublishedAtUtc != null
                     && s.Place!.Kind == PlaceKind.PublicLocation
                     && s.MediaReviewState == FeedMediaReviewState.Approved)
            .SelectMany(s => s.Files)
            .Where(f => f.UploadFileId != null)
            .Select(f => f.UploadFileId!.Value)
            .ToListAsync(ct);

        // Offered by a guest at an event here, and published by them to the place.
        var events = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(e => e.OrgCalendarEvent.PlaceId == placeId
                     && e.PublishedToPlaceAtUtc != null
                     && e.ArchiveReviewState == FeedMediaReviewState.Approved
                     && e.OrgCalendarEvent.Place!.Kind == PlaceKind.PublicLocation)
            .Select(e => e.UploadFileId)
            .ToListAsync(ct);

        // The de-duplication Ben asked for, in one line and in one place.
        return added.Concat(sessions).Concat(events).ToHashSet();
    }

    /// <summary>What this place's evidence and its votes come to.</summary>
    public static async Task<PlaceEvidenceFigures> ForPlaceAsync(
        BenDataContext db, Guid placeId, CancellationToken ct)
    {
        var files = await FileIdsAsync(db, placeId, ct);
        if (files.Count == 0) return PlaceEvidenceFigures.Nothing;

        var votes = await db.EvidenceVotes.AsNoTracking()
            .Where(v => files.Contains(v.UploadFileId))
            .Select(v => new { v.VoteType, v.UploadFileId })
            .ToListAsync(ct);

        var confirms     = votes.Count(v => v.VoteType == EvidenceVoteType.Confirms);
        var disputes     = votes.Count(v => v.VoteType == EvidenceVoteType.Disputes);
        var inconclusive = votes.Count(v => v.VoteType == EvidenceVoteType.Inconclusive);

        return new PlaceEvidenceFigures(
            EvidenceCount: files.Count,
            // How many pieces anybody has actually weighed in on. A place with forty files and
            // two votes is a different thing from one with two files and forty, and a total on
            // its own cannot tell them apart.
            VotedOnCount: votes.Select(v => v.UploadFileId).Distinct().Count(),
            VoteCount: votes.Count,
            Confirms: confirms,
            Disputes: disputes,
            Inconclusive: inconclusive,
            // The site's one definition of the score, reused rather than re-derived — see
            // EvidenceVoteScore for why the mapping is a function and not the enum's values.
            Score: EvidenceVoteScore.FromCounts(confirms, disputes, inconclusive),
            // "The average of each", as Ben asked: what share of the opinion each way of seeing it
            // holds. A share rather than a mean, because the three are categories and the mean of
            // a category is not a number that means anything.
            ConfirmsShare:     Share(confirms, votes.Count),
            DisputesShare:     Share(disputes, votes.Count),
            InconclusiveShare: Share(inconclusive, votes.Count));
    }

    /// <summary>A proportion in 0..1, or null when nobody has voted.</summary>
    /// <remarks>
    /// Null rather than zero. "Nobody has said" and "everybody said no" are different facts, and a
    /// bar chart that draws them the same way is a chart that lies about an empty place.
    /// </remarks>
    private static double? Share(int part, int total)
        => total == 0 ? null : (double)part / total;
}
