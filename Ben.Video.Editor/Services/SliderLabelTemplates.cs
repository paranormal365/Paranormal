using Microsoft.AspNetCore.Components;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #30 fix — every Properties-panel <c>TelerikSlider</c> already shows its live value in a
/// label above the track, so Kendo's default per-<c>LargeStep</c> tick labels are redundant and,
/// at typical docked-panel widths, bunch into an unreadable overlapping mess. This renders only
/// the first and last generated tick instead, keeping a scale reference without the crowding.
/// Generic over <c>TValue</c> because <c>TelerikSlider&lt;int&gt;</c> and
/// <c>TelerikSlider&lt;double&gt;</c> both appear across the editor's Properties panels.
///
/// <para>Kendo generates ticks at <c>Min + n*LargeStep</c> and stops at the last one that does not
/// exceed <c>Max</c> — it does NOT add a bonus tick exactly at <c>Max</c> when the range isn't an
/// exact multiple of <c>LargeStep</c> (true for most duration-bound sliders, e.g. a 13.8s clip).
/// Matching tick against <c>Max</c> exactly would then label nothing at the high end. Instead this
/// labels the highest tick Kendo actually renders (<c>tick + LargeStep &gt; Max</c>) — a real,
/// honest position on the track, not a synthetic boundary.</para>
/// </summary>
public static class SliderLabelTemplates
{
    /// <summary>Label text for a given tick, or null to render nothing at that tick.</summary>
    public static string? LabelFor<TValue>(TValue tick, TValue min, TValue max, TValue largeStep) where TValue : IConvertible
    {
        var t = tick.ToDouble(null);
        var mn = min.ToDouble(null);
        var mx = max.ToDouble(null);
        var step = largeStep.ToDouble(null);

        var isFirst = IsCloseTo(t, mn);
        var isLast = step > 0 && t + step > mx + 1e-9;

        if (!isFirst && !isLast)
            return null;

        return t == Math.Floor(t) && Math.Abs(t) < 1_000_000
            ? ((long)t).ToString()
            : t.ToString("0.##");
    }

    private static bool IsCloseTo(double value, double target) => Math.Abs(value - target) < 0.0001;

    public static RenderFragment<TValue> Endpoints<TValue>(TValue min, TValue max, TValue largeStep) where TValue : IConvertible => tick => builder =>
    {
        var text = LabelFor(tick, min, max, largeStep);
        if (text is not null)
            builder.AddContent(0, text);
    };
}
