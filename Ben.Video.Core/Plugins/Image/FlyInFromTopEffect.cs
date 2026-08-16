using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Image;

/// <summary>
/// Slides the video frame in from the top over the specified duration using the
/// ffmpeg <c>crop</c> + <c>pad</c> approach. The frame starts fully above the
/// visible area and descends to its final position.
/// </summary>
public sealed class FlyInFromTopEffect : IClipEffect
{
    public string EffectId    => "fly_in_from_top";
    public string DisplayName => "Fly In from Top";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration", Label = "Duration (s)", Min = 0.1, Max = 5.0, Step = 0.1, LargeStep = 0.5, DefaultValue = 0.5 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 0.5 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var d  = Math.Min(p.GetValueOrDefault("duration", 0.5), clipDuration);
        if (d <= 0) return string.Empty;

        // Animate y-offset from -h (off-screen top) to 0 over `d` seconds.
        // ffmpeg expression: if(lt(t,dur), (-1+t/dur)*h, 0)
        var dStr = d.ToString("F3", ic);
        return $"pad=iw:ih*2:0:ih,crop=iw:ih:0:'if(lt(t,{dStr}),(-1+t/{dStr})*ih,0)'";
    }
}

