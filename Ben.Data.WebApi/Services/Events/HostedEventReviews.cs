using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Who may say what an event was like, and what the stars add up to (item 235 phase 12).
/// </summary>
public static class HostedEventReviews
{
    /// <summary>How long after the last night a review may still be left or changed.</summary>
    /// <remarks>
    /// Long enough for the thank-you to be read on a slow week, short enough that a review is about
    /// the evening rather than a memory of it. Archiving the event does not close it: the page drops
    /// off the public lists after a fortnight, and the guest's own link still works.
    /// </remarks>
    public static readonly TimeSpan OpenFor = TimeSpan.FromDays(60);

    /// <summary>
    /// Why this person may not review this event, or null when they may.
    /// </summary>
    /// <remarks>
    /// One method, asked by the page and by the endpoint, so the form a guest is shown and the answer
    /// they get on pressing it cannot disagree.
    /// </remarks>
    public static async Task<string?> WhyNotAsync(
        BenDataContext db, HostedEvent ev, Guid? userId, DateTime now, CancellationToken ct)
    {
        if (!ev.AllowReviews) return "This event isn't taking reviews.";

        if (ev.LifecycleState is not (HostedEventLifecycleState.Ended or HostedEventLifecycleState.Archived))
            return "Reviews open once the event is over.";

        if (now > ev.EndsOn.Date.AddDays(1) + OpenFor)
            return "Reviews for this event have closed.";

        if (userId is not { } who || who == Guid.Empty) return "Sign in to leave a review.";

        return await CameAsync(db, ev.Id, who, ct)
            ? null
            : "Reviews come from people who had a place at the event.";
    }

    /// <summary>
    /// Whether this person had a confirmed place: the lead, or a guest named with their account.
    /// </summary>
    public static Task<bool> CameAsync(BenDataContext db, Guid eventId, Guid userId, CancellationToken ct)
        => db.HostedEventBookings.AnyAsync(
               b => b.HostedEventId == eventId
                 && b.Status == HostedEventBookingStatus.Confirmed
                 && (b.LeadAppUserId == userId || b.Guests.Any(g => g.AppUserId == userId)), ct);

    /// <summary>An event's average stars, to one decimal, and how many gave them. Hidden ones do not count.</summary>
    public static async Task<(decimal? Average, int Count)> RatingAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
        => Average(await db.HostedEventReviews.AsNoTracking()
            .Where(r => r.HostedEventId == eventId && r.HiddenAtUtc == null)
            .Select(r => r.Stars)
            .ToListAsync(ct));

    /// <summary>
    /// What guests made of this group's earlier events, for the page of its next one.
    /// </summary>
    public static async Task<(decimal? Average, int Count)> PastRatingAsync(
        BenDataContext db, Guid organizationId, Guid exceptEventId, CancellationToken ct)
        => Average(await db.HostedEventReviews.AsNoTracking()
            .Where(r => r.HostedEvent.OrganizationId == organizationId
                     && r.HostedEventId != exceptEventId
                     && r.HiddenAtUtc == null)
            .Select(r => r.Stars)
            .ToListAsync(ct));

    private static (decimal?, int) Average(List<int> stars)
        => stars.Count == 0
            ? (null, 0)
            : (Math.Round((decimal)stars.Average(), 1, MidpointRounding.AwayFromZero), stars.Count);
}
