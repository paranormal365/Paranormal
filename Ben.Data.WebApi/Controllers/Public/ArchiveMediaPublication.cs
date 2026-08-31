using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Whether one file belonging to a published field session may be shown to a stranger.
/// </summary>
/// <remarks>
/// <para><b>Four conditions, and every one of them is load-bearing.</b> The session must be
/// published (an act with a date, performed by its owner), it must be attached to a place, that
/// place must be a <see cref="PlaceKind.PublicLocation"/>, its media must be
/// <see cref="FeedMediaReviewState.Approved"/>, and the file must actually belong to that
/// session. Drop any one and the archive becomes a way to read something it was never given.</para>
///
/// <para><b>The place kind is the whole safety story.</b> Publishing forces
/// <c>PlaceKind.PublicLocation</c> and never lets a caller choose it, precisely so that private
/// residences cannot enter the archive. Re-asking here means a place later corrected to a private
/// residence takes its media down with it, with nothing to remember and no page to edit — the
/// same binding-not-copying discipline the case-media slots are built on.</para>
///
/// <para><b>Asked per request, never cached into a flag.</b> The cheap version of this is to set
/// <c>UploadFile.IsPublic</c> when a session is published. That flag is global and permanent: it
/// would outlive the publication, survive a retraction, survive a flag being upheld, and hand the
/// file to every other endpoint in the app at once. A retraction that leaves the bytes readable is
/// not a retraction.</para>
/// </remarks>
public static class ArchiveMediaPublication
{
    /// <summary>True when an anonymous caller may receive this file's bytes.</summary>
    public static Task<bool> MayServeAsync(
        BenDataContext db, Guid fieldSessionId, Guid uploadFileId, CancellationToken ct)
        => db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.Id == fieldSessionId
                     && s.PublishedAtUtc != null
                     && s.PlaceId != null
                     && s.Place!.Kind == PlaceKind.PublicLocation
                     && s.MediaReviewState == FeedMediaReviewState.Approved)
            .AnyAsync(s => s.Files.Any(f => f.UploadFileId == uploadFileId), ct);

    /// <summary>
    /// The files of one published session that may currently be shown, in document order.
    /// </summary>
    /// <remarks>
    /// Deliberately the same predicate as <see cref="MayServeAsync"/> rather than a second reading
    /// of the rule. A listing that offers what the serving endpoint refuses is a page of broken
    /// frames; a listing that hides what it would serve is merely useless. Both come from one
    /// place so neither can happen.
    /// </remarks>
    public static async Task<IReadOnlyList<ArchiveMediaItem>> ServableFilesAsync(
        BenDataContext db, Guid fieldSessionId, CancellationToken ct)
        => await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.Id == fieldSessionId
                     && s.PublishedAtUtc != null
                     && s.PlaceId != null
                     && s.Place!.Kind == PlaceKind.PublicLocation
                     && s.MediaReviewState == FeedMediaReviewState.Approved)
            .SelectMany(s => s.Files)
            .OrderBy(f => f.RelativePath)
            .Select(f => new ArchiveMediaItem(
                f.UploadFileId,
                f.RelativePath,
                f.UploadFile.ContentType,
                f.UploadFile.FileName))
            .ToListAsync(ct);
}

/// <summary>
/// One servable recording from a session's archive entry.
/// </summary>
/// <param name="RelativePath">
/// What the session document calls this file — <c>media/audio-001.m4a</c>. Carried so a reader can
/// tie a recording back to the reading that references it, which is the only reason the audio is
/// worth anything next to the numbers.
/// </param>
/// <param name="ContentType">
/// The type of the copy that will actually be SERVED, which for a sanitized derivative is not
/// necessarily the type the device uploaded.
/// </param>
public sealed record ArchiveMediaItem(
    Guid UploadFileId,
    string RelativePath,
    string ContentType,
    string FileName);
