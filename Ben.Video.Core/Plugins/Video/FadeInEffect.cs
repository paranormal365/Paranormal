using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>Fade in from black at the start of the clip (<c>fade=t=in</c>).</summary>
public sealed class FadeInEffect : IClipEffect
{
    public string EffectId    => "fade_in";
    public string DisplayName => "Fade In";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration", Label = "Duration (s)", Min = 0.1, Max = 10.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 1.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 1.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var d  = Math.Min(p.GetValueOrDefault("duration", 1.0), clipDuration);
        if (d <= 0) return string.Empty;
        return "fade=t=in:st=0:d=" + d.ToString("F3", ic);
    }
}

