namespace Ben.Web.Services;

/// <summary>
/// Maps stored Bootstrap colour classes onto ones that follow the viewer's light/dark theme.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> An organization picks a colour for a calendar event type or an
/// experience category, and what gets stored is a CSS class string, not a hex code. Some of the
/// classes on offer were pinned to a literal colour: <c>text-dark</c> resolves through
/// <c>--bs-dark-rgb</c>, which this theme never redefines, so "Black" stayed black when the page
/// went dark and the label disappeared into the background. Item 132.</para>
///
/// <para><b>Why a translation and not just a better picker.</b> Fixing the dropdown only helps the
/// next choice. These values are already in the database, and <c>AdminLookupTypes</c> lets a
/// SuperAdmin type any class they like into a free-text box, so unusable values can arrive after
/// this change as easily as before it. Correcting at the point of render covers stored data, the
/// free-text box, and anything seeded, in one place.</para>
///
/// <para><b>What is deliberately left alone.</b> <c>text-warning</c>, <c>text-info</c> and the
/// rest of the semantic colours are the same in both themes by design, and are perfectly readable
/// in each — they are choices, not mistakes. Only the classes with no dark-mode definition are
/// rewritten.</para>
/// </remarks>
public static class ThemeSafeColorExtensions
{
    /// <summary>
    /// Colour classes with no dark-theme definition, and the theme-aware class that carries the
    /// same intent. <c>text-body-emphasis</c> is the strongest plain text colour in either theme —
    /// black on light, white on dark — which is what somebody choosing "Black" was reaching for.
    /// </summary>
    private static readonly Dictionary<string, string> Replacements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text-dark"]  = "text-body-emphasis",
        ["text-black"] = "text-body-emphasis",
        ["text-light"] = "text-body-secondary",
        ["text-white"] = "text-body-emphasis",
    };

    /// <summary>
    /// Returns the class unchanged unless it is pinned to one theme, in which case it returns the
    /// theme-aware equivalent. Null and blank pass straight through — "no colour chosen" is a
    /// real answer and inherits the surrounding text colour, which already follows the theme.
    /// </summary>
    /// <remarks>
    /// Handles a value holding several classes, because the free-text admin box allows it.
    /// </remarks>
    public static string? ToThemeSafeColorClass(this string? colorClass)
    {
        if (string.IsNullOrWhiteSpace(colorClass)) return colorClass;

        var parts = colorClass.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mapped = parts.Select(p => Replacements.TryGetValue(p, out var better) ? better : p);

        return string.Join(' ', mapped);
    }
}
