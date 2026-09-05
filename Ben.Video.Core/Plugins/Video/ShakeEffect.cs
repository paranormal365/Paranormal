using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Horizontal shake effect — rapid oscillating crop offset simulating a camera shake.
/// Inspired by animate.css <c>shakeX</c>.
/// </summary>
public sealed class ShakeEffect : IClipEffect
{
    public string EffectId    => "shake";
    public string DisplayName => "Shake";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration",   Label = "Duration (s)", Min = 0.1, Max = 5.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 0.6 },
        new() { Key = "intensity",  Label = "Intensity (px)", Min = 2.0, Max = 80.0, Step = 2.0, LargeStep = 10.0, DefaultValue = 20.0 },
        new() { Key = "frequency",  Label = "Frequency (cycles)", Min = 1.0, Max = 20.0, Step = 1.0, LargeStep = 5.0, DefaultValue = 8.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 0.6, ["intensity"] = 20.0, ["frequency"] = 8.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic   = System.Globalization.CultureInfo.InvariantCulture;
        var d    = Math.Min(p.GetValueOrDefault("duration", 0.6), clipDuration);
        var amt  = p.GetValueOrDefault("intensity", 20.0);
        var freq = p.GetValueOrDefault("frequency", 8.0);
        if (d <= 0 || amt <= 0) return string.Empty;

        var dStr    = d.ToString("F3", ic);
        var amtStr  = amt.ToString("F1", ic);
        var freqStr = freq.ToString("F1", ic);

        // x oscillates; envelope decays to 0 at t=d. Crop with extra padding so border isn't visible.
        // shake_x = amt * sin(freq * 2*PI * t/d) * (1-t/d)  (when t<d)
        var shakeX = $"if(lt(t,{dStr}),{amtStr}*sin({freqStr}*2*3.14159*t/{dStr})*(1-t/{dStr}),0)";
        var pad    = (int)Math.Ceiling(amt) + 4;
        return $"pad=iw+{pad*2}:ih:x={pad}:y=0:color=black,crop=iw:ih:'({pad})+({shakeX})':0";
    }
}
