using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Feed;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Re-fits the category-match weights from the labelled examples (item 186 F6) — the part of
/// the loop that makes the thing genuinely learn as the database grows.
/// </summary>
/// <remarks>
/// <para>Runs on every scheduler pass but self-gates hard: a type is re-fit only when it has
/// enough examples of BOTH labels, at least one new example since its last fit, and its last
/// fit is old enough. On a quiet site this job does nothing at all, which is correct.</para>
///
/// <para>Each fit APPENDS a new <see cref="FeedTypeWeightSet"/> version — never edits — with
/// its holdout accuracy recorded. A fit that measures worse than what it replaces is logged
/// loudly but still becomes active: with example counts this small, chasing the holdout number
/// would be pretending to a rigor the data cannot support, and the append-only history means
/// reverting is one row's delete by a person who actually looked.</para>
/// </remarks>
public sealed class WeightRefitJob : IScheduledJob
{
    /// <summary>A fit needs something to learn from — and something of each label, or the
    /// "fit" is a constant.</summary>
    public const int MinimumExamples = 20;
    public const int MinimumPerLabel = 5;

    /// <summary>Nightly cadence, enforced per type from its own fit history.</summary>
    public static readonly TimeSpan MinimumFitAge = TimeSpan.FromHours(23);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<WeightRefitJob> _logger;

    public WeightRefitJob(IDbContextFactory<BenDataContext> dbFactory, ILogger<WeightRefitJob> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public string Name => "feed-weight-refit";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Types with any examples at all, with their latest fit in one round trip.
        var candidates = await db.FeedLabelledExamples.AsNoTracking()
            .GroupBy(e => e.ExperienceTypeId)
            .Select(g => new
            {
                ExperienceTypeId = g.Key,
                Total = g.Count(),
                Confirmed = g.Count(e => e.Label == Ben.Data.Common.Enums.FeedLabel.Confirmed),
                NewestExampleUtc = g.Max(e => e.DecidedUtc),
            })
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var mismatches = candidate.Total - candidate.Confirmed;
            if (candidate.Total < MinimumExamples
                || candidate.Confirmed < MinimumPerLabel
                || mismatches < MinimumPerLabel)
                continue;

            var latest = await db.FeedTypeWeightSets.AsNoTracking()
                .Where(w => w.ExperienceTypeId == candidate.ExperienceTypeId)
                .OrderByDescending(w => w.FitVersion)
                .Select(w => new { w.FitVersion, w.FitUtc, w.HoldoutAccuracy })
                .FirstOrDefaultAsync(ct);

            if (latest is not null
                && (DateTime.UtcNow - latest.FitUtc < MinimumFitAge
                    || candidate.NewestExampleUtc <= latest.FitUtc))
                continue;

            var rows = await db.FeedLabelledExamples.AsNoTracking()
                .Where(e => e.ExperienceTypeId == candidate.ExperienceTypeId && e.FeaturesJson != null)
                .OrderBy(e => e.DecidedUtc)
                .Select(e => new { e.FeaturesJson, e.Label })
                .ToListAsync(ct);

            var examples = rows
                .Select(r => (Features: FeedFeatures.FromJson(r.FeaturesJson), r.Label))
                .Where(r => r.Features is not null)
                .Select(r => new LogisticFit.Example(
                    r.Features!, r.Label == Ben.Data.Common.Enums.FeedLabel.Confirmed))
                .ToList();
            if (examples.Count < MinimumExamples) continue;

            var fit = LogisticFit.Fit(examples);

            db.FeedTypeWeightSets.Add(new FeedTypeWeightSet
            {
                Id = Guid.NewGuid(),
                ExperienceTypeId = candidate.ExperienceTypeId,
                FitVersion = (latest?.FitVersion ?? 0) + 1,
                FitUtc = DateTime.UtcNow,
                ExampleCount = fit.TrainedOn + fit.HeldOut,
                WeightsJson = FeedFeatures.ToJson(fit.Weights),
                HoldoutAccuracy = fit.HoldoutAccuracy,
            });
            await db.SaveChangesAsync(ct);

            if (latest?.HoldoutAccuracy is { } previous && fit.HoldoutAccuracy is { } current && current < previous)
                _logger.LogWarning(
                    "Weight re-fit for type {TypeId} v{Version} measures WORSE on holdout " +
                    "({Current:0.00} vs {Previous:0.00}). It is active; the prior version remains " +
                    "in FeedTypeWeightSets if a person judges the regression real.",
                    candidate.ExperienceTypeId, (latest?.FitVersion ?? 0) + 1, current, previous);
            else
                _logger.LogInformation(
                    "Re-fit type {TypeId} → v{Version}: {Count} examples, holdout {Accuracy:0.00}.",
                    candidate.ExperienceTypeId, (latest?.FitVersion ?? 0) + 1,
                    fit.TrainedOn + fit.HeldOut, fit.HoldoutAccuracy ?? double.NaN);
        }
    }
}
