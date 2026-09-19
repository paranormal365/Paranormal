using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Media;

/// <summary>
/// How long a file stays, for the plan it was uploaded under (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "Evidence collected - unless marked to save - only lasts a week
/// for everything but photos. Photos stay a month." The numbers are plan limits rather than
/// constants, so the tour plan carries 30 and 7 and every other plan carries no rows at all —
/// and <b>no row means no clock</b>, which is how the site has always behaved for everybody.</para>
///
/// <para><b>The clock is stamped once, at upload.</b> A file that was uploaded under a plan with
/// no retention keeps no expiry even if the business later moves onto the tour plan: changing plan
/// must not put a date on somebody's existing photographs. The other direction is fine — a tour
/// business that leaves the plan keeps whatever stamps its old files already carry, and the sweep
/// re-checks the plan before deleting anything.</para>
/// </remarks>
public sealed class MediaRetentionPolicy
{
    private readonly SubscriptionLimitGuard _limits;

    public MediaRetentionPolicy(SubscriptionLimitGuard limits) => _limits = limits;

    /// <summary>What the plan says about one organization's media, or nothing at all.</summary>
    /// <param name="PhotoDays">Days a photograph is kept; null means forever.</param>
    /// <param name="RecordingDays">Days a recording is kept; null means forever.</param>
    /// <param name="RecordingMinutes">The longest a recording may be; null means any length.</param>
    public sealed record Rules(int? PhotoDays, int? RecordingDays, int? RecordingMinutes)
    {
        /// <summary>Whether anything here bites at all.</summary>
        public bool Any => PhotoDays is not null || RecordingDays is not null || RecordingMinutes is not null;

        /// <summary>Nothing expires and nothing is refused — every plan but the tour one.</summary>
        public static Rules None { get; } = new(null, null, null);
    }

    /// <summary>The retention rules binding on this organization right now.</summary>
    public async Task<Rules> RulesForAsync(Guid? organizationId, CancellationToken ct)
    {
        if (organizationId is not { } id) return Rules.None;

        return new Rules(
            await _limits.ValueOfAsync(id, SubscriptionLimit.PhotoRetentionDays, ct),
            await _limits.ValueOfAsync(id, SubscriptionLimit.RecordingRetentionDays, ct),
            await _limits.ValueOfAsync(id, SubscriptionLimit.RecordingMinutes, ct));
    }

    /// <summary>
    /// When a file of this kind, uploaded now, would go.
    /// </summary>
    /// <remarks>
    /// Images take the photograph clock; everything else — audio, video, and anything we could not
    /// identify — takes the recording clock, which is the shorter one. Being wrong in the shorter
    /// direction is recoverable: the uploader is warned and can download it, and the business can
    /// keep it. Being wrong the other way quietly stores what somebody was told would go.
    /// </remarks>
    public static DateTime? ExpiryFor(Rules rules, string? contentType, DateTime now)
    {
        var days = IsImage(contentType) ? rules.PhotoDays : rules.RecordingDays;
        return days is { } d and > 0 ? now.AddDays(d) : null;
    }

    /// <summary>Why this recording is too long for the plan, or null.</summary>
    /// <remarks>
    /// Refused, not trimmed: the phone can already cut a session before sending it, so the honest
    /// answer names the limit and leaves the choice of which five minutes to whoever was there.
    /// A recording whose length we could not read is accepted — never guessed at.
    /// </remarks>
    public static string? WhyTooLong(Rules rules, string? contentType, double? durationSeconds)
    {
        if (rules.RecordingMinutes is not { } minutes || minutes <= 0) return null;
        if (IsImage(contentType)) return null;
        if (durationSeconds is not { } seconds || seconds <= 0) return null;
        if (seconds <= minutes * 60 + 1) return null;   // a second's grace for rounding

        return $"This plan takes recordings up to {minutes} minutes, and that one is "
             + $"{Math.Round(seconds / 60d, 1)} minutes. Trim it on the phone first, then send the "
             + "part that matters.";
    }

    /// <summary>Puts the clock on a file, or leaves it alone when the plan has none.</summary>
    /// <remarks>Does not save; the caller is already writing the file's row.</remarks>
    public static void Stamp(UploadFile file, Rules rules, DateTime now)
        => file.ExpiresAtUtc = ExpiryFor(rules, file.ContentType, now);

    /// <summary>
    /// Which organization a file answers to, for the purpose of its clock.
    /// </summary>
    /// <remarks>
    /// <para>There is no column that answers this. A file reaches a group through whichever door
    /// it came in — evidence at an event, a session on an investigation, a case file, or a file
    /// the group was handed outright — so the question is asked of each in turn.</para>
    ///
    /// <para>Null means nobody's plan governs it: a personal upload, and the site's oldest
    /// behaviour, which is that it stays.</para>
    /// </remarks>
    public static async Task<Guid?> OrganizationForAsync(
        BenDataContext db, Guid uploadFileId, CancellationToken ct)
    {
        var owned = await db.UploadFiles.AsNoTracking()
            .Where(f => f.Id == uploadFileId)
            .Select(f => f.OwnerOrganizationId)
            .FirstOrDefaultAsync(ct);
        if (owned is not null) return owned;

        var atAnEvent = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(e => e.UploadFileId == uploadFileId)
            .Select(e => (Guid?)e.OrgCalendarEvent.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (atAnEvent is not null) return atAnEvent;

        var inASession = await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => f.UploadFileId == uploadFileId
                     && f.FieldSessionUpload.InvestigationId != null)
            .Select(f => (Guid?)f.FieldSessionUpload.Investigation!.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (inASession is not null) return inASession;

        return await db.CaseFiles.AsNoTracking()
            .Where(f => f.UploadFileId == uploadFileId)
            .Select(f => (Guid?)f.Case.OrganizationId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>An image by its content type. Anything unreadable is treated as a recording.</summary>
    private static bool IsImage(string? contentType)
        => contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
}
