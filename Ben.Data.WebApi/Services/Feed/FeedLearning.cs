using System.Text.Json;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Feed;

/// <summary>
/// The canonical feature vector the category-match scorer works from (item 186 F6).
/// </summary>
/// <remarks>
/// <para>One place defines what a feature is called and how a <see cref="FeedMediaFeatureSet"/>
/// row becomes numbers, because three things must agree on it forever: the scorer, the re-fit,
/// and the <c>FeaturesJson</c> snapshots frozen inside labelled examples. A renamed feature
/// would silently zero its weight everywhere; keeping the names as constants makes that a
/// compile-time find-all-references instead.</para>
///
/// <para>Every encoded value lands in 0..1 so the hand priors and the fitted weights live on
/// comparable scales. Null measurements encode as 0 <i>with a paired presence flag</i> where
/// the difference matters — "no audio" and "audio unknown" must not teach the same lesson.</para>
/// </remarks>
public static class FeedFeatures
{
    public const string IsVideo = "isVideo";
    public const string HasAudio = "hasAudio";
    public const string AudioKnown = "audioKnown";
    public const string DurationNorm = "durationNorm";
    public const string MeanLuma = "meanLuma";
    public const string Darkness = "darkness";
    public const string LumaSpread = "lumaSpread";
    public const string CapturedAtNight = "capturedAtNight";

    /// <summary>Duration saturates here: for "is this an EVP clip", 10 minutes is as long as long gets.</summary>
    public const double DurationCapSeconds = 600;

    public static IReadOnlyList<string> Names { get; } =
        [IsVideo, HasAudio, AudioKnown, DurationNorm, MeanLuma, Darkness, LumaSpread, CapturedAtNight];

    public static Dictionary<string, double> Encode(FeedMediaFeatureSet features)
    {
        var meanLuma = features.MeanLuma ?? 0.5; // unknown brightness is mid, not black
        return new Dictionary<string, double>
        {
            [IsVideo] = features.IsVideo ? 1 : 0,
            [HasAudio] = features.HasAudio == true ? 1 : 0,
            [AudioKnown] = features.HasAudio is null ? 0 : 1,
            [DurationNorm] = Math.Min(1, (features.DurationSeconds ?? 0) / DurationCapSeconds),
            [MeanLuma] = Math.Clamp(meanLuma, 0, 1),
            [Darkness] = Math.Clamp(1 - meanLuma, 0, 1),
            [LumaSpread] = Math.Clamp(features.LumaStdDev ?? 0, 0, 1),
            [CapturedAtNight] = features.CapturedHourLocal is >= 21 or <= 5 ? 1 : 0,
        };
    }

    public static string ToJson(Dictionary<string, double> encoded) => JsonSerializer.Serialize(encoded);

    public static Dictionary<string, double>? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Scores how well measured features fit a chosen experience type: sigmoid(w·x + b).
/// </summary>
/// <remarks>
/// <para><b>A signal, not a verdict.</b> The score nudges the author and gently lowers ranking
/// (<see cref="FeedRanking"/>); it never blocks a post and is never shown to other readers. An
/// honest mistake is not misconduct, and a system that calls people liars for mislabelling
/// drives off exactly the enthusiasts the feed needs.</para>
///
/// <para>Weights come from the latest <see cref="FeedTypeWeightSet"/> for the type; before any
/// fit exists, from the hand-written priors below, keyed by the type's parent CATEGORY —
/// "Audible things want audio" is knowable without a single example, and that is all the priors
/// claim. Categories whose evidence a camera cannot capture (Physical, Olfactory,
/// Psychological…) get a neutral prior that sits above the nudge threshold by construction:
/// the honest statement that media neither confirms nor denies a cold spot.</para>
/// </remarks>
public static class CategoryMatchScoring
{
    /// <summary>Reserved weights key for the intercept.</summary>
    public const string BiasKey = "_bias";

    /// <summary>Below this, the author sees the recategorize nudge. Deliberately low: the nudge
    /// should fire on "tagged Voices, has no audio track", not on "your apparition photo is a
    /// bit bright".</summary>
    public const double NudgeThreshold = 0.30;

    public static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

    public static double Score(Dictionary<string, double> features, Dictionary<string, double> weights)
    {
        var z = weights.GetValueOrDefault(BiasKey);
        foreach (var (name, value) in features)
            z += weights.GetValueOrDefault(name) * value;
        return Sigmoid(z);
    }

    /// <summary>The version-0 priors, by parent category name. Unknown categories score neutral.</summary>
    public static Dictionary<string, double> PriorWeightsFor(string? categoryName) =>
        categoryName switch
        {
            // Audio is nearly the whole question. No audio track ⇒ sigmoid(-1.6) ≈ 0.17 (nudges);
            // audio present ⇒ sigmoid(+1.4) ≈ 0.80.
            "Audible" => new()
            {
                [BiasKey] = -1.6,
                [FeedFeatures.HasAudio] = 3.0,
                [FeedFeatures.AudioKnown] = 0.0,
            },
            // Visual evidence favors video a little and darkness a little — most sightings are
            // filmed at night — but a daylight photo is still plausible: floor ≈ sigmoid(0.4).
            "Visual" => new()
            {
                [BiasKey] = 0.4,
                [FeedFeatures.IsVideo] = 0.5,
                [FeedFeatures.Darkness] = 0.6,
                [FeedFeatures.CapturedAtNight] = 0.3,
            },
            // A camera can neither confirm nor refute a temperature drop, a smell, or a feeling.
            // Neutral ≈ 0.62, safely above the nudge threshold — these never nudge on priors.
            _ => new() { [BiasKey] = 0.5 },
        };
}

/// <summary>
/// Plain logistic regression by gradient descent — the re-fit's engine (item 186 F6).
/// </summary>
/// <remarks>
/// Hand-rolled over ~8 features rather than taking an ML dependency: deterministic (fixed
/// iteration count, no randomness beyond the holdout split, which is hash-based), auditable,
/// and unit-testable to convergence on synthetic data. L2 keeps thirty noisy examples from
/// producing confident nonsense.
/// </remarks>
public static class LogisticFit
{
    public const int Epochs = 500;
    public const double LearningRate = 0.5;
    public const double L2 = 0.01;

    /// <summary>Fraction of examples held out of the fit to measure honesty.</summary>
    public const double HoldoutFraction = 0.2;

    public readonly record struct Example(Dictionary<string, double> Features, bool Positive);

    public readonly record struct FitResult(
        Dictionary<string, double> Weights, int TrainedOn, int HeldOut, double? HoldoutAccuracy);

    public static FitResult Fit(IReadOnlyList<Example> examples)
    {
        // Hash-based split: stable across runs, no RNG to seed or argue about.
        var holdout = new List<Example>();
        var train = new List<Example>();
        for (var i = 0; i < examples.Count; i++)
            (i % (int)Math.Round(1 / HoldoutFraction) == 0 ? holdout : train).Add(examples[i]);
        if (train.Count == 0) (train, holdout) = (holdout, train);

        var weights = new Dictionary<string, double> { [CategoryMatchScoring.BiasKey] = 0 };
        foreach (var name in FeedFeatures.Names) weights[name] = 0;

        for (var epoch = 0; epoch < Epochs; epoch++)
        {
            var gradient = weights.Keys.ToDictionary(k => k, _ => 0.0);
            foreach (var example in train)
            {
                var error = CategoryMatchScoring.Score(example.Features, weights)
                            - (example.Positive ? 1 : 0);
                gradient[CategoryMatchScoring.BiasKey] += error;
                foreach (var (name, value) in example.Features)
                    if (gradient.ContainsKey(name)) gradient[name] += error * value;
            }
            foreach (var key in weights.Keys.ToList())
            {
                var l2 = key == CategoryMatchScoring.BiasKey ? 0 : L2 * weights[key];
                weights[key] -= LearningRate * ((gradient[key] / train.Count) + l2);
            }
        }

        double? accuracy = holdout.Count == 0
            ? null
            : holdout.Count(e => CategoryMatchScoring.Score(e.Features, weights) >= 0.5 == e.Positive)
              / (double)holdout.Count;

        return new FitResult(weights, train.Count, holdout.Count, accuracy);
    }
}
