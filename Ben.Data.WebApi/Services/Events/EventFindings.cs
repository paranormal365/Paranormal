using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Who may see an event's findings: its accepted evidence, the sessions shown with its results, and the votes on
/// them (10/25/2026). One rule, so every surface — the event page, the file route, the app's results — answers
/// the same.
/// </summary>
/// <remarks>
/// <para><b>Findings from a private home never go public.</b> Before this, accepted evidence at an event held at
/// a private residence was public on the event page — the place's kind was never checked, though sessions'
/// was. Only the people who were there see those: the organizers, the event's staff, and guests with a
/// confirmed place (or, at an ordinary calendar event, members and the people who said they were coming).</para>
/// </remarks>
public static class EventFindings
{
    /// <summary>Whether <paramref name="userId"/> (empty for signed out) may see this calendar event's findings.</summary>
    public static async Task<bool> MaySeeAsync(BenDataContext db, Guid calendarEventId, Guid userId, CancellationToken ct)
    {
        var row = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.Id == calendarEventId)
            .Select(e => new
            {
                e.IsPublic, e.OrganizationId, e.HostedEventId,
                Residence = e.Place != null && e.Place.Kind == PlaceKind.PrivateResidence,
                // A hosted event's own standing, not its calendar row's: archiving takes the row off the public
                // site, and its findings must stay as they were when it ended (10/25/2026).
                Hosted = db.HostedEvents.Where(h => h.Id == e.HostedEventId)
                    .Select(h => new { h.FindingsPublishedUtc, h.LifecycleState }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return false;

        // An ordinary calendar event keeps "accepted is public", except at a private home.
        if (row.HostedEventId is null)
            return row.Residence
                ? userId != Guid.Empty && await WasThereAsync(db, row.OrganizationId, null, calendarEventId, userId, ct)
                : row.IsPublic;

        // A hosted event's are the people who were there's until the organizer publishes them (10/25/2026).
        if (row.Hosted is null || row.Hosted.LifecycleState == HostedEventLifecycleState.Removed) return false;
        if (!row.Residence && row.Hosted.FindingsPublishedUtc is not null && ShowsFindings.Contains(row.Hosted.LifecycleState))
            return true;
        return userId != Guid.Empty && await WasThereAsync(db, row.OrganizationId, row.HostedEventId, calendarEventId, userId, ct);
    }

    /// <summary>The states whose published findings anybody may see: on the public site, or archived after it.</summary>
    public static readonly HostedEventLifecycleState[] ShowsFindings =
    [
        HostedEventLifecycleState.Published, HostedEventLifecycleState.Live,
        HostedEventLifecycleState.Ended, HostedEventLifecycleState.Archived,
    ];

    /// <summary>Whether a calendar event's findings are open to anybody at all, signed out included.</summary>
    public static async Task<bool> OpenToAllAsync(BenDataContext db, Guid calendarEventId, CancellationToken ct)
        => await MaySeeAsync(db, calendarEventId, Guid.Empty, ct);

    /// <summary>Why the organizer can't publish this event's findings now, or null when they can.</summary>
    /// <param name="firstStartsUtc">When the event's first night starts: findings arrive from then on.</param>
    public static string? WhyNotPublish(Ben.Data.Source.Entities.HostedEvent ev, PlaceKind? placeKind, DateTime firstStartsUtc, DateTime now)
    {
        if (placeKind == PlaceKind.PrivateResidence)
            return "This event was at a private home, so its findings stay with the people who were there.";
        if (HostedEventStates.CalledOff.Contains(ev.LifecycleState))
            return "This event was called off, so it has no findings to share.";
        if (now < firstStartsUtc)
            return "Findings can be shared once the event has started.";
        return null;
    }

    /// <summary>
    /// Makes the findings public, or takes them back to the people who were there, and with them the accepted
    /// evidence files. The caller saves. A file a guest put on the location's page themselves stays as they left it.
    /// </summary>
    public static async Task SetPublishedAsync(BenDataContext db, Ben.Data.Source.Entities.HostedEvent ev, bool published,
        Guid userId, DateTime now, CancellationToken ct)
    {
        ev.FindingsPublishedUtc = published ? now : null;
        ev.FindingsPublishedByAppUserId = published ? userId : null;
        if (!published) ev.FindingsOnPlace = FindingsOnPlace.None;
        ev.DateUpdated = now;
        ev.UpdatedByAppUserId = userId;

        var files = await db.EventEvidenceSubmissions
            .Where(s => s.OrgCalendarEvent.HostedEventId == ev.Id && s.Status == EvidenceSubmissionStatus.Accepted
                     && (published || s.PublishedToPlaceAtUtc == null))
            .Select(s => s.UploadFile)
            .ToListAsync(ct);
        foreach (var f in files) f.IsPublic = published;
    }

    /// <summary>The people who were there: the group, the event's staff, and its confirmed guests or RSVPs.</summary>
    public static async Task<bool> WasThereAsync(
        BenDataContext db, Guid organizationId, Guid? hostedEventId, Guid calendarEventId, Guid userId, CancellationToken ct)
    {
        if (await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive, ct))
            return true;

        if (hostedEventId is { } hostedId)
            return await db.HostedEventStaff.AsNoTracking().AnyAsync(s => s.HostedEventId == hostedId && s.AppUserId == userId, ct)
                || await db.HostedEventBookings.AsNoTracking().AnyAsync(
                       b => b.HostedEventId == hostedId && b.Status == HostedEventBookingStatus.Confirmed
                         && (b.LeadAppUserId == userId || b.Guests.Any(g => g.AppUserId == userId)), ct);

        return await db.OrgCalendarEventAttendees.AsNoTracking()
                   .AnyAsync(a => a.OrgCalendarEventId == calendarEventId && a.AppUserId == userId
                               && a.RsvpStatus == RsvpStatus.Accepted, ct)
            || await db.EventAttendanceInvites.AsNoTracking()
                   .AnyAsync(i => i.OrgCalendarEventId == calendarEventId && i.ConfirmedByAppUserId == userId
                               && i.DateConfirmed != null, ct);
    }
}
