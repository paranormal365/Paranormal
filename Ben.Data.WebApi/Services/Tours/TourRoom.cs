using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>
/// A tour date's room: the people checked in on the walk, and its guides, staying in touch
/// (event-scenario-gaps).
/// </summary>
/// <remarks>
/// <para>Ben, 10/20/2026: <i>"You need to be able to stay in touch with people checked in with the event
/// or tour if you both are checked in. Also, maybe if there is a leader or organizer or guide, their
/// response or text is given a different color."</i></para>
///
/// <para><b>Checked in, not signed up.</b> A guest is in the room once the guide has scanned them in, and
/// stays in it afterwards: the point is keeping in touch with the people who were actually there. A seat
/// that was reserved and never used is not a place in the room.</para>
///
/// <para><b>The guides and the group running it are in it too</b>, without checking in — they are the
/// ones running the walk — and their posts carry their role.</para>
///
/// <para><b>Words only.</b> A walk's room is for "we're at the corner" and "thanks, everyone"; photos
/// belong in each person's own sessions and evidence.</para>
/// </remarks>
public static class TourRoom
{
    /// <summary>How long after the walk ends the room stays open for posting.</summary>
    public static readonly TimeSpan OpenAfterTheWalk = TimeSpan.FromDays(7);

    /// <summary>How long before the walk starts the room opens (the guide may check people in early).</summary>
    public static readonly TimeSpan OpensBefore = TimeSpan.FromHours(3);

    /// <summary>The longest post.</summary>
    public const int MaxBody = 1000;

    /// <param name="IsMember">In the room at all.</param>
    /// <param name="Role">"Guide" or "Organizer" for the people running it; null for a guest.</param>
    public sealed record Standing(bool IsMember, string? Role)
    {
        public static readonly Standing Nobody = new(false, null);
        public bool IsTeam => Role is not null;
    }

    /// <summary>Whether this person is in the room, and as what.</summary>
    public static async Task<Standing> StandingAsync(
        BenDataContext db, Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security,
        OrgCalendarEvent date, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty || date.TourId is null) return Standing.Nobody;

        if (await db.OrgCalendarEventGuides.AsNoTracking().AnyAsync(g => g.OrgCalendarEventId == date.Id && g.AppUserId == userId, ct))
            return new(true, "Guide");

        var runsIt = await db.OrganizationUserMemberships.AsNoTracking().AnyAsync(m =>
                         m.OrganizationId == date.OrganizationId && m.AppUserId == userId && m.IsActive
                      && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator), ct)
                  || await security.HasAccessAsync(userId, date.OrganizationId,
                         OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update, ct);
        if (runsIt) return new(true, "Organizer");

        var checkedIn = await db.OrgCalendarEventAttendees.AsNoTracking().AnyAsync(a =>
            a.OrgCalendarEventId == date.Id && a.AppUserId == userId && a.CheckedInUtc != null, ct);
        return checkedIn ? new(true, null) : Standing.Nobody;
    }

    /// <summary>Why nobody can post now, or null when the room is open.</summary>
    public static string? WhyClosed(OrgCalendarEvent date, DateTime now)
    {
        if (now < date.StartDateTime - OpensBefore) return "The room opens on the day of the walk.";
        if (now > date.EndDateTime + OpenAfterTheWalk) return "The room closed a week after the walk. You can still read it.";
        return null;
    }

    /// <summary>What each author is on this walk, for the people running it.</summary>
    public static async Task<Dictionary<Guid, string>> RolesAsync(
        BenDataContext db, OrgCalendarEvent date, IReadOnlyCollection<Guid> authorIds, CancellationToken ct)
    {
        var roles = new Dictionary<Guid, string>();
        if (authorIds.Count == 0) return roles;

        foreach (var id in await db.OrganizationUserMemberships.AsNoTracking()
                     .Where(m => m.OrganizationId == date.OrganizationId && m.IsActive && authorIds.Contains(m.AppUserId)
                              && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator))
                     .Select(m => m.AppUserId).ToListAsync(ct))
            roles[id] = "Organizer";

        foreach (var id in await db.OrgCalendarEventGuides.AsNoTracking()
                     .Where(g => g.OrgCalendarEventId == date.Id && authorIds.Contains(g.AppUserId))
                     .Select(g => g.AppUserId).ToListAsync(ct))
            roles[id] = "Guide";

        return roles;
    }
}
