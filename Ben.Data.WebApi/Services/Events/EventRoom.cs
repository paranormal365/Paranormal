using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Who is in an event's room, when it is open, and who looks after it (item 235 phase 11).
/// </summary>
/// <remarks>
/// <para><b>The people at the event, and nobody else.</b> Guests the venue has confirmed, the event's
/// helpers, the group running it, and the venue's own people when the venue lent them. A request
/// still waiting on the venue is not a place at the event and not a place in its room.</para>
///
/// <para><b>What is posted belongs to whoever posted it.</b> Ben, 2026-09-13: "Uploads during an event
/// belong to the uploader but can be sent to and shared with event organizer and venue." A photo is
/// the uploader's own file; sending it to the organizer or the venue is the uploader's choice, made
/// with a share, and taking the post down leaves the file in their library.</para>
///
/// <para><b>Open from publishing until a week after the last night</b>, or until the host closes it
/// sooner. After that it can still be read — the photos from the weekend are the point — but not
/// added to.</para>
/// </remarks>
public static class EventRoom
{
    /// <summary>How long after the last night a room stays open for posting.</summary>
    public static readonly TimeSpan OpenAfterTheLastNight = TimeSpan.FromDays(7);

    /// <summary>The longest body a room message may have.</summary>
    public const int MaxBody = 1000;

    /// <param name="IsTeam">The organizing group or a helper at the event — not a guest.</param>
    public sealed record Standing(bool IsMember, bool CanModerate, bool IsTeam = false);

    /// <summary>Whether this person may add photos and videos, under the event's setting.</summary>
    public static bool MayAddPhotos(HostedEvent hosted, Standing standing)
        => standing.IsMember && (standing.IsTeam || hosted.PhotoPosting == EventPhotoPosting.TeamAndGuests);

    /// <summary>Whether this person is in the room, and whether they look after it.</summary>
    public static async Task<Standing> StandingAsync(
        BenDataContext db, Access.HostedEventAccess access, HostedEvent hosted, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return new(false, false);

        var moderates = await access.CanEditEventAsync(userId, hosted.OrganizationId, ct)
                     || await access.CanDecideBookingsAsync(userId, hosted.OrganizationId, hosted.Id, db, ct);
        if (moderates) return new(true, true, true);

        var team = await access.CanReadEventAsync(userId, hosted.OrganizationId, ct)
                || await db.HostedEventStaff.AnyAsync(s => s.HostedEventId == hosted.Id && s.AppUserId == userId && s.DateConfirmed != null, ct)
                || await access.CanReadBookingsAsync(userId, hosted.OrganizationId, hosted.Id, db, ct);
        if (team) return new(true, false, true);

        var guest = await db.HostedEventBookings.AnyAsync(b => b.HostedEventId == hosted.Id && b.LeadAppUserId == userId
                                                            && b.Status == HostedEventBookingStatus.Confirmed, ct);
        return new(guest, false, false);
    }

    /// <summary>Why nobody can post in this room now, or null when it is open.</summary>
    public static string? WhyClosed(HostedEvent hosted, IReadOnlyList<DateTime> nightDates, DateTime now)
    {
        if (!HostedEventStates.OnThePublicSite.Contains(hosted.LifecycleState))
            return HostedEventStates.CalledOff.Contains(hosted.LifecycleState)
                ? "This event was called off, so its room is closed."
                : "The room opens when the event is published.";

        if (hosted.RoomClosedUtc is { } closed)
            return $"The organizers closed the room on {closed:MM/dd/yyyy}. You can still look back through it.";

        if (nightDates.Count > 0 && now.Date > nightDates.Max().Date + OpenAfterTheLastNight)
            return "The room closed a week after the last night. You can still look back through it.";

        return null;
    }

    /// <summary>
    /// Sends a post's file to the event's organizer and, when there is one on the site, its venue —
    /// as shares, so the file stays the uploader's. The caller saves.
    /// </summary>
    /// <returns>The groups it went to, by name.</returns>
    public static async Task<IReadOnlyList<string>> ShareWithTheHostsAsync(
        BenDataContext db, HostedEvent hosted, Guid uploadFileId, Guid uploaderId, DateTime now, CancellationToken ct)
    {
        var groups = new List<Guid> { hosted.OrganizationId };
        if (hosted.VenueGrantId is Guid grantId
            && await db.OrganizationVenueGrants.AsNoTracking().FirstOrDefaultAsync(g => g.Id == grantId && g.RevokedUtc == null, ct) is { } grant)
            groups.Add(grant.VenueOrganizationId);

        foreach (var orgId in groups.Distinct())
        {
            var share = await db.UploadFileOrganizationShares
                .FirstOrDefaultAsync(s => s.UploadFileId == uploadFileId && s.OrganizationId == orgId, ct);
            if (share is null)
            {
                db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
                {
                    Id = Guid.NewGuid(), UploadFileId = uploadFileId, OrganizationId = orgId,
                    SharedByAppUserId = uploaderId, Visibility = FileShareVisibility.OrgMembers, IsActive = true,
                    DateCreated = now, CreatedByAppUserId = uploaderId,
                });
            }
            else if (!share.IsActive)
            {
                share.IsActive = true;
                share.RemovalDate = null;
                share.RemovedByAppUserId = null;
                share.DateUpdated = now;
                share.UpdatedByAppUserId = uploaderId;
            }
        }

        return await db.Organizations.AsNoTracking()
            .Where(o => groups.Contains(o.Id)).Select(o => o.Name).ToListAsync(ct);
    }
}
