using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Image;

/// <summary>
/// Slow zoom in and out oscillation simulating a gentle pulse effect.
/// Inspired by animate.css <c>pulse</c>.
/// </summary>
public sealed class PulseEffect : IClipEffect
{
    public string EffectId    => "img_pulse";
    public string DisplayName => "Pulse";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "max_zoom", Label = "Max Zoom",  Min = 1.02, Max = 1.5, Step = 0.01, LargeStep = 0.1, DefaultValue = 1.1 },
        new() { Key = "cycles",   Label = "Cycles",    Min = 0.5,  Max = 10.0, Step = 0.5, LargeStep = 2.0, DefaultValue = 2.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["max_zoom"] = 1.1, ["cycles"] = 2.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic      = System.Globalization.CultureInfo.InvariantCulture;
        var maxZ    = p.GetValueOrDefault("max_zoom", 1.1);
        var cycles  = p.GetValueOrDefault("cycles", 2.0);
        var d       = clipDuration;
        if (d <= 0 || maxZ <= 1.0) return string.Empty;

        var mzStr  = maxZ.ToString("F3", ic);
        var dStr   = d.ToString("F3", ic);
        var cycStr = cycles.ToString("F2", ic);

        // zoom oscillates 1 ↔ maxZ using abs(sin), against zoompan's own output clock
        var zExpr = $"1+({mzStr}-1)*abs(sin({cycStr}*3.14159*{ZoompanFragment.TimeVariable}/{dStr}))";

        return ZoompanFragment.Build(
            zExpr, ZoompanFragment.CentredX, ZoompanFragment.CentredY,
            canvasWidth, canvasHeight);
    }
}
