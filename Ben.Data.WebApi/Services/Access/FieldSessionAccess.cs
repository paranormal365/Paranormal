using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>
/// Who may read a field session, and the doors that rule is built from.
/// </summary>
/// <remarks>
/// Moved unchanged out of <c>FieldSessionUploadController</c> (phase 9 of the audio plan, 10/27/2026)
/// so the scan rules in <see cref="FieldSessionAnalysisAccess"/> are built from the same doors
/// rather than copies of them. The controller's private methods forward here.
/// </remarks>
public static class FieldSessionAccess
{
    /// <summary>
    /// Who may READ a session: its own sender, or anyone entitled to its investigation.
    /// </summary>
    public static async Task<bool> MayReadAsync(
        BenDataContext db, FieldSessionUpload session, Guid userId, CancellationToken ct)
    {
        if (session.SubmittedByAppUserId == userId) return true;
        if (session.InvestigationId is Guid linked) return await MayContributeAsync(db, linked, userId, ct);
        // A tour date's or event's sessions are for the group's team for it, not the other guests.
        if (session.OrgCalendarEventId is Guid eventId) return await IsEventTeamAsync(db, eventId, userId, ct);
        if (session.HostedEventId is Guid hostedId) return await IsHostedTeamAsync(db, hostedId, userId, ct);
        return false;
    }

    /// <summary>
    /// Who may add a recording to an investigation.
    /// </summary>
    /// <remarks>
    /// <para>Three doors, widest last (Ben's rule, 2026-08-25):</para>
    /// <list type="bullet">
    /// <item>Somebody who was actually on it — an attendee row. The commonest case.</item>
    /// <item>An active member of the group running it.</item>
    /// <item>Anybody at all, when the investigation or its case is <b>public</b>. An open
    /// investigation is an invitation, and thirty strangers with phones is the whole value of
    /// one — the same bargain the public-event evidence door already makes.</item>
    /// </list>
    ///
    /// <para>A public case does NOT make a private residence's investigation public: the case's
    /// own flag is what is read, and case privacy is decided elsewhere and deliberately.</para>
    ///
    /// <para><b>"Public case" here means PUBLISHED</b> — the flag <i>and</i> a status of Public or
    /// Haunted, which is what the phrase means in every other query on the site (see
    /// <c>PublicCaseController</c>'s own summary). This asked for the flag alone until 2026-09-17,
    /// and the flag alone is set long before anybody publishes anything: it is a stated intention,
    /// not a publication. That made the widest of the three doors open on a case somebody had
    /// merely ticked a box on, which was never the bargain. Found while making an unpaid account's
    /// cases public by default, where it would have thrown this door open on every new case.</para>
    /// </remarks>
    public static async Task<bool> MayContributeAsync(
        BenDataContext db, Guid investigationId, Guid userId, CancellationToken ct)
    {
        var wasThere = await db.InvestigationAttendees.AsNoTracking()
            .AnyAsync(a => a.InvestigationId == investigationId && a.AppUserId == userId, ct);
        if (wasThere) return true;

        var investigation = await db.Investigations.AsNoTracking()
            .Where(i => i.Id == investigationId)
            .Select(i => new
            {
                i.OrganizationId,
                i.Visibility,
                CaseIsPublic = i.CaseId != null
                    && db.Cases.Any(c => c.Id == i.CaseId
                                      && c.IsPublic
                                      && (c.Status == Ben.Data.Common.Enums.CaseStatus.Public
                                       || c.Status == Ben.Data.Common.Enums.CaseStatus.Haunted)),
            })
            .FirstOrDefaultAsync(ct);
        if (investigation is null) return false;

        if (investigation.Visibility == InvestigationVisibility.Public
            || investigation.CaseIsPublic)
        {
            return true;
        }

        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == investigation.OrganizationId
                        && m.AppUserId == userId && m.IsActive, ct);
    }

    /// <summary>Who may READ a tour date's or event's sessions: the group, and the date's guides.</summary>
    public static async Task<bool> IsEventTeamAsync(
        BenDataContext db, Guid eventId, Guid userId, CancellationToken ct, Guid? organizationId = null)
    {
        var organization = organizationId ?? await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.Id == eventId).Select(e => e.OrganizationId).FirstOrDefaultAsync(ct);
        return await db.OrganizationUserMemberships.AsNoTracking()
                   .AnyAsync(m => m.OrganizationId == organization && m.AppUserId == userId && m.IsActive, ct)
            || await db.OrgCalendarEventGuides.AsNoTracking()
                   .AnyAsync(g => g.OrgCalendarEventId == eventId && g.AppUserId == userId, ct);
    }

    /// <summary>Who may READ a hosted event's sessions: the group, and the event's confirmed staff.</summary>
    public static async Task<bool> IsHostedTeamAsync(
        BenDataContext db, Guid hostedId, Guid userId, CancellationToken ct, Guid? organizationId = null)
    {
        var organization = organizationId ?? await db.HostedEvents.AsNoTracking()
            .Where(h => h.Id == hostedId).Select(h => h.OrganizationId).FirstOrDefaultAsync(ct);
        return await db.OrganizationUserMemberships.AsNoTracking()
                   .AnyAsync(m => m.OrganizationId == organization && m.AppUserId == userId && m.IsActive, ct)
            || await db.HostedEventStaff.AsNoTracking()
                   .AnyAsync(s => s.HostedEventId == hostedId && s.AppUserId == userId && s.DateConfirmed != null, ct);
    }
}
