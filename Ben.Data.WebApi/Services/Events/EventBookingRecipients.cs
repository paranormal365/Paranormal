using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Who should hear about one event's bookings, and how (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>Everybody who may decide, and nobody else.</b> The same question the booking board
/// asks — through <see cref="Access.HostedEventAccess"/> — so a letter can never reach somebody the
/// board would refuse, and a steward handed only the door is never written to about requests they
/// cannot answer.</para>
///
/// <para><b>Candidates first, then the question.</b> Asking every member of a large group whether
/// they may decide would be a permission check per member per event per pass; so the candidates
/// are the group's active members and the event's accepted staff, and the check is asked only of
/// them, only for events that have something to say.</para>
/// </remarks>
public static class EventBookingRecipients
{
    /// <summary>Somebody to write to, with the address to write to and how they want it.</summary>
    public sealed record Recipient(Guid AppUserId, string Email, string? Name, EventBookingAlertMode Mode);

    public static async Task<IReadOnlyList<Recipient>> ForAsync(
        BenDataContext db, Access.HostedEventAccess access, HostedEvent ev, CancellationToken ct)
    {
        var members = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == ev.OrganizationId && m.IsActive)
            .Select(m => m.AppUserId)
            .ToListAsync(ct);

        var staff = await db.HostedEventStaff.AsNoTracking()
            .Where(s => s.HostedEventId == ev.Id && s.AppUserId != null
                     && s.DateConfirmed != null && s.Decides)
            .Select(s => s.AppUserId!.Value)
            .ToListAsync(ct);

        var candidates = members.Concat(staff).Distinct().ToList();
        if (candidates.Count == 0) return [];

        var people = await db.Users.AsNoTracking()
            .Where(u => candidates.Contains(u.Id) && u.Email != null && u.DateClosed == null)
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToListAsync(ct);

        var preferences = await db.EventBookingAlertPreferences.AsNoTracking()
            .Where(p => p.OrganizationId == ev.OrganizationId && candidates.Contains(p.AppUserId))
            .ToDictionaryAsync(p => p.AppUserId, p => p.Mode, ct);

        var recipients = new List<Recipient>();

        foreach (var person in people)
        {
            if (string.IsNullOrWhiteSpace(person.Email)) continue;

            // The board's own question, asked of the board's own rules.
            if (!await access.CanDecideBookingsAsync(person.Id, ev.OrganizationId, ev.Id, db, ct))
                continue;

            recipients.Add(new Recipient(
                person.Id,
                person.Email,
                person.DisplayName,
                // No row is the loud default: the failure worth avoiding is the silent one.
                preferences.GetValueOrDefault(person.Id, EventBookingAlertMode.AsItHappens)));
        }

        return recipients;
    }
}
