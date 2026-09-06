using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Rotates the clip from a starting angle to 0° over the animation duration.
/// Inspired by animate.css <c>rotateIn</c>.
/// Uses the ffmpeg <c>rotate</c> filter.
/// </summary>
public sealed class RotateInEffect : IClipEffect
{
    public string EffectId    => "rotate_in";
    public string DisplayName => "Rotate In";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration",    Label = "Duration (s)",     Min = 0.1, Max = 5.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 0.8 },
        new() { Key = "start_angle", Label = "Start Angle (°)",  Min = -360, Max = 360, Step = 15, LargeStep = 90, DefaultValue = -90 },
        new() { Key = "easing",      Label = "Easing", Type = ParameterType.Select,
                Options = EasingHelper.Labels, DefaultValue = EasingHelper.EaseOut },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 0.8, ["start_angle"] = -90, ["easing"] = EasingHelper.EaseOut },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic   = System.Globalization.CultureInfo.InvariantCulture;
        var d    = Math.Min(p.GetValueOrDefault("duration", 0.8), clipDuration);
        var ang  = p.GetValueOrDefault("start_angle", -90.0);
        var eas  = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseOut));
        if (d <= 0) return string.Empty;

        // Convert degrees to radians
        var angRad = (ang * Math.PI / 180.0).ToString("F4", ic);
        var ease   = EasingHelper.GetClamped(eas, "t", d);
        var dStr   = d.ToString("F3", ic);

        // angle goes from angRad → 0 as ease goes 0 → 1
        var rotExpr = $"if(lt(t,{dStr}),{angRad}*(1-{ease}),0)";
        return $"rotate=angle='{rotExpr}':c=black@0:bilinear=1,scale=iw:ih:flags=lanczos,crop=iw:ih";
    }
}
