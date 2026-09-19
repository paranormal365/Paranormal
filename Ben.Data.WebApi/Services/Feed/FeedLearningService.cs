using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Ben.Data.WebApi.Services.Feed;

/// <summary>
/// The learning loop's working end (item 186 F6): extracts a post's media features, scores the
/// category match, and records the labelled examples everything later learns from.
/// </summary>
/// <remarks>
/// <para>Scoped, like the controllers that call it: it works inside the caller's DbContext so a
/// post and its features commit together.</para>
///
/// <para><b>Nothing here throws into the posting path.</b> A post whose features cannot be
/// extracted is a post without features — scored null, nudged never — because "we could not
/// measure your video" must not become "your post failed". Same doctrine as screening, opposite
/// default: screening fails closed because publishing is irreversible; scoring fails OPEN
/// because a missing signal is not evidence of anything.</para>
/// </remarks>
public sealed class FeedLearningService
{
    private readonly IFileStorageService _storage;
    private readonly ILogger<FeedLearningService> _logger;

    public FeedLearningService(IFileStorageService storage, ILogger<FeedLearningService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Builds (or returns) the feature row for a media-bearing post. Reads the ingest pipeline's
    /// own metadata row for the facts it already extracted — duration, audio, dimensions, camera,
    /// capture time — and adds what only decoding shows: luminance, for images.
    /// </summary>
    public async Task<FeedMediaFeatureSet?> ExtractFeaturesAsync(
        BenDataContext db, OrgMessage post, CancellationToken ct)
    {
        if (post.MediaUploadFileId is not { } fileId) return null;

        var existing = await db.FeedMediaFeatureSets
            .FirstOrDefaultAsync(f => f.OrgMessageId == post.Id, ct);
        if (existing is not null) return existing;

        var file = await db.UploadFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Select(f => new { f.StoragePath, f.ContentType })
            .FirstOrDefaultAsync(ct);
        if (file is null) return null;

        var metadata = await db.UploadFileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UploadFileId == fileId, ct);

        var isVideo = file.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
        var features = new FeedMediaFeatureSet
        {
            OrgMessageId = post.Id,
            IsVideo = isVideo,
            DurationSeconds = metadata?.DurationSeconds,
            // The metadata extractor writes an audio codec exactly when an audio stream exists.
            // For an image "has audio" is a category error, not a false — leave it unknown.
            HasAudio = isVideo ? metadata?.AudioCodec is not null : null,
            WidthPixels = metadata?.WidthPixels,
            HeightPixels = metadata?.HeightPixels,
            CapturedHourLocal = metadata?.CapturedAtUtc?.Hour,
            CameraManufacturer = metadata?.CameraManufacturer,
            DateCreated = DateTime.UtcNow,
        };

        // Luminance needs the pixels. Images only for now — video luma wants the screener's
        // sampled frames, which is a sharing refactor recorded for a later slice, not silently
        // approximated here.
        if (!isVideo && file.StoragePath is { Length: > 0 } storagePath && _storage.Exists(storagePath))
        {
            try
            {
                await using var stream = await _storage.OpenReadAsync(storagePath, ct);
                using var bitmap = SKBitmap.Decode(stream);
                if (bitmap is not null)
                {
                    var (mean, stdDev) = ComputeLuma(bitmap);
                    features.MeanLuma = mean;
                    features.LumaStdDev = stdDev;
                    features.WidthPixels ??= bitmap.Width;
                    features.HeightPixels ??= bitmap.Height;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Luma extraction failed for post {PostId}; scoring without it.", post.Id);
            }
        }

        db.FeedMediaFeatureSets.Add(features);
        return features;
    }

    /// <summary>
    /// Scores the post's category match against the active weights for its type, writing
    /// <see cref="OrgMessage.CategoryMatchScore"/>. Null when there is nothing to say.
    /// </summary>
    public async Task ScoreAsync(BenDataContext db, OrgMessage post, CancellationToken ct)
    {
        if (post.FeedExperienceTypeId is not { } typeId || post.MediaUploadFileId is null)
        {
            post.CategoryMatchScore = null;
            return;
        }

        var features = await ExtractFeaturesAsync(db, post, ct);
        if (features is null)
        {
            post.CategoryMatchScore = null;
            return;
        }

        var weights = await ActiveWeightsForAsync(db, typeId, ct);
        post.CategoryMatchScore = CategoryMatchScoring.Score(FeedFeatures.Encode(features), weights);
    }

    /// <summary>Latest fitted weights for the type, else the priors for its parent category.</summary>
    public async Task<Dictionary<string, double>> ActiveWeightsForAsync(
        BenDataContext db, Guid experienceTypeId, CancellationToken ct)
    {
        var fitted = await db.FeedTypeWeightSets.AsNoTracking()
            .Where(w => w.ExperienceTypeId == experienceTypeId)
            .OrderByDescending(w => w.FitVersion)
            .Select(w => w.WeightsJson)
            .FirstOrDefaultAsync(ct);

        if (fitted is not null && FeedFeatures.FromJson(fitted) is { } weights)
            return weights;

        var categoryName = await db.ExperienceTypes.AsNoTracking()
            .Where(t => t.Id == experienceTypeId)
            .Select(t => t.ExperienceCategory.Name)
            .FirstOrDefaultAsync(ct);
        return CategoryMatchScoring.PriorWeightsFor(categoryName);
    }

    /// <summary>
    /// Records one judgment. APPEND-ONLY by construction: this is the only writer, and it only
    /// ever adds. The example snapshots the features so it stays interpretable after the post
    /// is gone.
    /// </summary>
    public async Task AddExampleAsync(
        BenDataContext db, Guid orgMessageId, Guid experienceTypeId,
        FeedLabel label, FeedLabelSource source, Guid decidedByAppUserId, CancellationToken ct)
    {
        var features = await db.FeedMediaFeatureSets.AsNoTracking()
            .FirstOrDefaultAsync(f => f.OrgMessageId == orgMessageId, ct);

        db.FeedLabelledExamples.Add(new FeedLabelledExample
        {
            Id = Guid.NewGuid(),
            OrgMessageId = orgMessageId,
            ExperienceTypeId = experienceTypeId,
            Label = label,
            Source = source,
            FeaturesJson = features is null ? null : FeedFeatures.ToJson(FeedFeatures.Encode(features)),
            DecidedByAppUserId = decidedByAppUserId,
            DecidedUtc = DateTime.UtcNow,
        });
    }

    /// <summary>Mean and standard deviation of luminance, 0–1, over a downsampled decode.</summary>
    public static (double Mean, double StdDev) ComputeLuma(SKBitmap source)
    {
        // 64×64 is plenty: luma statistics, not detail.
        using var resized = source.Resize(
            new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear));
        var bitmap = resized ?? source;

        double sum = 0, sumSquares = 0;
        var count = bitmap.Width * bitmap.Height;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var luma = ((0.299 * pixel.Red) + (0.587 * pixel.Green) + (0.114 * pixel.Blue)) / 255.0;
                sum += luma;
                sumSquares += luma * luma;
            }
        }
        var mean = sum / count;
        var variance = Math.Max(0, (sumSquares / count) - (mean * mean));
        return (mean, Math.Sqrt(variance));
    }
}
