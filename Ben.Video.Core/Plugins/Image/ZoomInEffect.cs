using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Image;

/// <summary>
/// Zooms from a wider view into the image centre over the display duration.
/// Inspired by animate.css <c>zoomIn</c>. Optimised for still images via <c>zoompan</c>.
/// </summary>
public sealed class ZoomInEffect : IClipEffect
{
    public string EffectId    => "img_zoom_in";
    public string DisplayName => "Zoom In";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "start_zoom", Label = "Start Zoom", Min = 1.1, Max = 3.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.5 },
        new() { Key = "easing",     Label = "Easing", Type = ParameterType.Select,
                Options = EasingHelper.Labels, DefaultValue = EasingHelper.EaseOut },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["start_zoom"] = 1.5, ["easing"] = EasingHelper.EaseOut },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var sz  = p.GetValueOrDefault("start_zoom", 1.5);
        var eas = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseOut));
        var d   = clipDuration;
        if (d <= 0 || sz <= 1.0) return string.Empty;

        var frames = (int)Math.Ceiling(d * 25);
        var ease   = EasingHelper.GetClamped(eas, "on/fps", d);
        var szStr  = sz.ToString("F3", ic);
        return $"zoompan=z='{szStr}-({szStr}-1)*{ease}':x='(iw-iw/zoom)/2':y='(ih-ih/zoom)/2':d={frames}:s=iw+\"x\"+ih";
    }
}
