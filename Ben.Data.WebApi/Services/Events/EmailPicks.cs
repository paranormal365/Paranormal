using System.Security.Cryptography;
using System.Text;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The rules for places picked by somebody not signed in (item 235 slice 11d).
/// </summary>
/// <remarks>
/// <para><b>Why fifteen minutes.</b> Long enough to open a mail app, find the letter and press the
/// button; short enough that a made-up address keeps a seat from a real guest for less time than it
/// takes to walk away and come back. The event's own hold deadline starts at the click.</para>
///
/// <para><b>What stops somebody swamping an event</b>, in the order it bites: a rate limit per
/// address (<see cref="RateLimiting.HostedEmailPickPolicy"/>); one live pick per email per event,
/// enforced by the database; a few live picks per email across the site; and a ceiling on how much of
/// one night unproven picks may hold between them, so a script with a thousand addresses still leaves
/// most of the house for people who can click a link.</para>
/// </remarks>
public static class EmailPicks
{
    /// <summary>How long a pick's places are pending before the link must have been clicked.</summary>
    public static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(15);

    /// <summary>How many events one address may have pending picks at, at once.</summary>
    public const int MaxLivePerAddress = 3;

    /// <summary>
    /// The most places, on any one night, that unproven picks may hold between them: a quarter of the
    /// plan, and never fewer than this.
    /// </summary>
    public const int UnprovenFloor = 8;

    /// <summary>How long a pick nobody confirmed is kept, name and phone with it.</summary>
    public static readonly TimeSpan ForgottenAfter = TimeSpan.FromDays(1);

    /// <summary>How long a confirmed pick's link goes on showing the booking it became.</summary>
    public static readonly TimeSpan LinkShowsBookingFor = TimeSpan.FromDays(30);

    /// <summary>256 bits, URL-safe. Guessing one must not be a way in.</summary>
    public static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>What is stored instead of the token, so a copy of the database holds no working links.</summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Stops a pick counting: its places go back at once.</summary>
    public static void Retire(HostedEventEmailPick pick, DateTime now)
    {
        pick.IsLive = false;
        pick.DateUpdated = now;
        foreach (var place in pick.Places) place.IsLive = false;
    }

    /// <summary>
    /// Retires every pick whose fifteen minutes are up, optionally on one event only.
    /// </summary>
    /// <remarks>
    /// <b>Run before anybody picks</b>, not only by the job: the arbiter index knows a flag, not the
    /// time, so a pick that lapsed a minute ago would otherwise refuse the next person until the
    /// five-minute pass came round.
    /// </remarks>
    public static async Task RetireLapsedAsync(
        BenDataContext db, DateTime now, Guid? hostedEventId, CancellationToken ct)
    {
        await db.HostedEventEmailPickPlaces
            .Where(p => p.IsLive && p.HostedEventEmailPick.ExpiresUtc <= now
                     && (hostedEventId == null || p.HostedEventEmailPick.HostedEventId == hostedEventId))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.IsLive, false), ct);

        await db.HostedEventEmailPicks
            .Where(p => p.IsLive && p.ExpiresUtc <= now
                     && (hostedEventId == null || p.HostedEventId == hostedEventId))
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.IsLive, false)
                .SetProperty(p => p.DateUpdated, now), ct);
    }

    /// <summary>
    /// Deletes what is no longer needed: a day after a pick nobody confirmed, a month after one that
    /// became a booking.
    /// </summary>
    public static Task<int> ForgetAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var unconfirmedBefore = now - ForgottenAfter;
        var confirmedBefore = now - LinkShowsBookingFor;

        return db.HostedEventEmailPicks
            .Where(p => !p.IsLive
                     && (p.ConfirmedUtc == null ? p.DateCreated < unconfirmedBefore : p.ConfirmedUtc < confirmedBefore))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>The live picks' squares on one event, for the plan and for every check.</summary>
    public static async Task<List<(Guid NightId, Guid UnitId, Guid PickId, int People)>> LiveSquaresAsync(
        BenDataContext db, Guid hostedEventId, CancellationToken ct)
        => (await db.HostedEventEmailPickPlaces.AsNoTracking()
                .Where(p => p.IsLive && p.HostedEventLayoutUnitId != null
                         && p.HostedEventEmailPick.HostedEventId == hostedEventId)
                .Select(p => new
                {
                    p.HostedEventNightId,
                    UnitId = p.HostedEventLayoutUnitId!.Value,
                    p.HostedEventEmailPickId,
                    People = p.People ?? p.HostedEventEmailPick.PartySize,
                })
                .ToListAsync(ct))
            .Select(p => (p.HostedEventNightId, p.UnitId, p.HostedEventEmailPickId, p.People))
            .ToList();

    /// <summary>
    /// Which of these squares somebody is confirming by email, or null when none.
    /// </summary>
    /// <param name="exceptPickId">The pick being turned into a booking, whose own squares are fine.</param>
    public static async Task<IReadOnlyList<Guid>?> PendingAmongAsync(
        BenDataContext db, Guid hostedEventId, IReadOnlyList<HostedEventBookingNightChoice> chosen,
        Guid? exceptPickId, CancellationToken ct)
    {
        var live = await LiveSquaresAsync(db, hostedEventId, ct);
        if (live.Count == 0) return null;

        var pending = live
            .Where(l => l.PickId != exceptPickId)
            .Select(l => (l.NightId, l.UnitId))
            .ToHashSet();

        var hit = chosen
            .Where(c => c.HostedEventLayoutUnitId is { } unit && pending.Contains((c.HostedEventNightId, unit)))
            .Select(c => c.HostedEventLayoutUnitId!.Value)
            .Distinct()
            .ToList();

        return hit.Count == 0 ? null : hit;
    }

    /// <summary>
    /// Why unproven picks may not take this many more places on one of these nights, or null.
    /// </summary>
    public static async Task<string?> WhyTooMuchIsPendingAsync(
        BenDataContext db, Guid hostedEventId, IReadOnlyList<HostedEventBookingNightChoice> chosen,
        CancellationToken ct)
    {
        var units = await db.HostedEventLayoutUnits.CountAsync(u => u.HostedEventId == hostedEventId, ct);
        var ceiling = CeilingFor(units);

        var live = await LiveSquaresAsync(db, hostedEventId, ct);

        foreach (var night in chosen.Where(c => c.HostedEventLayoutUnitId is not null).GroupBy(c => c.HostedEventNightId))
        {
            var already = live.Count(l => l.NightId == night.Key);
            if (already + night.Select(c => c.HostedEventLayoutUnitId).Distinct().Count() > ceiling)
                return "A lot of places at this event are waiting for people to confirm by email just now. "
                     + "Sign in to hold yours straight away, or try again in a few minutes.";
        }

        return null;
    }

    /// <summary>A quarter of the plan, never fewer than <see cref="UnprovenFloor"/>.</summary>
    public static int CeilingFor(int unitsOnThePlan) => Math.Max(UnprovenFloor, unitsOnThePlan / 4);
}
