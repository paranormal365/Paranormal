using System.Globalization;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Making a field recording easier to listen to.
/// </summary>
/// <remarks>
/// <para>The editor had no audio effects at all. A recording made in a house at two in the morning
/// is mostly room tone, fridge hum and the recorder's own noise floor, and the two things anybody
/// wants to do to one are lift the voice out of the hiss and stop the level jumping between clips
/// (2026-09-05 audit, audio-25).</para>
///
/// <para>Both are expressed as things a person wants rather than as filter parameters. The
/// reduction is measured in decibels over a range nobody should have to learn, and pushing it too
/// far turns speech into a warble — so the dial covers the part of the range that helps.</para>
/// </remarks>
public static class AudioCleanupFilter
{
    /// <summary>The gentlest reduction worth applying, in decibels.</summary>
    public const double MinimumReductionDb = 6.0;

    /// <summary>The heaviest this offers.</summary>
    /// <remarks>
    /// afftdn allows up to 97 dB. Past about 30 the artefacts are worse than the noise on speech,
    /// so the dial stops where the result is still worth having.
    /// </remarks>
    public const double MaximumReductionDb = 30.0;

    /// <summary>
    /// The noise-reduction clause for a dial setting, or null when it is off.
    /// </summary>
    /// <param name="amount">0 leaves the recording alone; 1 is the heaviest offered.</param>
    public static string? NoiseReduction(double amount)
    {
        if (amount <= 0) return null;

        var db = MinimumReductionDb
               + Math.Clamp(amount, 0.0, 1.0) * (MaximumReductionDb - MinimumReductionDb);

        return "afftdn=nr=" + db.ToString("F1", CultureInfo.InvariantCulture) + ":nf=-40";
    }

    /// <summary>
    /// The loudness-levelling clause, or null when it is off.
    /// </summary>
    /// <remarks>
    /// The single-pass form. A two-pass measure-then-apply is more accurate and needs the whole
    /// file analysed before anything can be encoded, which is not a trade worth making inside a
    /// browser for a clip somebody is going to listen to rather than broadcast.
    /// </remarks>
    public static string? Levelling(bool enabled) =>
        enabled ? "loudnorm=I=-16:TP=-1.5:LRA=11" : null;

    /// <summary>
    /// The whole cleanup chain for one clip, or null when nothing is asked for.
    /// </summary>
    /// <remarks>
    /// Noise first. Levelling measures how loud the material is, and measuring it before the hiss
    /// comes out means levelling to the hiss.
    /// </remarks>
    public static string? Build(double noiseReduction, bool normalise)
    {
        var parts = new List<string>();

        if (NoiseReduction(noiseReduction) is { } denoise) parts.Add(denoise);
        if (Levelling(normalise) is { } level)             parts.Add(level);

        return parts.Count == 0 ? null : string.Join(",", parts);
    }
}
