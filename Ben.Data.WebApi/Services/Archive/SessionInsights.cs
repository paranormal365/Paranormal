using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Archive;

/// <summary>
/// What everybody else's visits say about this one.
/// </summary>
/// <param name="PlaceName">Where this session was recorded.</param>
/// <param name="OthersWhoRecordedHere">
/// Distinct people, not sessions, and never counting the asker. Twelve visits by one person is an
/// enthusiast; twelve people is a body of evidence, and only the second makes a comparison mean
/// anything.
/// </param>
/// <param name="OthersWhoFlaggedSomething">
/// How many of those people marked at least one moment. This is the archive's headline sentence —
/// "eleven of twelve people flagged something on these stairs" — and the reason the marker count
/// is the figure worth comparing rather than the readings.
/// </param>
/// <param name="YourSessionsHere">How many times the asker has recorded at this place.</param>
/// <param name="YourMarkersPerHour">This session's flagged moments per hour.</param>
/// <param name="PlaceMedianMarkersPerHour">
/// The median across everybody else's published sessions here. Median rather than mean: one
/// six-hour vigil with two hundred marks would drag an average somewhere nobody's night lives.
/// </param>
/// <param name="StandsOut">
/// Whether this session's rate is materially above what this place usually gives people — the one
/// sentence somebody actually wants: "was that unusual, or is this building always like that?"
/// </param>
/// <param name="Detailed">
/// False when the comparison is withheld. The counts above are on the place's public page anyway
/// and stay visible; what a plan buys is the comparison against them.
/// </param>
public sealed record SessionInsights(
    string PlaceName,
    int OthersWhoRecordedHere,
    int OthersWhoFlaggedSomething,
    int YourSessionsHere,
    double? YourMarkersPerHour,
    double? PlaceMedianMarkersPerHour,
    bool? StandsOut,
    bool Detailed);

/// <summary>
/// Turns one person's night into a measurement, using everybody else's.
/// </summary>
/// <remarks>
/// <para><b>The individual's reason to subscribe.</b> A group pays for people, cases and privacy.
/// Somebody investigating alone has none of those to buy, and their own recordings are already
/// theirs and already private — so a plan sold on privacy sells them something they have. What
/// they cannot get on their own, at any price, is context: whether the spike in that cellar was
/// remarkable or whether the building does that to everybody.</para>
///
/// <para><b>The line this draws, and why it is defensible.</b> Your own session is yours, free,
/// always — every figure the app already shows you stays visible. What a plan buys is the
/// ARCHIVE's answer about it, which is other people's contributed work aggregated. That gets more
/// valuable every time somebody records, which makes free contributors an asset rather than
/// freeloaders, and it means the product nobody can copy is the one being sold.</para>
///
/// <para><b>What is never withheld:</b> the counts. How many people have recorded at a place and
/// how many flagged something are on that place's public page for anybody, signed in or not.
/// Hiding them here would be theatre, and the kind that teaches people the paywall is arbitrary.
/// The withheld part is the comparison — and the free response says exactly what it would say.</para>
/// </remarks>
public static class SessionInsightsService
{
    /// <summary>How far above the median counts as standing out.</summary>
    /// <remarks>
    /// Half again, not a hair over. A rate that merely edges the median is noise — declaring it
    /// remarkable would be the astrology version of this feature, and the whole value of the
    /// archive is that it can say "no, that was ordinary here" and be believed.
    /// </remarks>
    private const double StandsOutFactor = 1.5;

    /// <summary>The comparison for one session, or null when there is nothing to compare.</summary>
    public static async Task<SessionInsights?> ForSessionAsync(
        BenDataContext db, Guid sessionId, Guid askerId, bool detailed, CancellationToken ct)
    {
        var session = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.Id == sessionId && s.SubmittedByAppUserId == askerId)
            .Select(s => new
            {
                s.Id, s.PlaceId, s.StartedAt, s.EndedAt, s.MarkerCount,
                PlaceName = s.Place != null ? s.Place.Name : null,
                PlaceKind = s.Place != null ? s.Place.Kind : (PlaceKind?)null,
            })
            .FirstOrDefaultAsync(ct);

        // Somebody else's session, or one attached to no place, has nothing to say.
        if (session?.PlaceId is not { } placeId) return null;

        // Only public locations have an archive to compare against. A private residence's
        // sessions are not a body of evidence anybody may consult.
        if (session.PlaceKind != PlaceKind.PublicLocation) return null;

        // Everybody else's PUBLISHED sessions here. Unpublished ones are private work and must
        // not leak into an aggregate somebody can read a number out of.
        var others = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.PlaceId == placeId
                     && s.PublishedAtUtc != null
                     && s.SubmittedByAppUserId != askerId)
            .Select(s => new { s.SubmittedByAppUserId, s.StartedAt, s.EndedAt, s.MarkerCount })
            .ToListAsync(ct);

        var contributors = others.Select(o => o.SubmittedByAppUserId).Distinct().Count();
        var flagged = others.Where(o => o.MarkerCount > 0)
            .Select(o => o.SubmittedByAppUserId).Distinct().Count();

        var yoursHere = await db.FieldSessionUploads.AsNoTracking()
            .CountAsync(s => s.PlaceId == placeId && s.SubmittedByAppUserId == askerId, ct);

        var name = session.PlaceName ?? "this place";

        if (!detailed)
        {
            // The counts are public anyway; the comparison is what a plan buys.
            return new SessionInsights(name, contributors, flagged, yoursHere,
                YourMarkersPerHour: null, PlaceMedianMarkersPerHour: null,
                StandsOut: null, Detailed: false);
        }

        var yourRate = MarkersPerHour(session.StartedAt, session.EndedAt, session.MarkerCount);
        var otherRates = others
            .Select(o => MarkersPerHour(o.StartedAt, o.EndedAt, o.MarkerCount))
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .ToList();

        var median = Median(otherRates);

        return new SessionInsights(name, contributors, flagged, yoursHere,
            yourRate, median,
            // Null rather than false when there is nothing to compare against: "you did not stand
            // out" and "nobody else has recorded here" are different answers, and conflating them
            // would tell a first visitor their night was unremarkable against no evidence at all.
            StandsOut: yourRate is { } mine && median is { } m && m > 0 ? mine >= m * StandsOutFactor : null,
            Detailed: true);
    }

    /// <summary>
    /// Flagged moments per hour, or null when the session has no usable length.
    /// </summary>
    /// <remarks>
    /// A rate rather than a count, because the count rewards sitting there longer. Sessions
    /// shorter than a minute are discarded rather than divided by: a thirty-second recording with
    /// one mark is 120 an hour, which would make the noisiest thing in the archive an accident.
    /// </remarks>
    private static double? MarkersPerHour(DateTime startedAt, DateTime? endedAt, int markers)
    {
        if (endedAt is not { } ended) return null;

        var hours = (ended - startedAt).TotalHours;
        return hours >= 1.0 / 60.0 ? markers / hours : null;
    }

    /// <summary>The middle value, averaging the two middles on an even count.</summary>
    private static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
