using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// Which of a case's files a group may put on a public page.
/// </summary>
/// <remarks>
/// <para>The prerequisite for item #80's page templates. A template slot that offered "any photo
/// from the case" would be a way to publish the investigators' working files — precisely the hole
/// part 4 exists to close, one record type further in.</para>
///
/// <para><b>The rule is the one the public case page already follows</b>, restated in one place
/// rather than invented: a file is publishable when it hangs off a timeline entry that is itself
/// <see cref="CaseTimelineVisibility.Public"/>, on a case that is itself public. Nothing here
/// widens what a visitor can already reach — a template can publish what the case page could
/// publish, and not one file more.</para>
///
/// <para><b>Files on the case's general Files tab are never publishable.</b> <c>CaseFile</c> has no
/// visibility column at all, so there is no answer to "did anybody agree to this being public?" —
/// and inventing one by defaulting would publish, in bulk, exactly the material nobody has looked
/// at. If those should be publishable, they need a visibility field and a person to set it; that is
/// a product decision, and it is recorded in the backlog rather than guessed at here.</para>
///
/// <para><b>Resolved at read, never snapshotted</b> — the same discipline as <see cref="CmsEmbed"/>.
/// A slot holds a file id; whether that file may still be shown is asked again on every request. So
/// a timeline entry pulled back from Public to OrgOnly next month removes its photo from a page
/// published today, without anybody remembering which pages used it.</para>
/// </remarks>
public static class CaseMediaPublication
{
    /// <summary>
    /// The files on this case that may appear publicly, newest first.
    /// </summary>
    /// <remarks>
    /// Used by both halves and deliberately so: the picker offers this list, and the renderer
    /// re-checks against it. A picker that filtered correctly while the renderer trusted whatever
    /// id it was handed would be the same mistake as offering the right options and calling it a
    /// rule.
    /// </remarks>
    public static async Task<IReadOnlyList<PublishableCaseFile>> PublishableAsync(
        BenDataContext db, Guid caseId, CancellationToken ct)
    {
        var caseIsPublic = await db.Cases.AsNoTracking().AnyAsync(
            c => c.Id == caseId
              && c.IsPublic
              && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted), ct);

        if (!caseIsPublic) return [];

        return await db.CaseTimelineEntryFiles.AsNoTracking()
            .Where(f => f.CaseTimelineEntry.CaseId == caseId
                     && f.CaseTimelineEntry.Visibility == CaseTimelineVisibility.Public)
            .OrderByDescending(f => f.CaseTimelineEntry.EventDateTime ?? f.CaseTimelineEntry.DateCreated)
            .Select(f => new PublishableCaseFile(
                f.UploadFileId,
                f.CaseTimelineEntry.Title,
                f.CaseTimelineEntry.EventDateTime,
                f.CaseTimelineEntry.EntryType))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Whether this specific file may be published for this case, right now.
    /// </summary>
    /// <remarks>
    /// Asked per file at render time. An id that no longer qualifies — because the entry's
    /// visibility was narrowed, the case was unpublished, or the file was unlinked — answers false,
    /// and the slot renders as nothing. Degrading to silence is the required behaviour: a slot that
    /// kept showing a photo after its entry went private would make the page immune to somebody
    /// changing their mind, which is the whole reason for binding rather than copying.
    /// </remarks>
    public static async Task<bool> MayPublishAsync(
        BenDataContext db, Guid caseId, Guid uploadFileId, CancellationToken ct)
    {
        var caseIsPublic = await db.Cases.AsNoTracking().AnyAsync(
            c => c.Id == caseId
              && c.IsPublic
              && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted), ct);

        if (!caseIsPublic) return false;

        return await db.CaseTimelineEntryFiles.AsNoTracking().AnyAsync(
            f => f.UploadFileId == uploadFileId
              && f.CaseTimelineEntry.CaseId == caseId
              && f.CaseTimelineEntry.Visibility == CaseTimelineVisibility.Public, ct);
    }

    /// <summary>Filters a set of file ids down to those publishable, preserving the caller's order.</summary>
    /// <remarks>
    /// One query rather than one per id — a write-up with a dozen photos should not cost a dozen
    /// round trips, and the per-file overload above exists for the single-slot case.
    /// </remarks>
    public static async Task<IReadOnlyList<Guid>> FilterPublishableAsync(
        BenDataContext db, Guid caseId, IReadOnlyList<Guid> uploadFileIds, CancellationToken ct)
    {
        if (uploadFileIds.Count == 0) return [];

        var allowed = (await PublishableAsync(db, caseId, ct))
            .Select(f => f.UploadFileId)
            .ToHashSet();

        return [.. uploadFileIds.Where(allowed.Contains)];
    }
}
