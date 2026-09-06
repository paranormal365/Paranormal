using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Image;

/// <summary>Slide In From Left for image clips.</summary>
public sealed class SlideInFromLeftEffect : IClipEffect
{
    public string EffectId    => "img_slide_in_left";
    public string DisplayName => "Slide In From Left";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration", Label = "Duration (s)", Min = 0.1, Max = 5.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 0.5 },
        new() { Key = "easing",   Label = "Easing", Type = ParameterType.Select,
                Options = EasingHelper.Labels, DefaultValue = EasingHelper.EaseOut },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 0.5, ["easing"] = EasingHelper.EaseOut },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 0.5), clipDuration);
        var eas = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseOut));
        if (d <= 0) return string.Empty;

        var ease = EasingHelper.GetClamped(eas, "t", d);
        var dStr = d.ToString("F3", ic);
        return $"pad=iw*2:ih:0:0:black,crop=iw:ih:'if(lt(t,{dStr}),iw*(1-{ease}),0)':0";
    }
}
