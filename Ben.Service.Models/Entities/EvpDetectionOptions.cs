using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// The tunable parameters of an EVP scan.
/// </summary>
/// <remarks>
/// <para>Detection is never automatic — a scan only happens when someone asks for one, and these
/// are the dials they get. Recordings differ enormously (a quiet bedroom versus a basement with a
/// furnace), so a single fixed threshold either misses events or floods the queue depending on the
/// room. The presets are starting points; every value here can be overridden per scan.</para>
/// <para>Defaults match <see cref="EvpSensitivity.Medium"/>.</para>
/// </remarks>
/// <param name="ThresholdDb">
/// How far above the local noise floor a sound must rise to be proposed. The main tolerance
/// control: lower finds more and reviews longer, higher finds only the obvious.
/// </param>
/// <param name="MinDurationSeconds">
/// Shortest sound kept. Below roughly 0.15s you are collecting clicks and taps rather than
/// anything that could be a word.
/// </param>
/// <param name="MergeGapSeconds">
/// Sounds closer together than this become one candidate, so the gaps between syllables don't
/// shatter a phrase into fragments.
/// </param>
/// <param name="ContextPadSeconds">
/// Extra audio kept either side of each candidate so it can be judged in context rather than
/// starting mid-sound.
/// </param>
/// <param name="MaxEventSeconds">
/// Longest single candidate. Continuous talking would otherwise merge into one enormous span, and
/// "listen to these ten seconds" is not a reviewable finding.
/// </param>
public sealed record EvpDetectionOptions(
    double ThresholdDb        = 6.0,
    double MinDurationSeconds = 0.15,
    double MergeGapSeconds    = 0.35,
    double ContextPadSeconds  = 0.40,
    double MaxEventSeconds    = 5.0)
{
    /// <summary>Bounds that keep a scan meaningful, applied server-side so a hand-built request can't slip past them.</summary>
    public static readonly (double Min, double Max) ThresholdRange = (2.0, 20.0);
    public static readonly (double Min, double Max) MinDurationRange = (0.05, 5.0);
    public static readonly (double Min, double Max) MergeGapRange = (0.0, 2.0);
    public static readonly (double Min, double Max) ContextPadRange = (0.0, 3.0);
    public static readonly (double Min, double Max) MaxEventRange = (1.0, 30.0);

    /// <summary>The preset starting points, which the caller is free to adjust from.</summary>
    public static EvpDetectionOptions FromSensitivity(EvpSensitivity sensitivity) => sensitivity switch
    {
        EvpSensitivity.High => new EvpDetectionOptions(ThresholdDb: 4.0),
        EvpSensitivity.Low  => new EvpDetectionOptions(ThresholdDb: 9.0),
        _                   => new EvpDetectionOptions(),
    };

    /// <summary>Null when every value is in range, otherwise the reason it isn't.</summary>
    public string? Validate()
    {
        if (OutOfRange(ThresholdDb, ThresholdRange))
            return $"Threshold must be between {ThresholdRange.Min} and {ThresholdRange.Max} dB.";
        if (OutOfRange(MinDurationSeconds, MinDurationRange))
            return $"Minimum length must be between {MinDurationRange.Min} and {MinDurationRange.Max} seconds.";
        if (OutOfRange(MergeGapSeconds, MergeGapRange))
            return $"Merge gap must be between {MergeGapRange.Min} and {MergeGapRange.Max} seconds.";
        if (OutOfRange(ContextPadSeconds, ContextPadRange))
            return $"Context padding must be between {ContextPadRange.Min} and {ContextPadRange.Max} seconds.";
        if (OutOfRange(MaxEventSeconds, MaxEventRange))
            return $"Longest candidate must be between {MaxEventRange.Min} and {MaxEventRange.Max} seconds.";
        if (MaxEventSeconds <= MinDurationSeconds)
            return "Longest candidate must be greater than the minimum length.";
        return null;
    }

    private static bool OutOfRange(double value, (double Min, double Max) range) =>
        double.IsNaN(value) || value < range.Min || value > range.Max;
}
