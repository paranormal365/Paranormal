using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Slowly zooms out from a close crop to the full frame over the clip duration.
/// Inspired by animate.css <c>zoomOut</c>.
/// </summary>
public sealed class ZoomOutEffect : IClipEffect
{
    public string EffectId    => "zoom_out";
    public string DisplayName => "Zoom Out";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration",  Label = "Duration (s)", Min = 0.1, Max = 10.0, Step = 0.1, LargeStep = 1.0, DefaultValue = 2.0 },
        new() { Key = "end_zoom",  Label = "End Zoom", Min = 1.1, Max = 3.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.5 },
        new() { Key = "easing",    Label = "Easing", Type = ParameterType.Select,
                Options = EasingHelper.Labels, DefaultValue = EasingHelper.EaseIn },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 2.0, ["end_zoom"] = 1.5, ["easing"] = EasingHelper.EaseIn },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 2.0), clipDuration);
        var ez  = p.GetValueOrDefault("end_zoom", 1.5);
        var eas = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseIn));
        if (d <= 0) return string.Empty;

        var fps   = 25;
        var frames = (int)Math.Ceiling(d * fps);
        var ease  = EasingHelper.GetClamped(eas, "on/fps", d);
        var ezStr = ez.ToString("F3", ic);
        return $"zoompan=z='1+({ezStr}-1)*{ease}':x='(iw-iw/zoom)/2':y='(ih-ih/zoom)/2':d={frames}:s=iw+\"x\"+ih";
    }
}
