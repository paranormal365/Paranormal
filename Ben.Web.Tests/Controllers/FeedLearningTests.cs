using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Feed;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The learning loop (item 186 F6): the feature encoding, the priors, the fit, the ranking
/// nudge, and the re-fit job's self-gating.
/// </summary>
/// <remarks>
/// The claims under protection: a mismatch is a SIGNAL — it nudges the author and gently lowers
/// ranking, and can never block or hide a post; the labelled-example store is append-only and
/// the asset; and the priors are humble — categories a camera cannot witness never nudge at all.
/// </remarks>
public sealed class FeedLearningTests
{
    // ── Feature encoding ────────────────────────────────────────────────────

    [Fact]
    public void Unknown_audio_is_not_the_same_lesson_as_no_audio()
    {
        var image = FeedFeatures.Encode(new FeedMediaFeatureSet { IsVideo = false, HasAudio = null });
        var silentVideo = FeedFeatures.Encode(new FeedMediaFeatureSet { IsVideo = true, HasAudio = false });

        Assert.Equal(0, image[FeedFeatures.HasAudio]);
        Assert.Equal(0, image[FeedFeatures.AudioKnown]);      // unknown: no claim either way
        Assert.Equal(0, silentVideo[FeedFeatures.HasAudio]);
        Assert.Equal(1, silentVideo[FeedFeatures.AudioKnown]); // measured absent: a real fact
    }

    [Theory]
    [InlineData(21, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    [InlineData(20, 0)]
    [InlineData(null, 0)]
    public void Night_hours_are_inclusive_at_both_ends(int? hour, double expected)
    {
        var encoded = FeedFeatures.Encode(new FeedMediaFeatureSet { CapturedHourLocal = hour });
        Assert.Equal(expected, encoded[FeedFeatures.CapturedAtNight]);
    }

    [Fact]
    public void Duration_saturates_at_the_cap()
    {
        var encoded = FeedFeatures.Encode(new FeedMediaFeatureSet { DurationSeconds = 4000 });
        Assert.Equal(1, encoded[FeedFeatures.DurationNorm]);
    }

    [Fact]
    public void Feature_json_round_trips()
    {
        var encoded = FeedFeatures.Encode(new FeedMediaFeatureSet { IsVideo = true, MeanLuma = 0.25 });
        var back = FeedFeatures.FromJson(FeedFeatures.ToJson(encoded));
        Assert.Equal(encoded, back);
    }

    // ── The priors: humble by construction ──────────────────────────────────

    [Fact]
    public void Audible_without_audio_nudges_and_with_audio_does_not()
    {
        var weights = CategoryMatchScoring.PriorWeightsFor("Audible");

        var silent = FeedFeatures.Encode(new FeedMediaFeatureSet { IsVideo = true, HasAudio = false });
        Assert.True(CategoryMatchScoring.Score(silent, weights) < CategoryMatchScoring.NudgeThreshold);

        var voiced = FeedFeatures.Encode(new FeedMediaFeatureSet { IsVideo = true, HasAudio = true });
        Assert.True(CategoryMatchScoring.Score(voiced, weights) > 0.5);
    }

    [Theory]
    [InlineData("Physical")]
    [InlineData("Olfactory")]
    [InlineData("Psychological")]
    [InlineData(null)]
    public void Categories_a_camera_cannot_witness_never_nudge_on_priors(string? category)
    {
        // Whatever the media looks like, the honest prior for these is "cannot say" — which must
        // sit above the nudge threshold, or every cold-spot photo gets an insinuating banner.
        var weights = CategoryMatchScoring.PriorWeightsFor(category);
        var worstCase = FeedFeatures.Encode(new FeedMediaFeatureSet
        {
            IsVideo = false, HasAudio = null, MeanLuma = 1.0, CapturedHourLocal = 12,
        });
        Assert.True(CategoryMatchScoring.Score(worstCase, weights) >= CategoryMatchScoring.NudgeThreshold);
    }

    [Fact]
    public void A_daylight_apparition_photo_does_not_nudge()
    {
        // Favoring darkness must not mean punishing daylight: an honest daytime photo stays
        // above the threshold on the Visual prior.
        var weights = CategoryMatchScoring.PriorWeightsFor("Visual");
        var daylight = FeedFeatures.Encode(new FeedMediaFeatureSet
        {
            IsVideo = false, MeanLuma = 0.8, CapturedHourLocal = 14,
        });
        Assert.True(CategoryMatchScoring.Score(daylight, weights) >= CategoryMatchScoring.NudgeThreshold);
    }

    // ── The fit ─────────────────────────────────────────────────────────────

    private static List<LogisticFit.Example> SeparableExamples(int perClass)
    {
        var examples = new List<LogisticFit.Example>();
        for (var i = 0; i < perClass; i++)
        {
            examples.Add(new LogisticFit.Example(
                FeedFeatures.Encode(new FeedMediaFeatureSet
                    { IsVideo = true, HasAudio = true, DurationSeconds = 30 + i }), true));
            examples.Add(new LogisticFit.Example(
                FeedFeatures.Encode(new FeedMediaFeatureSet
                    { IsVideo = false, HasAudio = null, MeanLuma = 0.5 + (i % 3) * 0.1 }), false));
        }
        return examples;
    }

    [Fact]
    public void Fit_converges_on_separable_data_and_learns_the_right_sign()
    {
        var fit = LogisticFit.Fit(SeparableExamples(15));
        Assert.True(fit.Weights[FeedFeatures.HasAudio] > 0);
        Assert.Equal(1.0, fit.HoldoutAccuracy!.Value, precision: 5);
    }

    [Fact]
    public void Fit_is_deterministic()
    {
        var first = LogisticFit.Fit(SeparableExamples(12));
        var second = LogisticFit.Fit(SeparableExamples(12));
        Assert.Equal(first.Weights, second.Weights);
    }

    // ── Ranking: a signal, not a verdict ────────────────────────────────────

    [Fact]
    public void Match_score_scales_ranking_between_floor_and_one()
    {
        var now = DateTime.UtcNow;
        var unscored = new RankableFeedPost(Guid.NewGuid(), now.AddHours(-1), 2, 1);
        var matched = unscored with { MatchScore = 1.0 };
        var mismatched = unscored with { MatchScore = 0.0 };

        Assert.Equal(FeedRanking.Score(unscored, now), FeedRanking.Score(matched, now), precision: 10);
        Assert.Equal(FeedRanking.Score(unscored, now) * FeedRanking.MatchFloor,
                     FeedRanking.Score(mismatched, now), precision: 10);
        // The floor keeps a certain mismatch visible: it sinks, it does not vanish.
        Assert.True(FeedRanking.Score(mismatched, now) > 0);
    }

    // ── Luma ────────────────────────────────────────────────────────────────

    [Fact]
    public void Luma_of_solid_frames_is_exact_and_split_frames_spread()
    {
        using var black = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(black)) canvas.Clear(SKColors.Black);
        var (blackMean, blackSpread) = FeedLearningService.ComputeLuma(black);
        Assert.Equal(0, blackMean, precision: 2);
        Assert.Equal(0, blackSpread, precision: 2);

        using var white = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(white)) canvas.Clear(SKColors.White);
        var (whiteMean, _) = FeedLearningService.ComputeLuma(white);
        Assert.Equal(1, whiteMean, precision: 2);

        using var split = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(split))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawRect(new SKRect(0, 0, 32, 16), new SKPaint { Color = SKColors.White });
        }
        var (splitMean, splitSpread) = FeedLearningService.ComputeLuma(split);
        Assert.Equal(0.5, splitMean, precision: 1);
        Assert.True(splitSpread > 0.4);
    }

    // ── The re-fit job ──────────────────────────────────────────────────────

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid TypeId)> SeedExamplesAsync(
        int confirmed, int mismatched)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();

        Guid userId = Guid.NewGuid(), categoryId = Guid.NewGuid(), typeId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser { Id = userId, UserName = "m", Email = "m@t.dev", DisplayName = "M", Handle = "m" });
        db.ExperienceCategories.Add(new ExperienceCategory
        {
            Id = categoryId, Name = "Audible", SortOrder = 1, IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.ExperienceTypes.Add(new ExperienceType
        {
            Id = typeId, ExperienceCategoryId = categoryId, Name = "Voices / Whispering",
            SortOrder = 1, IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        for (var i = 0; i < confirmed; i++)
            db.FeedLabelledExamples.Add(NewExample(typeId, userId, positive: true, daysAgo: 2));
        for (var i = 0; i < mismatched; i++)
            db.FeedLabelledExamples.Add(NewExample(typeId, userId, positive: false, daysAgo: 2));
        await db.SaveChangesAsync();
        return (factory, typeId);
    }

    private static FeedLabelledExample NewExample(Guid typeId, Guid userId, bool positive, int daysAgo)
        => new()
        {
            Id = Guid.NewGuid(),
            ExperienceTypeId = typeId,
            Label = positive ? FeedLabel.Confirmed : FeedLabel.Mismatch,
            Source = FeedLabelSource.Moderator,
            FeaturesJson = FeedFeatures.ToJson(FeedFeatures.Encode(new FeedMediaFeatureSet
            {
                IsVideo = true, HasAudio = positive, DurationSeconds = 30,
            })),
            DecidedByAppUserId = userId,
            DecidedUtc = DateTime.UtcNow.AddDays(-daysAgo),
        };

    private static WeightRefitJob Job(IDbContextFactory<BenDataContext> factory)
        => new(factory, NullLogger<WeightRefitJob>.Instance);

    [Fact]
    public async Task Refit_writes_a_versioned_set_that_learned_the_signal()
    {
        var (factory, typeId) = await SeedExamplesAsync(confirmed: 12, mismatched: 12);
        await Job(factory).RunAsync(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var set = await db.FeedTypeWeightSets.SingleAsync();
        Assert.Equal(typeId, set.ExperienceTypeId);
        Assert.Equal(1, set.FitVersion);
        Assert.Equal(24, set.ExampleCount);
        var weights = FeedFeatures.FromJson(set.WeightsJson)!;
        Assert.True(weights[FeedFeatures.HasAudio] > 0);
    }

    [Fact]
    public async Task Refit_self_gates_on_a_second_pass_with_nothing_new()
    {
        var (factory, _) = await SeedExamplesAsync(confirmed: 12, mismatched: 12);
        await Job(factory).RunAsync(CancellationToken.None);
        await Job(factory).RunAsync(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.FeedTypeWeightSets.CountAsync());
    }

    [Theory]
    [InlineData(19, 0)]   // under total minimum
    [InlineData(16, 4)]   // under per-label minimum
    public async Task Refit_declines_to_learn_from_too_little(int confirmed, int mismatched)
    {
        var (factory, _) = await SeedExamplesAsync(confirmed, mismatched);
        await Job(factory).RunAsync(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.FeedTypeWeightSets.CountAsync());
    }
}
