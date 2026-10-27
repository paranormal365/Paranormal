using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>One copy somebody may make of a session's recordings: their own, or a group's.</summary>
/// <param name="Label">How the page names it: "Your own copy", "Hollow Creek Paranormal's copy".</param>
public sealed record ScanScope(AnalysisScope Scope, Guid? OrganizationId, string Label);

/// <summary>Whether somebody may scan a session, and for whom.</summary>
/// <param name="Scope">The copy the scan works on, when allowed.</param>
/// <param name="Message">The sentence to show, when refused.</param>
public sealed record ScanDecision(ScanRefusal Refusal, ScanScope? Scope, string? Message)
{
    public bool Allowed => Refusal == ScanRefusal.None;
}

/// <summary>
/// Who may scan a field session for EVPs, and whose copy they work on (phase 9, 10/27/2026).
/// </summary>
/// <remarks>
/// <para><b>Ben's list (planning, 10/26/2026):</b> the person who recorded it; the group behind the
/// investigation or calendar event it was sent to; a hosted event's team when it was sent to that
/// event; and the verified venue of a public place it was published to. Public visitors and share-link
/// viewers never.</para>
///
/// <para><b>Narrower than reading, on purpose.</b> An open investigation lets any signed-in person
/// read what was sent to it (<see cref="FieldSessionAccess.MayContributeAsync"/>'s public door).
/// That door is for watching. Scanning makes copies and markers, so here only somebody who was there
/// or belongs to the group may do it.</para>
///
/// <para><b>The venue's door needs the session to be really public</b>: published to a public place
/// and its media approved for show. Until then it is somebody's private night, whatever place it
/// names.</para>
/// </remarks>
public static class FieldSessionAnalysisAccess
{
    /// <summary>Every copy this person may make of this session, their own first.</summary>
    public static async Task<IReadOnlyList<ScanScope>> ScopesAsync(
        BenDataContext db, FieldSessionUpload session, Guid userId, CancellationToken ct)
    {
        var scopes = new List<ScanScope>();
        if (userId == Guid.Empty) return scopes;

        if (session.SubmittedByAppUserId == userId)
            scopes.Add(new ScanScope(AnalysisScope.Person, null, "Your own copy"));

        var groups = new List<Guid>();

        if (session.InvestigationId is Guid investigationId)
        {
            var organizationId = await db.Investigations.AsNoTracking()
                .Where(i => i.Id == investigationId).Select(i => (Guid?)i.OrganizationId).FirstOrDefaultAsync(ct);
            if (organizationId is Guid org
                && (await db.InvestigationAttendees.AsNoTracking()
                        .AnyAsync(a => a.InvestigationId == investigationId && a.AppUserId == userId, ct)
                    || await IsActiveMemberAsync(db, org, userId, ct)))
                groups.Add(org);
        }

        if (session.OrgCalendarEventId is Guid eventId)
        {
            var org = await db.OrgCalendarEvents.AsNoTracking()
                .Where(e => e.Id == eventId).Select(e => e.OrganizationId).FirstOrDefaultAsync(ct);
            if (await FieldSessionAccess.IsEventTeamAsync(db, eventId, userId, ct, org)) groups.Add(org);
        }

        if (session.HostedEventId is Guid hostedId)
        {
            var org = await db.HostedEvents.AsNoTracking()
                .Where(h => h.Id == hostedId).Select(h => h.OrganizationId).FirstOrDefaultAsync(ct);
            if (await FieldSessionAccess.IsHostedTeamAsync(db, hostedId, userId, ct, org)) groups.Add(org);
        }

        if (session.PlaceId is Guid placeId
            && session.MediaReviewState == FeedMediaReviewState.Approved
            && await IsPublishedToAPublicPlaceAsync(db, session, ct))
        {
            var venues = await db.OrganizationVenueProfiles.AsNoTracking()
                .Where(v => v.PlaceId == placeId && v.VerifiedUtc != null
                         && db.OrganizationUserMemberships.Any(m => m.OrganizationId == v.OrganizationId
                                                                 && m.AppUserId == userId && m.IsActive))
                .Select(v => v.OrganizationId)
                .ToListAsync(ct);
            groups.AddRange(venues);
        }

        var distinct = groups.Where(g => g != Guid.Empty).Distinct().ToList();
        if (distinct.Count > 0)
        {
            var names = await db.Organizations.AsNoTracking()
                .Where(o => distinct.Contains(o.Id))
                .Select(o => new { o.Id, o.Name })
                .ToDictionaryAsync(o => o.Id, o => o.Name, ct);
            foreach (var org in distinct)
                scopes.Add(new ScanScope(AnalysisScope.Organization, org,
                    names.TryGetValue(org, out var name) ? $"{name}'s copy" : "The group's copy"));
        }

        return scopes;
    }

    /// <summary>
    /// Whether this person may scan this session, and whose copy they get: the one asked for, or the
    /// first they may have.
    /// </summary>
    /// <remarks>Does not look at the recordings; a caller with none to scan says <see cref="ScanRefusal.NoAudio"/>.</remarks>
    public static async Task<ScanDecision> MayScanAsync(
        BenDataContext db, FieldSessionUpload? session, Guid userId,
        AnalysisScope? requested, Guid? requestedOrganizationId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return Refuse(ScanRefusal.NotSignedIn);
        if (session is null) return Refuse(ScanRefusal.NoSuchSession);

        var scopes = await ScopesAsync(db, session, userId, ct);
        if (scopes.Count == 0)
            return Refuse(await IsPublishedToAPublicPlaceAsync(db, session, ct)
                ? ScanRefusal.PublicViewOnly
                : ScanRefusal.NotYours);

        if (requested is null) return new ScanDecision(ScanRefusal.None, scopes[0], null);

        var match = scopes.FirstOrDefault(s => s.Scope == requested
            && (requested == AnalysisScope.Person || s.OrganizationId == requestedOrganizationId));
        return match is null
            ? new ScanDecision(ScanRefusal.NotYours, null,
                requested == AnalysisScope.Person
                    ? "Only the person who recorded this session can keep a copy of it in their own files. Scan it for your group instead."
                    : "You can't scan this session for that group.")
            : new ScanDecision(ScanRefusal.None, match, null);
    }

    /// <summary>The sentence for a refusal.</summary>
    public static string Describe(ScanRefusal refusal) => refusal switch
    {
        ScanRefusal.NotSignedIn    => "Sign in to scan this session.",
        ScanRefusal.NoSuchSession  => "This session isn't here.",
        ScanRefusal.NotYours       => "Only the person who recorded this session and the team it was sent to can scan it.",
        ScanRefusal.PublicViewOnly => "Only the person who recorded this, their group, or the location's venue can scan it.",
        ScanRefusal.NoAudio        => "This session has no sound recordings to scan.",
        _                          => "",
    };

    /// <summary>
    /// Whether a group's file that came from a session may be seen by this person: they may scan that
    /// session for that group. Lets an event's confirmed staff, who are not members, open the group's copy.
    /// </summary>
    public static async Task<bool> MayViewGroupCopyAsync(
        BenDataContext db, Guid sessionId, Guid organizationId, Guid userId, CancellationToken ct)
    {
        var session = await db.FieldSessionUploads.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return false;
        var scopes = await ScopesAsync(db, session, userId, ct);
        return scopes.Any(s => s.Scope == AnalysisScope.Organization && s.OrganizationId == organizationId);
    }

    private static ScanDecision Refuse(ScanRefusal refusal) => new(refusal, null, Describe(refusal));

    private static Task<bool> IsActiveMemberAsync(BenDataContext db, Guid organizationId, Guid userId, CancellationToken ct)
        => db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive, ct);

    /// <summary>The public archive's own rule (<c>PublicFieldSessionController.Publishable</c>).</summary>
    private static async Task<bool> IsPublishedToAPublicPlaceAsync(BenDataContext db, FieldSessionUpload session, CancellationToken ct)
        => session.PublishedAtUtc is not null && session.PlaceId is Guid placeId
           && await db.Places.AsNoTracking().AnyAsync(p => p.Id == placeId && p.Kind == PlaceKind.PublicLocation, ct);
}
