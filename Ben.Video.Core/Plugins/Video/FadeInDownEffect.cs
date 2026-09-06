using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Fades in while sliding down from above. Inspired by animate.css <c>fadeInDown</c>.
/// Combines <c>fade=in</c> with a crop that slides from the top.
/// </summary>
public sealed class FadeInDownEffect : IClipEffect
{
    public string EffectId    => "fade_in_down";
    public string DisplayName => "Fade In Down";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration", Label = "Duration (s)", Min = 0.1, Max = 5.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 0.6 },
        new() { Key = "distance", Label = "Slide Distance (%)", Min = 5.0, Max = 100.0, Step = 5.0, LargeStep = 20.0, DefaultValue = 30.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 0.6, ["distance"] = 30.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic   = System.Globalization.CultureInfo.InvariantCulture;
        var d    = Math.Min(p.GetValueOrDefault("duration", 0.6), clipDuration);
        var dist = p.GetValueOrDefault("distance", 30.0) / 100.0; // fraction of frame height
        if (d <= 0) return string.Empty;

        var dStr    = d.ToString("F3", ic);
        var distStr = dist.ToString("F3", ic);

        // Slide: content moves from above (negative y) to 0 using ease-out
        var ease    = EasingHelper.GetClamped(EasingHelper.EaseOut, "t", d);
        var slideY  = $"if(lt(t,{dStr}),ih*{distStr}*(1-{ease})*(-1),0)";
        var padPx   = (int)Math.Ceiling(dist * 1080) + 4; // generous padding for slide
        var slide   = $"pad=iw:ih+{padPx}:0:{padPx}:color=black,crop=iw:ih:0:'max(0,{padPx}+({slideY}))'";
        var fade    = $"fade=t=in:st=0:d={dStr}";
        return $"{slide},{fade}";
    }
}
