using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Applies a warm sepia colour tone.
/// Uses <c>colorchannelmixer</c> with classic sepia coefficients.
/// </summary>
public sealed class SepiaEffect : IClipEffect
{
    public string EffectId    => "sepia";
    public string DisplayName => "Sepia";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "intensity", Label = "Intensity", Min = 0.0, Max = 1.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["intensity"] = 1.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var k  = Math.Clamp(p.GetValueOrDefault("intensity", 1.0), 0.0, 1.0);
        if (k <= 0) return string.Empty;

        // Classic sepia matrix blended with identity by k
        var id = 1.0 - k;
        var rr = (id + k * 0.393).ToString("F4", ic);
        var rg = (     k * 0.769).ToString("F4", ic);
        var rb = (     k * 0.189).ToString("F4", ic);
        var gr = (     k * 0.349).ToString("F4", ic);
        var gg = (id + k * 0.686).ToString("F4", ic);
        var gb = (     k * 0.168).ToString("F4", ic);
        var br = (     k * 0.272).ToString("F4", ic);
        var bg = (     k * 0.534).ToString("F4", ic);
        var bb = (id + k * 0.131).ToString("F4", ic);
        return $"colorchannelmixer={rr}:{rg}:{rb}:0:{gr}:{gg}:{gb}:0:{br}:{bg}:{bb}:0";
    }
}
