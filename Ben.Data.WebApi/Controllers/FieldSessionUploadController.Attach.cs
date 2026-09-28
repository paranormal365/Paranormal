using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Where a session goes when it is sent up (item 252): an investigation, as always, or now a tour
/// date, a calendar event or a hosted event — directly, or through the lead's launch it was joined
/// from.
/// </summary>
/// <remarks>
/// <para><b>Days later is fine.</b> Ben, 2026-09-28: "they should be able to upload their evidence
/// days after the event because they might not see the evidence until review later." Nothing here
/// asks the time. Being registered, a member, or let into the launch is what counts, and none of
/// those lapses when the night ends — only the launch's card leaves the feed.</para>
///
/// <para><b>Sending is not reading.</b> Anybody registered may send a session to a tour date; only
/// the group's team for it may read what the guests sent — the same line item 248 drew between a
/// guest pass's write door and the read door, for the same reason.</para>
/// </remarks>
public sealed partial class FieldSessionUploadController
{
    /// <summary>What a session is filed under, and whose storage it goes into.</summary>
    private sealed record SessionTarget(
        Guid? InvestigationId, Guid? OrgCalendarEventId, Guid? HostedEventId, Guid? FieldLaunchId, Guid OrganizationId)
    {
        public static readonly SessionTarget Own = new(null, null, null, null, Guid.Empty);
    }

    private abstract record TargetResult
    {
        public sealed record Found(SessionTarget Target) : TargetResult;
        /// <summary>Answered as 404: whether somebody else's investigation exists is not for probing.</summary>
        public sealed record Hidden : TargetResult;
        /// <summary>A sentence for the person: why this cannot go there, and that it is still on their phone.</summary>
        public sealed record Refused(string Reason) : TargetResult;
    }

    private const string StillOnThePhone = "It's still on your phone — send it as your own session instead.";

    private static async Task<TargetResult> ResolveTargetAsync(
        BenDataContext db, Guid userId, Guid? investigationId, Guid? orgCalendarEventId, Guid? hostedEventId,
        Guid? fieldLaunchId, CancellationToken ct)
    {
        // Joined from a launch: the launch says where it goes, and the person must be one of its people.
        if (fieldLaunchId is Guid launchId && launchId != Guid.Empty)
        {
            var launch = await db.FieldLaunches.AsNoTracking().FirstOrDefaultAsync(l => l.Id == launchId, ct);
            if (launch is null)
                return new TargetResult.Refused("That group session is no longer on the server. " + StillOnThePhone);
            var isOneOfItsPeople = launch.IsPublic || launch.LaunchedByAppUserId == userId
                || await db.FieldLaunchRecipients.AsNoTracking()
                       .AnyAsync(r => r.FieldLaunchId == launchId && r.AppUserId == userId, ct);
            if (!isOneOfItsPeople)
                return new TargetResult.Refused("You weren't let into that group session, so this can't go to it. " + StillOnThePhone);
            return new TargetResult.Found(new SessionTarget(
                launch.InvestigationId, launch.OrgCalendarEventId, launch.HostedEventId, launchId, launch.OrganizationId));
        }

        if (investigationId is Guid target && target != Guid.Empty)
        {
            var organization = await db.Investigations.AsNoTracking()
                .Where(i => i.Id == target).Select(i => (Guid?)i.OrganizationId).FirstOrDefaultAsync(ct);
            if (organization is null || !await MayWriteAsync(db, target, userId, ct)) return new TargetResult.Hidden();
            return new TargetResult.Found(new SessionTarget(target, null, null, null, organization.Value));
        }

        if (orgCalendarEventId is Guid eventId && eventId != Guid.Empty)
        {
            var organization = await db.OrgCalendarEvents.AsNoTracking()
                .Where(e => e.Id == eventId).Select(e => (Guid?)e.OrganizationId).FirstOrDefaultAsync(ct);
            if (organization is null)
                return new TargetResult.Refused("That event is no longer on the server. " + StillOnThePhone);
            if (!await MayAttachToEventAsync(db, eventId, organization.Value, userId, ct))
                return new TargetResult.Refused("You aren't registered for that event, so this can't go to it. " + StillOnThePhone);
            return new TargetResult.Found(new SessionTarget(null, eventId, null, null, organization.Value));
        }

        if (hostedEventId is Guid hostedId && hostedId != Guid.Empty)
        {
            var organization = await db.HostedEvents.AsNoTracking()
                .Where(h => h.Id == hostedId).Select(h => (Guid?)h.OrganizationId).FirstOrDefaultAsync(ct);
            if (organization is null)
                return new TargetResult.Refused("That event is no longer on the server. " + StillOnThePhone);
            if (!await MayAttachToHostedAsync(db, hostedId, organization.Value, userId, ct))
                return new TargetResult.Refused("You don't have a confirmed place at that event, so this can't go to it. " + StillOnThePhone);
            return new TargetResult.Found(new SessionTarget(null, null, hostedId, null, organization.Value));
        }

        return new TargetResult.Found(SessionTarget.Own);
    }

    /// <summary>Who may SEND to a tour date or calendar event: its guests, guides, the group, or its launch's people.</summary>
    private static async Task<bool> MayAttachToEventAsync(
        BenDataContext db, Guid eventId, Guid organizationId, Guid userId, CancellationToken ct)
        => await db.OrgCalendarEventAttendees.AsNoTracking()
               .AnyAsync(a => a.OrgCalendarEventId == eventId && a.AppUserId == userId && a.RsvpStatus == RsvpStatus.Accepted, ct)
        || await IsEventTeamAsync(db, eventId, userId, ct, organizationId)
        || await IsLaunchedPersonAsync(db, userId, l => l.OrgCalendarEventId == eventId, ct);

    /// <summary>Who may SEND to a hosted event: confirmed bookings and their named guests, its team, or its launch's people.</summary>
    private static async Task<bool> MayAttachToHostedAsync(
        BenDataContext db, Guid hostedId, Guid organizationId, Guid userId, CancellationToken ct)
    {
        var confirmed = db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == hostedId && b.Status == HostedEventBookingStatus.Confirmed);
        return await confirmed.AnyAsync(b => b.LeadAppUserId == userId, ct)
            || await db.HostedEventBookingGuests.AsNoTracking()
                   .AnyAsync(g => g.AppUserId == userId && confirmed.Any(b => b.Id == g.HostedEventBookingId), ct)
            || await IsHostedTeamAsync(db, hostedId, userId, ct, organizationId)
            || await IsLaunchedPersonAsync(db, userId, l => l.HostedEventId == hostedId, ct);
    }

    /// <summary>Who may READ a tour date's or event's sessions: the group, and the date's guides.</summary>
    private static async Task<bool> IsEventTeamAsync(
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
    private static async Task<bool> IsHostedTeamAsync(
        BenDataContext db, Guid hostedId, Guid userId, CancellationToken ct, Guid? organizationId = null)
    {
        var organization = organizationId ?? await db.HostedEvents.AsNoTracking()
            .Where(h => h.Id == hostedId).Select(h => h.OrganizationId).FirstOrDefaultAsync(ct);
        return await db.OrganizationUserMemberships.AsNoTracking()
                   .AnyAsync(m => m.OrganizationId == organization && m.AppUserId == userId && m.IsActive, ct)
            || await db.HostedEventStaff.AsNoTracking()
                   .AnyAsync(s => s.HostedEventId == hostedId && s.AppUserId == userId && s.DateConfirmed != null, ct);
    }

    /// <summary>Somebody a launch of this thing reached, or let in — whenever that was.</summary>
    private static Task<bool> IsLaunchedPersonAsync(
        BenDataContext db, Guid userId, System.Linq.Expressions.Expression<Func<Source.Entities.FieldLaunch, bool>> ofThis,
        CancellationToken ct)
    {
        var launches = db.FieldLaunches.AsNoTracking().Where(ofThis).Select(l => l.Id);
        return db.FieldLaunchRecipients.AsNoTracking()
            .AnyAsync(r => r.AppUserId == userId && launches.Contains(r.FieldLaunchId), ct);
    }
}
