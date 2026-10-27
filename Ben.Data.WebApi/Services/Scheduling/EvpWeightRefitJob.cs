using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Audio.Learning;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Fits the EVP score's weights from Keep and Dismiss, at most once a day (audio phase 13, 10/27/2026).
/// </summary>
/// <remarks>
/// Does nothing until there are enough decisions from enough recordings to fit at all, and nothing when
/// no decision has been made since the last fit. It only ever records a fit; switching one on is a
/// SuperAdmin's decision on /admin/evp.
/// </remarks>
public sealed class EvpWeightRefitJob(
    IDbContextFactory<BenDataContext> dbFactory, EvpScoring scoring, ILogger<EvpWeightRefitJob> logger) : IScheduledJob
{
    public static readonly TimeSpan MinimumFitAge = TimeSpan.FromHours(23);

    public string Name => "evp-weight-refit";

    public async Task RunAsync(CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var lastFit = await db.EvpScoringVersions.AsNoTracking().MaxAsync(v => (DateTime?)v.DateCreated, ct);
            if (lastFit is { } at && DateTime.UtcNow - at < MinimumFitAge) return;

            var decisions = await EvpScoring.DecisionsAsync(db, ct);
            if (decisions.Count < EvpScoreRefit.MinimumDecisions
                || decisions.Select(d => d.RecordingId).Distinct().Count() < EvpScoreRefit.MinimumRecordings)
                return;
        }

        var row = await scoring.FitAndRecordAsync(evenIfNothingNew: false, ct);
        if (row is { PassedGate: true })
            logger.LogWarning("EVP score refit {Version} passed its gate and is waiting to be switched on at /admin/evp.", row.Version);
    }
}
