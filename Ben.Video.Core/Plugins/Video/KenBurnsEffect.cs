using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Ken Burns effect — a slow pan combined with a zoom, moving from one area of
/// the frame to another over the clip duration. Classic documentary/photo style.
/// </summary>
public sealed class KenBurnsEffect : IClipEffect
{
    public string EffectId    => "ken_burns";
    public string DisplayName => "Ken Burns";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "duration",   Label = "Duration (s)", Min = 1.0, Max = 30.0, Step = 0.5, LargeStep = 2.0, DefaultValue = 5.0 },
        new() { Key = "zoom",       Label = "Zoom Amount",  Min = 1.0, Max = 2.0,  Step = 0.05, LargeStep = 0.25, DefaultValue = 1.3 },
        new() { Key = "direction",  Label = "Direction", Type = ParameterType.Select,
                Options = ["Top-Left → Bottom-Right", "Bottom-Right → Top-Left",
                           "Top-Right → Bottom-Left", "Bottom-Left → Top-Right",
                           "Centre → Top-Left", "Centre → Bottom-Right"],
                DefaultValue = 0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["duration"] = 5.0, ["zoom"] = 1.3, ["direction"] = 0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var d   = Math.Min(p.GetValueOrDefault("duration", 5.0), clipDuration);
        var z   = p.GetValueOrDefault("zoom", 1.3);
        var dir = (int)Math.Round(p.GetValueOrDefault("direction", 0));
        if (d <= 0 || z <= 1.0) return string.Empty;

        var zStr = z.ToString("F3", ic);

        // Progress through the pan, from zoompan's own output clock in seconds. It used to be
        // written as on/fps, and fps is not a variable zoompan defines — see ZoompanFragment.
        var prog = $"min({ZoompanFragment.TimeVariable}/{d.ToString("F3", ic)},1)";
        // zoom: starts at z and stays (or starts at 1 and moves to z — both common)
        var zExpr = zStr;

        // x and y pan based on direction (0=top-left to bottom-right, etc.)
        string xExpr, yExpr;
        switch (dir)
        {
            case 1: // bottom-right → top-left
                xExpr = $"(iw-iw/zoom)*(1-{prog})";
                yExpr = $"(ih-ih/zoom)*(1-{prog})";
                break;
            case 2: // top-right → bottom-left
                xExpr = $"(iw-iw/zoom)*(1-{prog})";
                yExpr = $"(ih-ih/zoom)*{prog}";
                break;
            case 3: // bottom-left → top-right
                xExpr = $"(iw-iw/zoom)*{prog}";
                yExpr = $"(ih-ih/zoom)*(1-{prog})";
                break;
            case 4: // centre → top-left
                xExpr = $"(iw-iw/zoom)*0.5*(1-{prog})";
                yExpr = $"(ih-ih/zoom)*0.5*(1-{prog})";
                break;
            case 5: // centre → bottom-right
                xExpr = $"(iw-iw/zoom)*0.5*(1+{prog})";
                yExpr = $"(ih-ih/zoom)*0.5*(1+{prog})";
                break;
            default: // 0: top-left → bottom-right
                xExpr = $"(iw-iw/zoom)*{prog}";
                yExpr = $"(ih-ih/zoom)*{prog}";
                break;
        }

        return ZoompanFragment.Build(zExpr, xExpr, yExpr, canvasWidth, canvasHeight);
    }
}
