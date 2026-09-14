using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// IsHaunted taking an event off the site, and the appeal that can bring it back (item 235 phase 17b).
/// </summary>
/// <remarks>
/// <para><b>What removal does, in one save:</b> the event becomes Removed (called off, and off the public site), every
/// live pass is withdrawn, a spent credit goes back to whoever paid for it <b>whatever the timing</b>, the calendar row
/// follows, and a <see cref="HostedEventRemoval"/> records who, when, what it was, and the private note. Letters go
/// after the save, from the controller: a removal that failed to save must not have told forty guests their weekend is
/// off.</para>
///
/// <para><b>Why the credit always comes back.</b> Removal is the site's decision, not the organizer's cancellation, so
/// the forty-eight hour rule does not apply. And the event becomes unpaid again: if an appeal is upheld and it is
/// published again, it costs a credit again, which is the same rule an un-cancelled event follows.</para>
///
/// <para><b>An upheld appeal brings it back as a draft</b>, never straight onto the site. Its guests were told it is not
/// going ahead; putting it back up in silence would leave them holding a letter that is no longer true.</para>
/// </remarks>
public static class HostedEventRemovals
{
    public const int MaxNote = 2000;
    public const int MaxAppeal = 4000;

    /// <summary>What removing this event would do, before anybody presses anything.</summary>
    public sealed record Effect(bool AlreadyRemoved, bool CreditReturns, int PartiesWithPlaces, int PeopleWithPlaces);

    public static async Task<Effect?> PreviewAsync(BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var state = await db.HostedEvents.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => (HostedEventLifecycleState?)e.LifecycleState).FirstOrDefaultAsync(ct);
        if (state is null) return null;

        var parties = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == eventId && Live.Contains(b.Status))
            .Select(b => b.PartySize)
            .ToListAsync(ct);

        return new Effect(
            state == HostedEventLifecycleState.Removed,
            await EventCredits.SpentOnAsync(db, eventId, ct) is not null,
            parties.Count,
            parties.Sum());
    }

    /// <summary>The bookings that hold or are waiting for a place, and so hear about it.</summary>
    private static readonly HostedEventBookingStatus[] Live =
        [HostedEventBookingStatus.Requested, HostedEventBookingStatus.Confirmed, HostedEventBookingStatus.Held];

    /// <summary>Removes the event. The caller saves, then writes the letters. Null when it is already removed.</summary>
    public static async Task<HostedEventRemoval?> RemoveAsync(
        BenDataContext db, HostedEvent hosted, Guid actorId, string? note, HostedEventCalendarSync sync, DateTime now,
        CancellationToken ct)
    {
        if (hosted.LifecycleState is HostedEventLifecycleState.Removed) return null;

        var removal = new HostedEventRemoval
        {
            Id = Guid.NewGuid(),
            HostedEventId = hosted.Id,
            PreviousState = hosted.LifecycleState,
            Note = note?.Trim() is { Length: > 0 } n ? n : null,
            RemovedByAppUserId = actorId,
            RemovedUtc = now,
            DateCreated = now,
            CreatedByAppUserId = actorId,
        };

        hosted.LifecycleState = HostedEventLifecycleState.Removed;
        hosted.CancelledAtUtc = now;
        // What a guest's page and the phone show as the reason. Generic on purpose; the note stays on the removal.
        hosted.CancelledReason = null;
        hosted.DateUpdated = now;
        hosted.UpdatedByAppUserId = actorId;

        if (await EventCredits.SpentOnAsync(db, hosted.Id, ct) is { } credit)
        {
            EventCredits.Unspend(credit, actorId, now);
            hosted.FirstPublishedUtc = null;
            removal.CreditReturned = true;
        }

        var bookingIds = await db.HostedEventBookings
            .Where(b => b.HostedEventId == hosted.Id)
            .Select(b => b.Id)
            .ToListAsync(ct);
        foreach (var bookingId in bookingIds)
            await EventPasses.RevokeAllAsync(db, bookingId, actorId, "This event is not going ahead.", ct);

        await sync.SyncAsync(db, hosted, actorId, ct);
        db.HostedEventRemovals.Add(removal);
        return removal;
    }

    /// <summary>The removal an organizer may still appeal, or read the answer to: the newest one.</summary>
    public static Task<HostedEventRemoval?> LatestAsync(BenDataContext db, Guid eventId, CancellationToken ct)
        => db.HostedEventRemovals
            .Where(r => r.HostedEventId == eventId)
            .OrderByDescending(r => r.RemovedUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>Records the organizer's appeal, or says in words why it can't be made. The caller saves.</summary>
    public static string? Appeal(HostedEvent hosted, HostedEventRemoval? removal, Guid actorId, string? message, DateTime now)
    {
        if (removal is null || hosted.LifecycleState is not HostedEventLifecycleState.Removed)
            return "This event hasn't been removed, so there is nothing to appeal.";
        if (removal.AppealState is not HostedEventAppealState.NotAppealed)
            return removal.AppealState is HostedEventAppealState.Waiting
                ? "You've already appealed. You'll get an answer by email and in your messages."
                : "This removal has already been appealed and answered.";
        if (message?.Trim() is not { Length: > 0 } said)
            return "Say why the event should come back — the person reviewing it reads this.";
        if (said.Length > MaxAppeal)
            return $"Keep the appeal under {MaxAppeal:N0} characters.";

        removal.AppealState = HostedEventAppealState.Waiting;
        removal.AppealMessage = said;
        removal.AppealedByAppUserId = actorId;
        removal.AppealedUtc = now;
        removal.DateUpdated = now;
        removal.UpdatedByAppUserId = actorId;
        return null;
    }

    /// <summary>Answers a waiting appeal. Upheld brings the event back as a draft. The caller saves.</summary>
    public static async Task<string?> DecideAsync(
        BenDataContext db, HostedEventRemoval removal, bool uphold, string? note, Guid actorId,
        HostedEventCalendarSync sync, DateTime now, CancellationToken ct)
    {
        if (removal.AppealState is not HostedEventAppealState.Waiting)
            return "This appeal has already been answered.";
        var said = note?.Trim() is { Length: > 0 } n ? n : null;
        if (!uphold && said is null)
            return "Say why the appeal is declined. The organizer reads it.";
        if (said is { Length: > MaxNote })
            return $"Keep the answer under {MaxNote:N0} characters.";

        removal.AppealState = uphold ? HostedEventAppealState.Upheld : HostedEventAppealState.Declined;
        removal.DecisionNote = said;
        removal.DecidedByAppUserId = actorId;
        removal.DecidedUtc = now;
        removal.DateUpdated = now;
        removal.UpdatedByAppUserId = actorId;

        if (!uphold) return null;

        var hosted = await db.HostedEvents.Include(e => e.Nights).FirstAsync(e => e.Id == removal.HostedEventId, ct);
        if (hosted.LifecycleState is HostedEventLifecycleState.Removed)
        {
            hosted.LifecycleState = HostedEventLifecycleState.Draft;
            hosted.CancelledAtUtc = null;
            hosted.CancelledReason = null;
            hosted.ArchivedAtUtc = null;
            hosted.DateUpdated = now;
            hosted.UpdatedByAppUserId = actorId;
            await sync.SyncAsync(db, hosted, actorId, ct);
        }
        return null;
    }

    /// <summary>
    /// Who hears about a removal on the organizer's side: whoever created the event, the group's creator, and its
    /// billing contacts — the people who paid for the credit and the person who wrote the page.
    /// </summary>
    public static async Task<IReadOnlyList<(Guid Id, string? Email, string? Name)>> OrganizerRecipientsAsync(
        BenDataContext db, HostedEvent hosted, CancellationToken ct)
    {
        var groupCreator = await db.Organizations.AsNoTracking().Where(o => o.Id == hosted.OrganizationId)
            .Select(o => o.CreatedByAppUserId).FirstOrDefaultAsync(ct);
        var contacts = await db.OrganizationBillingContacts.AsNoTracking()
            .Where(c => c.OrganizationId == hosted.OrganizationId).Select(c => c.AppUserId).ToListAsync(ct);

        var ids = contacts.Append(groupCreator).Append(hosted.CreatedByAppUserId).Where(id => id != Guid.Empty).Distinct().ToList();
        var people = await db.AppUsers.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.DisplayName }).ToListAsync(ct);
        return [.. people.Select(p => (p.Id, p.Email, p.DisplayName))];
    }
}
