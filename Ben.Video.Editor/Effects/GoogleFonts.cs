namespace Ben.Video.Editor.Effects;

/// <summary>
/// Curated Google Fonts family names offered in text/callout font pickers, alongside
/// <see cref="StandardFonts"/>'s system fonts (backlog item #16, phase 116). Unlike system fonts,
/// these need a web-font load before they'll actually render — see
/// <see cref="Services.GoogleFontService"/> — so <see cref="IsGoogleFont"/> lets callers skip that
/// step entirely for the common system-font case.
/// </summary>
public static class GoogleFonts
{
    public static readonly IReadOnlyList<string> Names =
    [
        "Roboto", "Open Sans", "Lato", "Montserrat", "Poppins",
        "Oswald", "Merriweather", "Playfair Display", "Inter", "Nunito",
        "Raleway", "Ubuntu", "Source Sans Pro", "PT Sans", "Rubik",
    ];

    private static readonly HashSet<string> NameSet = new(Names, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="fontFamily"/> is one of the curated Google Fonts names
    /// (not a system font) — case-insensitive, since the value ultimately round-trips through
    /// project JSON and a user's saved project shouldn't break over casing.</summary>
    public static bool IsGoogleFont(string? fontFamily) =>
        fontFamily is not null && NameSet.Contains(fontFamily);
}
