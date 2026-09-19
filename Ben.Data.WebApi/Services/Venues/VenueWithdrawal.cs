using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Venues;

/// <summary>
/// What happens when a venue takes back its yes (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Every event resting on the grant that is still to happen stops</b>, in one save: its
/// state becomes Venue withdrew, its guests' passes are revoked with the venue's reason on them, and
/// its credit comes back <b>whatever the timing</b> (decision 8) — the organizer did nothing wrong.
/// A draft loses only its permission, and its readiness list says why.</para>
///
/// <para><b>What already happened is left alone.</b> An event that has ended or been archived is a
/// record of a night that took place, and withdrawing permission for it afterwards means nothing.</para>
///
/// <para>Letters go after the save, from the controller: a withdrawal that failed to save must not
/// have told forty guests their weekend is off.</para>
/// </remarks>
public static class VenueWithdrawal
{
    /// <summary>The states a withdrawal stops. Drafts lose their permission without changing state.</summary>
    private static readonly HostedEventLifecycleState[] Stops =
        [HostedEventLifecycleState.Published, HostedEventLifecycleState.Live];

    /// <summary>One event the withdrawal touched, for the letters and the note.</summary>
    public sealed record Stopped(Guid HostedEventId, string Name, Guid OrganizationId, bool CreditReturned);

    /// <summary>Revokes the grant and stops what rests on it. The caller saves.</summary>
    public static async Task<IReadOnlyList<Stopped>> WithdrawAsync(
        BenDataContext db, OrganizationVenueGrant grant, Guid actorId, string reason,
        HostedEventCalendarSync sync, DateTime now, CancellationToken ct)
    {
        grant.RevokedUtc = now;
        grant.RevokedByAppUserId = actorId;
        grant.RevokedReason = reason;
        grant.DateUpdated = now;
        grant.UpdatedByAppUserId = actorId;

        var events = await db.HostedEvents
            .Include(e => e.Nights)
            .Where(e => e.VenueGrantId == grant.Id && Stops.Contains(e.LifecycleState))
            .ToListAsync(ct);

        var stopped = new List<Stopped>();

        foreach (var hosted in events)
        {
            hosted.LifecycleState = HostedEventLifecycleState.VenueWithdrawn;
            hosted.CancelledAtUtc = now;
            hosted.CancelledReason = reason;
            hosted.DateUpdated = now;
            hosted.UpdatedByAppUserId = actorId;

            // Regardless of timing. The forty-eight hour rule is there to stop an organizer cancelling
            // on the Friday afternoon for free; here the organizer cancelled nothing.
            var credit = await EventCredits.SpentOnAsync(db, hosted.Id, ct);
            if (credit is not null)
            {
                EventCredits.Unspend(credit, actorId, now);
                hosted.FirstPublishedUtc = null;
            }

            var bookingIds = await db.HostedEventBookings
                .Where(b => b.HostedEventId == hosted.Id)
                .Select(b => b.Id)
                .ToListAsync(ct);
            foreach (var bookingId in bookingIds)
                await EventPasses.RevokeAllAsync(db, bookingId, actorId, $"The venue withdrew: {reason}", ct);

            await sync.SyncAsync(db, hosted, actorId, ct);

            stopped.Add(new(hosted.Id, hosted.Name, hosted.OrganizationId, credit is not null));
        }

        return stopped;
    }
}
