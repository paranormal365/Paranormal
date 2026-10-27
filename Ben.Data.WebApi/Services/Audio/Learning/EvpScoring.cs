using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Audio.Learning;

/// <summary>
/// Which weights score EVP scans, and fitting new ones from Keep and Dismiss (audio phase 13, 10/27/2026).
/// </summary>
/// <remarks>
/// The built-in weights (version 1) run unless a SuperAdmin has switched on a fit that passed its gate.
/// Fitting only ever records; it never switches anything on.
/// </remarks>
public sealed class EvpScoring(IDbContextFactory<BenDataContext> dbContextFactory, ILogger<EvpScoring> logger)
{
    /// <summary>The weights new scans use.</summary>
    public async Task<EvpScoreWeights> ActiveAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var active = await db.EvpScoringVersions.AsNoTracking().FirstOrDefaultAsync(v => v.IsActive, ct);
        return active is null ? EvpScoreWeights.BuiltIn : ToWeights(active);
    }

    /// <summary>The weights a scan was scored with, for explaining its score; the built-in ones when unknown.</summary>
    public static async Task<EvpScoreWeights> ForVersionAsync(BenDataContext db, int version, CancellationToken ct)
    {
        if (version <= 1) return EvpScoreWeights.BuiltIn;
        var row = await db.EvpScoringVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Version == version, ct);
        return row is null ? EvpScoreWeights.BuiltIn : ToWeights(row);
    }

    public static EvpScoreWeights ToWeights(EvpScoringVersion row)
        => new(row.Version, row.WeightProminence, row.WeightBand, row.WeightDuration);

    /// <summary>
    /// Every candidate somebody kept or dismissed, by its latest ruling, with what was measured. The
    /// measurements do not depend on the weights, so decisions made under any version are all usable.
    /// </summary>
    public static async Task<List<EvpDecision>> DecisionsAsync(BenDataContext db, CancellationToken ct)
    {
        var rulings = await db.EvpRulings.AsNoTracking()
            .Where(r => r.Ruling == EvpReviewStatus.Confirmed || r.Ruling == EvpReviewStatus.Dismissed)
            .Select(r => new { r.AudioMarkerId, r.Ruling, r.PlayedFirst, r.DateCreated })
            .ToListAsync(ct);
        var latest = rulings.GroupBy(r => r.AudioMarkerId).Select(g => g.MaxBy(r => r.DateCreated)!)
            .ToDictionary(r => r.AudioMarkerId);
        var ids = latest.Keys.ToList();

        var candidates = await db.EvpScanCandidates.AsNoTracking()
            .Where(c => c.AudioMarkerId != null && ids.Contains(c.AudioMarkerId.Value))
            .Select(c => new
            {
                MarkerId = c.AudioMarkerId!.Value, c.EvpScan.UploadFileId, c.EvpScan.DateCreated,
                c.PeakProminenceDb, c.MeanBandGapDb, c.EventSeconds, c.MeanFloorDb, c.PeakBandDb,
                c.BandLevelSpreadDb, c.ZeroCrossingRate,
            })
            .ToListAsync(ct);

        // A marker proposed by more than one scan counts once, by its newest.
        return candidates.GroupBy(c => c.MarkerId).Select(g => g.MaxBy(c => c.DateCreated)!)
            .Select(c =>
            {
                var r = latest[c.MarkerId];
                return new EvpDecision(c.UploadFileId,
                    new EvpFeatures(c.PeakProminenceDb, c.MeanBandGapDb, c.EventSeconds, c.MeanFloorDb,
                        c.PeakBandDb, c.BandLevelSpreadDb, c.ZeroCrossingRate),
                    r.Ruling == EvpReviewStatus.Confirmed,
                    r.PlayedFirst == true ? 2 : 1);
            })
            .ToList();
    }

    /// <summary>
    /// Fits weights from every decision and records the fit, with whether it passed its gate. Null when
    /// there is nothing of both kinds to learn from, or nothing new since the last fit (unless asked again).
    /// </summary>
    public async Task<EvpScoringVersion?> FitAndRecordAsync(bool evenIfNothingNew, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var decisions = await DecisionsAsync(db, ct);
        var last = await db.EvpScoringVersions.AsNoTracking().OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct);
        if (!evenIfNothingNew && last is not null && last.Decisions == decisions.Count) return null;

        var version = Math.Max(2, (last?.Version ?? 1) + 1);
        if (EvpScoreRefit.Fit(decisions, version) is not { } fit) return null;

        var row = new EvpScoringVersion
        {
            Id = Guid.NewGuid(), Version = version,
            WeightProminence = fit.Weights.Prominence, WeightBand = fit.Weights.Band, WeightDuration = fit.Weights.Duration,
            Decisions = fit.Decisions, Recordings = fit.Recordings, HeldOutDecisions = fit.HeldOutDecisions,
            TrainAuc = Finite(fit.TrainAuc), HeldOutAuc = Finite(fit.HeldOutAuc), BaselineHeldOutAuc = Finite(fit.BaselineHeldOutAuc),
            PassedGate = fit.PassedGate, GateNote = fit.GateNote, DateCreated = DateTime.UtcNow,
        };
        db.EvpScoringVersions.Add(row);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("EVP score refit {Version}: {Prominence}/{Band}/{Duration}, passed {Passed}: {Note}",
            version, row.WeightProminence, row.WeightBand, row.WeightDuration, row.PassedGate, row.GateNote);
        return row;
    }

    /// <summary>NaN (nothing to test on) is stored as 0, with the gate note saying why.</summary>
    private static double Finite(double x) => double.IsFinite(x) ? x : 0;
}
