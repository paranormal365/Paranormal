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
/// <param name="BandLowHz">
/// Bottom of the range the detector listens in. Below it is rumble and handling noise. 300 Hz is
/// the telephone band's floor; a deep voice reaches lower.
/// </param>
/// <param name="BandHighHz">
/// Top of the range the detector listens in. Above it is mostly hiss. Clamped to just under half
/// the sample rate by the detector.
/// </param>
/// <param name="FloorWindowSeconds">
/// How much of the recording either side of a moment counts as "the room" when deciding what its
/// background level is. Shorter follows a changing room more closely; longer is steadier.
/// </param>
/// <param name="FloorPercentile">
/// Which quiet part of that window is taken as the background, as a fraction: 0.20 means the level
/// the quietest fifth of it stays under. Higher makes the background louder, so fewer sounds stand
/// out over it.
/// </param>
/// <param name="ReleaseHysteresisDb">
/// How far below the onset level a sound must fall before it counts as ended, so a wavering sound
/// stays one candidate instead of breaking into many. No larger than the threshold.
/// </param>
/// <param name="MaxCandidates">
/// Most candidates one scan proposes. Never more than the server's own ceiling.
/// </param>
/// <param name="MinScore">
/// Candidates scoring below this are not proposed. 0 keeps everything the detector found.
/// </param>
/// <param name="ExplorationShare">
/// When a scan finds more than <see cref="MaxCandidates"/>, this share of the places is given to
/// lower-ranked candidates spread through the recording rather than to the top scores, so the
/// reviewer's rulings teach the detector about more than its favorites (item 253). Each one is
/// marked as such. Never random: the same scan picks the same ones.
/// </param>
/// <param name="ReviewedOverlapFraction">
/// How much a new candidate must overlap something already kept or dismissed to count as the same
/// sound and be left out. Lower leaves out more.
/// </param>
/// <remarks>
/// Not adjustable, on purpose: the 25 ms frame and 10 ms hop. The hop is the unit every stored
/// measurement is taken in, so letting it vary per scan would make measurements from different
/// scans impossible to compare. Every default here is the value the detector used before these
/// were adjustable, so a scan that changes nothing scores exactly as before.
/// </remarks>
public sealed record EvpDetectionOptions(
    double ThresholdDb             = 6.0,
    double MinDurationSeconds      = 0.15,
    double MergeGapSeconds         = 0.35,
    double ContextPadSeconds       = 0.40,
    double MaxEventSeconds         = 5.0,
    double BandLowHz               = 300.0,
    double BandHighHz              = 3400.0,
    double FloorWindowSeconds      = 10.0,
    double FloorPercentile         = 0.20,
    double ReleaseHysteresisDb     = 2.0,
    int    MaxCandidates           = 500,
    double MinScore                = 0.0,
    double ExplorationShare        = 0.10,
    double ReviewedOverlapFraction = 0.5)
{
    /// <summary>Bounds that keep a scan meaningful, applied server-side so a hand-built request can't slip past them.</summary>
    public static readonly (double Min, double Max) ThresholdRange = (2.0, 20.0);
    public static readonly (double Min, double Max) MinDurationRange = (0.05, 5.0);
    public static readonly (double Min, double Max) MergeGapRange = (0.0, 2.0);
    public static readonly (double Min, double Max) ContextPadRange = (0.0, 3.0);
    public static readonly (double Min, double Max) MaxEventRange = (1.0, 30.0);
    public static readonly (double Min, double Max) BandRange = (50.0, 8000.0);
    public static readonly (double Min, double Max) FloorWindowRange = (2.0, 60.0);
    public static readonly (double Min, double Max) FloorPercentileRange = (0.05, 0.50);
    public static readonly (double Min, double Max) HysteresisRange = (0.0, 10.0);
    public static readonly (double Min, double Max) MaxCandidatesRange = (10, 500);
    public static readonly (double Min, double Max) MinScoreRange = (0.0, 100.0);
    public static readonly (double Min, double Max) ExplorationRange = (0.0, 0.5);
    public static readonly (double Min, double Max) ReviewedOverlapRange = (0.1, 1.0);

    /// <summary>The preset starting points, which the caller is free to adjust from.</summary>
    public static EvpDetectionOptions FromSensitivity(EvpSensitivity sensitivity) => sensitivity switch
    {
        EvpSensitivity.High => new EvpDetectionOptions(ThresholdDb: 4.0),
        EvpSensitivity.Low  => new EvpDetectionOptions(ThresholdDb: 9.0),
        _                   => new EvpDetectionOptions(),
    };

    /// <summary>
    /// "Low", "Medium" or "High" when <paramref name="options"/> is exactly that preset, or null
    /// when anything has been changed from it (the page says "Custom").
    /// </summary>
    public static string? PresetName(EvpDetectionOptions options) =>
        new[] { EvpSensitivity.Low, EvpSensitivity.Medium, EvpSensitivity.High }
            .Where(s => FromSensitivity(s) == options)
            .Select(s => s.ToString())
            .FirstOrDefault();

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
        if (OutOfRange(BandLowHz, BandRange) || OutOfRange(BandHighHz, BandRange))
            return $"The listening range must be between {BandRange.Min:0} and {BandRange.Max:0} Hz.";
        if (BandHighHz <= BandLowHz)
            return "The top of the listening range must be above its bottom.";
        if (OutOfRange(FloorWindowSeconds, FloorWindowRange))
            return $"The background window must be between {FloorWindowRange.Min} and {FloorWindowRange.Max} seconds.";
        if (OutOfRange(FloorPercentile, FloorPercentileRange))
            return $"The background level must be between {FloorPercentileRange.Min * 100:0}% and {FloorPercentileRange.Max * 100:0}%.";
        if (OutOfRange(ReleaseHysteresisDb, HysteresisRange))
            return $"The release margin must be between {HysteresisRange.Min} and {HysteresisRange.Max} dB.";
        // Equal is allowed: a sound then ends when it falls back to the background, which is the
        // lowest threshold's only workable release (2 dB with the 2 dB default).
        if (ReleaseHysteresisDb > ThresholdDb)
            return "The release margin can't be larger than the threshold.";
        if (OutOfRange(MaxCandidates, MaxCandidatesRange))
            return $"Most candidates must be between {MaxCandidatesRange.Min:0} and {MaxCandidatesRange.Max:0}.";
        if (OutOfRange(MinScore, MinScoreRange))
            return $"Lowest score must be between {MinScoreRange.Min:0} and {MinScoreRange.Max:0}.";
        if (OutOfRange(ExplorationShare, ExplorationRange))
            return $"The share for lower-ranked candidates must be between {ExplorationRange.Min * 100:0}% and {ExplorationRange.Max * 100:0}%.";
        if (OutOfRange(ReviewedOverlapFraction, ReviewedOverlapRange))
            return $"The overlap with reviewed sounds must be between {ReviewedOverlapRange.Min * 100:0}% and {ReviewedOverlapRange.Max * 100:0}%.";
        return null;
    }

    private static bool OutOfRange(double value, (double Min, double Max) range) =>
        double.IsNaN(value) || value < range.Min || value > range.Max;
}
