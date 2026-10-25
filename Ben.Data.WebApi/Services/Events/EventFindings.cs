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
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return false;

        if (!row.Residence) return row.IsPublic;
        return userId != Guid.Empty && await WasThereAsync(db, row.OrganizationId, row.HostedEventId, calendarEventId, userId, ct);
    }

    /// <summary>Whether a calendar event's findings are open to anybody at all, signed out included.</summary>
    public static async Task<bool> OpenToAllAsync(BenDataContext db, Guid calendarEventId, CancellationToken ct)
        => await MaySeeAsync(db, calendarEventId, Guid.Empty, ct);

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
