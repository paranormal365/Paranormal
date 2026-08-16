using System.Globalization;

namespace Ben.Video.Editor.Effects;

/// <summary>
/// Provides ffmpeg filter expression fragments for the easing types defined in
/// <c>docs/easings.css</c> (Elastic, Bounce) and standard CSS easing curves.
///
/// All expressions evaluate to a progress value in [0, 1] given <c>t</c> (seconds)
/// and <c>d</c> (effect duration in seconds). Values may briefly exceed [0, 1] for
/// Elastic — callers should clamp with <c>min(max(expr, 0), 1)</c> where needed.
/// </summary>
public static class EasingHelper
{
    // ── Named indices (stored as double in AppliedEffect.Parameters) ──────────

    public const int Linear    = 0;
    public const int EaseIn    = 1;
    public const int EaseOut   = 2;
    public const int EaseInOut = 3;
    public const int Bounce    = 4;
    public const int Elastic   = 5;

    /// <summary>Human-readable labels for the effect dropdown, ordered by index.</summary>
    public static readonly IReadOnlyList<string> Labels =
        ["Linear", "Ease In", "Ease Out", "Ease In/Out", "Bounce Out", "Elastic Out"];

    // ── ffmpeg expression builder ──────────────────────────────────────────────

    /// <summary>
    /// Returns an ffmpeg-expression string that evaluates to the eased progress [0,1]
    /// for frame time <paramref name="tVar"/> (e.g. <c>"t"</c>) and duration <paramref name="duration"/>.
    ///
    /// Use this inside a <c>crop</c>, <c>zoompan</c>, <c>rotate</c>, or <c>geq</c>
    /// expression by substituting the returned string for the progress variable.
    /// </summary>
    /// <param name="easingIndex">One of the <c>EasingHelper.Linear … Elastic</c> constants.</param>
    /// <param name="tVar">The ffmpeg variable name for current time in seconds (usually <c>"t"</c>).</param>
    /// <param name="duration">Effect duration in seconds.</param>
    public static string GetExpression(int easingIndex, string tVar, double duration)
    {
        var ic = CultureInfo.InvariantCulture;
        var d  = duration.ToString("F3", ic);

        // p = linear progress [0,1], clamped so it doesn't exceed 1 after duration
        var p = $"min({tVar}/{d},1)";

        return easingIndex switch
        {
            EaseIn    => $"pow({p},2)",
            EaseOut   => $"(1-pow(1-{p},2))",
            EaseInOut => $"if(lt({p},0.5),2*pow({p},2),1-2*pow(1-{p},2))",

            // Bounce Out — approximates the CSS easeOutBounce keyframes from easings.css
            // Uses a damped cosine that settles to 1 as p→1
            Bounce    => $"(1-abs(cos(3.14159*{p}*2.5))*pow(1-{p},2))",

            // Elastic Out — approximates the CSS easeOutElastic keyframes from easings.css
            // Overshoots and oscillates; clamped to [0,1] by callers
            Elastic   => $"(pow(2,-10*{p})*sin(({p}-0.075)*2*3.14159/0.3)+1)",

            // Linear (default)
            _ => p,
        };
    }

    /// <summary>
    /// Shorthand: returns a complete progress expression clamped to [0,1].
    /// Use for any motion expression where overshoot is undesirable.
    /// </summary>
    public static string GetClamped(int easingIndex, string tVar, double duration)
        => $"min(max({GetExpression(easingIndex, tVar, duration)},0),1)";
}
