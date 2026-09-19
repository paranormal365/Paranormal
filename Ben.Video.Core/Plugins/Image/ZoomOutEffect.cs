using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Image;

/// <summary>
/// Slowly zooms out from a tight crop to the full image over the display duration.
/// Inspired by animate.css <c>zoomOut</c>.
/// </summary>
public sealed class ZoomOutEffect : IClipEffect
{
    public string EffectId    => "img_zoom_out";
    public string DisplayName => "Zoom Out";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "end_zoom", Label = "End Zoom", Min = 1.1, Max = 3.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.5 },
        new() { Key = "easing",   Label = "Easing", Type = ParameterType.Select,
                Options = EasingHelper.Labels, DefaultValue = EasingHelper.EaseIn },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["end_zoom"] = 1.5, ["easing"] = EasingHelper.EaseIn },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var ez  = p.GetValueOrDefault("end_zoom", 1.5);
        var eas = (int)Math.Round(p.GetValueOrDefault("easing", EasingHelper.EaseIn));
        var d   = clipDuration;
        if (d <= 0 || ez <= 1.0) return string.Empty;

        var ease  = EasingHelper.GetClamped(eas, ZoompanFragment.TimeVariable, d);
        var ezStr = ez.ToString("F3", ic);

        return ZoompanFragment.Build(
            $"1+({ezStr}-1)*{ease}",
            ZoompanFragment.CentredX, ZoompanFragment.CentredY,
            canvasWidth, canvasHeight);
    }
}
