using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Image;

/// <summary>
/// Ken Burns effect on still images — slow pan combined with a zoom,
/// moving from one area of the image to another over the display duration.
/// </summary>
public sealed class KenBurnsEffect : IClipEffect
{
    public string EffectId    => "img_ken_burns";
    public string DisplayName => "Ken Burns";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "zoom",      Label = "Zoom Amount", Min = 1.0, Max = 2.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.3 },
        new() { Key = "direction", Label = "Direction", Type = ParameterType.Select,
                Options = ["Top-Left → Bottom-Right", "Bottom-Right → Top-Left",
                           "Top-Right → Bottom-Left", "Bottom-Left → Top-Right",
                           "Centre → Corners", "Corners → Centre"],
                DefaultValue = 0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["zoom"] = 1.3, ["direction"] = 0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = clipDuration;
        var z   = p.GetValueOrDefault("zoom", 1.3);
        var dir = (int)Math.Round(p.GetValueOrDefault("direction", 0));
        if (d <= 0 || z <= 1.0) return string.Empty;

        var zStr = z.ToString("F3", ic);
        var dStr = d.ToString("F3", ic);

        // See ZoompanFragment: on/fps names a variable zoompan does not define.
        var prog = $"min({ZoompanFragment.TimeVariable}/{dStr},1)";

        string xExpr, yExpr;
        switch (dir)
        {
            case 1: xExpr = $"(iw-iw/{zStr})*(1-{prog})"; yExpr = $"(ih-ih/{zStr})*(1-{prog})"; break;
            case 2: xExpr = $"(iw-iw/{zStr})*(1-{prog})"; yExpr = $"(ih-ih/{zStr})*{prog}"; break;
            case 3: xExpr = $"(iw-iw/{zStr})*{prog}";     yExpr = $"(ih-ih/{zStr})*(1-{prog})"; break;
            case 4: xExpr = $"(iw-iw/{zStr})*0.5*(1-{prog})"; yExpr = $"(ih-ih/{zStr})*0.5*(1-{prog})"; break;
            case 5: xExpr = $"(iw-iw/{zStr})*0.5*(1+{prog})"; yExpr = $"(ih-ih/{zStr})*0.5*(1+{prog})"; break;
            default: xExpr = $"(iw-iw/{zStr})*{prog}";    yExpr = $"(ih-ih/{zStr})*{prog}"; break;
        }

        return ZoompanFragment.Build(zStr, xExpr, yExpr, canvasWidth, canvasHeight);
    }
}
