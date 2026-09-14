using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Which of an event's files a person may see (item 235 phase 11).
/// </summary>
/// <remarks>
/// <para><b>Three widening circles.</b> Anybody sees the <c>Public</c> files of an event on the public
/// site. A guest the venue has confirmed also sees <c>Attendees</c> files — the guest pack with the
/// parking and the door code. The event's own people, and the group that runs it, see everything,
/// <c>Staff</c> files included.</para>
///
/// <para><b>A request is not a place.</b> A guest still waiting on the venue sees what a stranger
/// sees: the guest pack says where the key safe is, and that is for people who are coming.</para>
/// </remarks>
public static class EventFileAudiences
{
    /// <summary>The widest audience this person may see, or null when they may see nothing.</summary>
    public static async Task<EventFileAudience?> WidestAsync(
        BenDataContext db, Access.HostedEventAccess access, HostedEvent hosted, Guid viewerId, CancellationToken ct)
    {
        if (viewerId != Guid.Empty)
        {
            if (await access.CanReadEventAsync(viewerId, hosted.OrganizationId, ct)
                || await db.HostedEventStaff.AnyAsync(s => s.HostedEventId == hosted.Id && s.AppUserId == viewerId && s.DateConfirmed != null, ct))
                return EventFileAudience.Staff;

            if (await db.HostedEventBookings.AnyAsync(b => b.HostedEventId == hosted.Id && b.LeadAppUserId == viewerId
                                                        && b.Status == HostedEventBookingStatus.Confirmed, ct))
                return EventFileAudience.Attendees;
        }

        return HostedEventStates.OnThePublicSite.Contains(hosted.LifecycleState) ? EventFileAudience.Public : null;
    }

    /// <summary>Whether a file meant for <paramref name="audience"/> reaches somebody who may see <paramref name="widest"/>.</summary>
    /// <remarks>Staff is the narrowest audience and the widest reach, so it is compared by inclusion rather than by number.</remarks>
    public static bool Reaches(EventFileAudience audience, EventFileAudience? widest) => (audience, widest) switch
    {
        (_, null) => false,
        (_, EventFileAudience.Staff) => true,
        (EventFileAudience.Staff, _) => false,
        (EventFileAudience.Attendees, EventFileAudience.Attendees) => true,
        (EventFileAudience.Public, _) => true,
        _ => false,
    };
}
