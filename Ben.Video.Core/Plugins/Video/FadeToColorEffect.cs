using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Fades out to a user-selected colour at the end of the clip.
/// Extends the existing <c>fade=t=out</c> with a configurable end colour.
/// </summary>
public sealed class FadeToColorEffect : IClipEffect
{
    public string EffectId    => "fade_to_color";
    public string DisplayName => "Fade To Color";

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

        var st  = Math.Max(0, clipDuration - d);
        return $"fade=t=out:st={st.ToString("F3", ic)}:d={d.ToString("F3", ic)}:color={ColorHelper.ToFfmpegColor(col)}";
    }
}
