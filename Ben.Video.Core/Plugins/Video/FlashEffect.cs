using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Rapid brightness oscillation that produces a strobe/camera-flash effect.
/// Inspired by animate.css <c>flash</c>.
/// Uses the <c>geq</c> filter to modulate luma per-frame.
/// </summary>
public sealed class FlashEffect : IClipEffect
{
    public string EffectId    => "flash";
    public string DisplayName => "Flash";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration", Label = "Duration (s)", Min = 0.1, Max = 5.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 0.8 },
        new() { Key = "flashes",  Label = "Flashes",      Min = 1.0, Max = 10.0, Step = 1.0, LargeStep = 2.0, DefaultValue = 3.0 },
        new() { Key = "strength", Label = "Strength",     Min = 0.1, Max = 2.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 1.5 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 0.8, ["flashes"] = 3.0, ["strength"] = 1.5 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 0.8), clipDuration);
        var n   = Math.Max(p.GetValueOrDefault("flashes", 3.0), 1.0);
        var str = p.GetValueOrDefault("strength", 1.5);
        if (d <= 0) return string.Empty;

        var dStr   = d.ToString("F3", ic);
        var nStr   = n.ToString("F1", ic);
        var strStr = str.ToString("F3", ic);

        // Brightness factor: oscillates between 1 and strength during [0,d], then 1 after.
        // factor = 1 + (strength-1) * max(0, sin(n * PI * t/d)) when t<d
        var factor = $"if(lt(t,{dStr}),1+({strStr}-1)*max(0,sin({nStr}*3.14159*t/{dStr})),1)";
        return $"geq=r='min(r(X,Y)*{factor},255)':g='min(g(X,Y)*{factor},255)':b='min(b(X,Y)*{factor},255)'";
    }
}
