using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Fades in from a user-selected colour at the start of the clip.
/// Extends the existing <c>fade=t=in</c> with a configurable start colour
/// (instead of always fading from black). Supports any opaque colour.
/// Inspired by animate.css <c>fadeIn</c> but with colour control.
/// </summary>
public sealed class FadeFromColorEffect : IClipEffect
{
    public string EffectId    => "fade_from_color";
    public string DisplayName => "Fade From Color";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration", Label = "Duration (s)",
                Min = 0.1, Max = 10.0, Step = 0.1, LargeStep = 1.0, DefaultValue = 1.0 },
        new() { Key = "color",    Label = "Fade Color",
                Type = ParameterType.Color, DefaultValue = ColorHelper.OpaqueBlack },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 1.0, ["color"] = ColorHelper.OpaqueBlack },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 1.0), clipDuration);
        var col = p.GetValueOrDefault("color", ColorHelper.OpaqueBlack);
        if (d <= 0) return string.Empty;

        return $"fade=t=in:st=0:d={d.ToString("F3", ic)}:color={ColorHelper.ToFfmpegColor(col)}";
    }
}
