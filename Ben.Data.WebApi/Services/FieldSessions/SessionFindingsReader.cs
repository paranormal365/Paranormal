using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Files;
using Ben.Service.Models.FieldSessions;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// The EVPs and clips found in a session's recordings, for whoever is reading its page (10/28/2026).
/// </summary>
/// <remarks>
/// <para><b>Signed in:</b> what this person can open — their own copy's, and their group's. Each
/// scanner's copy is theirs (Ben, 10/26/2026), so somebody else's findings are not announced.</para>
///
/// <para><b>The public</b> (a session in a place's archive, or shown with an event's results): the
/// sender's own findings and those of whoever shows it — the event's group, or the place's verified
/// venue — never a third person's; only certified clips; and only once the session's media is
/// approved, as its recordings are.</para>
/// </remarks>
public static class SessionFindingsReader
{
    public static async Task<SessionFindings> ForViewerAsync(BenDataContext db, Guid sessionId, Guid userId, CancellationToken ct)
    {
        var copies = await db.FieldSessionAnalysisCopies.AsNoTracking()
            .Where(c => c.FieldSessionUploadId == sessionId).ToListAsync(ct);
        var visibleCopies = new List<FieldSessionAnalysisCopy>();
        foreach (var copy in copies)
            if (await FileAudienceAccess.CanViewFileAsync(db, copy.CopyUploadFileId, userId, ct)) visibleCopies.Add(copy);

        var candidates = await DerivedAsync(db, sessionId, ct);
        var clips = new List<SessionFindingClip>();
        foreach (var c in candidates)
            if (await FileAudienceAccess.CanViewFileAsync(db, c.FileId, userId, ct)) clips.Add(c);

        return new SessionFindings(clips, await EvpsAsync(db, visibleCopies, clips.Select(c => c.FileId).ToHashSet(), ct));
    }

    public static async Task<SessionFindings> ForPublicAsync(BenDataContext db, FieldSessionUpload session, CancellationToken ct)
    {
        if (session.MediaReviewState != FeedMediaReviewState.Approved) return new SessionFindings([], []);

        // Who shows it: the hosted event's group, when it is shown with the event's results; the
        // place's verified venue, when it is in the place's archive.
        var showing = new HashSet<Guid>();
        if (session.HostedEventId is { } hostedId && session.ShownWithEventResultsAtUtc is not null)
            showing.Add(await db.HostedEvents.AsNoTracking().Where(h => h.Id == hostedId).Select(h => h.OrganizationId).FirstOrDefaultAsync(ct));
        if (session.PlaceId is { } placeId && session.PublishedAtUtc is not null)
            foreach (var venue in await db.OrganizationVenueProfiles.AsNoTracking()
                         .Where(v => v.PlaceId == placeId && v.VerifiedUtc != null).Select(v => v.OrganizationId).ToListAsync(ct))
                showing.Add(venue);

        var copies = (await db.FieldSessionAnalysisCopies.AsNoTracking()
                .Where(c => c.FieldSessionUploadId == session.Id).ToListAsync(ct))
            .Where(c => (c.Scope == AnalysisScope.Person && c.ScopeAppUserId == session.SubmittedByAppUserId)
                     || (c.Scope == AnalysisScope.Organization && c.ScopeOrganizationId is { } org && showing.Contains(org)))
            .ToList();
        var allowedCopies = copies.Select(c => c.CopyUploadFileId).ToHashSet();

        var certifiedFromThem = await db.FileProvenances.AsNoTracking()
            .Where(p => p.FieldSessionUploadId == session.Id).Select(p => new { p.UploadFileId, p.AnalysisCopyUploadFileId })
            .ToListAsync(ct);
        var shown = certifiedFromThem.Where(p => allowedCopies.Contains(p.AnalysisCopyUploadFileId)).Select(p => p.UploadFileId).ToHashSet();
        var clips = (await DerivedAsync(db, session.Id, ct)).Where(c => c.Certified && shown.Contains(c.FileId)).ToList();

        return new SessionFindings(clips, await EvpsAsync(db, copies, clips.Select(c => c.FileId).ToHashSet(), ct));
    }

    /// <summary>Whether a file is one of the clips this session's public page shows.</summary>
    public static async Task<bool> IsPublicClipAsync(BenDataContext db, FieldSessionUpload session, Guid fileId, CancellationToken ct)
        => (await ForPublicAsync(db, session, ct)).Clips.Any(c => c.FileId == fileId);

    /// <summary>Every clip and edit made from the session's recordings, certified or not.</summary>
    private static async Task<List<SessionFindingClip>> DerivedAsync(BenDataContext db, Guid sessionId, CancellationToken ct)
    {
        var files = await db.UploadFiles.AsNoTracking()
            .Where(f => f.SourceFieldSessionUploadId == sessionId && f.ParentFileId != null && f.ArchivedFromUploadFileId == null)
            .Select(f => new
            {
                f.Id, f.FileName, f.ContentType, f.SourceClipPath, f.ParentFileId, f.RegionStart, f.RegionEnd, f.DateCreated,
                MadeBy = f.CreatedByAppUser.DisplayName,
            })
            .ToListAsync(ct);
        var ids = files.Select(f => f.Id).ToList();
        var certified = await db.FileProvenances.AsNoTracking().Where(p => ids.Contains(p.UploadFileId)).ToDictionaryAsync(p => p.UploadFileId, ct);
        var parents = files.Select(f => f.ParentFileId!.Value).Distinct().ToList();
        var fromCopies = await db.FieldSessionAnalysisCopies.AsNoTracking()
            .Where(c => parents.Contains(c.CopyUploadFileId)).Select(c => c.CopyUploadFileId).ToListAsync(ct);

        return files
            .Select(f => certified.TryGetValue(f.Id, out var p)
                ? new SessionFindingClip(f.Id, f.FileName, f.ContentType, p.SourceClipPath, p.RecordingOffsetMs, p.DurationMs,
                    p.Enhanced, true, FileProvenanceService.Summary(p.ChainJson), f.MadeBy ?? "Somebody", f.DateCreated)
                // Made before certificates (10/28/2026): placed only when cut straight from a copy.
                : new SessionFindingClip(f.Id, f.FileName, f.ContentType, f.SourceClipPath ?? "",
                    fromCopies.Contains(f.ParentFileId!.Value) && f.RegionStart is { } start ? (long)Math.Round(start * 1000) : null,
                    f.RegionStart is { } s && f.RegionEnd is { } e && e > s ? (long)Math.Round((e - s) * 1000) : null,
                    false, false, "No certificate", f.MadeBy ?? "Somebody", f.DateCreated))
            .OrderBy(c => c.SourceClipPath).ThenBy(c => c.RecordingOffsetMs ?? long.MaxValue)
            .ToList();
    }

    /// <summary>The EVPs kept on these copies — what was heard, where in the recording.</summary>
    private static async Task<List<SessionFindingEvp>> EvpsAsync(
        BenDataContext db, IReadOnlyList<FieldSessionAnalysisCopy> copies, HashSet<Guid> listedClips, CancellationToken ct)
    {
        var byCopy = copies.ToDictionary(c => c.CopyUploadFileId, c => c.SourceRelativePath);
        var ids = byCopy.Keys.ToList();
        var markers = await db.AudioMarkers.AsNoTracking()
            .Where(m => ids.Contains(m.UploadFileId) && m.ReviewStatus == EvpReviewStatus.Confirmed)
            .OrderBy(m => m.TimeSeconds)
            .Select(m => new
            {
                m.Id, m.UploadFileId, m.TimeSeconds, m.EndSeconds, m.Label, m.ConfidenceLevel, m.Note, m.LinkedClipUploadFileId,
                MadeBy = m.CreatedByAppUser.DisplayName,
            })
            .ToListAsync(ct);
        return markers.Select(m => new SessionFindingEvp(
                m.Id, byCopy[m.UploadFileId], (long)Math.Round(m.TimeSeconds * 1000),
                m.EndSeconds is { } end ? (long)Math.Round(end * 1000) : null,
                m.Label, m.ConfidenceLevel.ToString(), m.Note, m.MadeBy ?? "Somebody",
                m.LinkedClipUploadFileId is { } clip && listedClips.Contains(clip) ? clip : null))
            .ToList();
    }
}
