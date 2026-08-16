using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Slides the content in from the bottom of the frame.
/// Inspired by animate.css <c>slideInUp</c>.
/// </summary>
public sealed class SlideInFromBottomEffect : IClipEffect
{
    public string EffectId    => "slide_in_from_bottom";
    public string DisplayName => "Slide In From Bottom";

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
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 0.5), clipDuration);
        var eas = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseOut));
        if (d <= 0) return string.Empty;

        // Canvas = [image on top | black below]. Crop y starts at ih (black) and moves to 0 (image).
        var ease = EasingHelper.GetClamped(eas, "t", d);
        var dStr = d.ToString("F3", ic);
        return $"pad=iw:ih*2:0:0:black,crop=iw:ih:0:'if(lt(t,{dStr}),ih*(1-{ease}),0)'";
    }
}
