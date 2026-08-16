using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Zooms the clip from a wider view into the normal frame over the animation duration.
/// Inspired by animate.css <c>zoomIn</c>.
/// </summary>
public sealed class ZoomInEffect : IClipEffect
{
    public string EffectId    => "zoom_in";
    public string DisplayName => "Zoom In";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration",   Label = "Duration (s)", Min = 0.1, Max = 10.0, Step = 0.1, LargeStep = 1.0, DefaultValue = 2.0 },
        new() { Key = "start_zoom", Label = "Start Zoom", Min = 1.1, Max = 3.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.5 },
        new() { Key = "easing",     Label = "Easing", Type = ParameterType.Select,
                Options = EasingHelper.Labels, DefaultValue = EasingHelper.EaseOut },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 2.0, ["start_zoom"] = 1.5, ["easing"] = EasingHelper.EaseOut },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 2.0), clipDuration);
        var sz  = p.GetValueOrDefault("start_zoom", 1.5);
        var eas = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseOut));
        if (d <= 0) return string.Empty;

        // zoompan: zoom decreases from sz→1 over d seconds, centred
        // z=sz - (sz-1)*ease; x/y centred on the current zoom
        var fps   = 25; // conservative; actual fps comes from source
        var frames = (int)Math.Ceiling(d * fps);
        var ease  = EasingHelper.GetClamped(eas, "on/fps", d);
        var szStr = sz.ToString("F3", ic);
        return $"zoompan=z='{szStr}-({szStr}-1)*{ease}':x='(iw-iw/zoom)/2':y='(ih-ih/zoom)/2':d={frames}:s=iw+\"x\"+ih";
    }
}
