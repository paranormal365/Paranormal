using System.Reflection;

namespace Ben.Data.Common.Menus;

/// <summary>
/// The fonts the menu designs are set in (Ben, 10/13/2026: "add some free custom design fonts").
/// </summary>
/// <remarks>
/// <para><b>Free, and kept here.</b> Static TrueType files from Google Fonts, under the SIL Open Font
/// License (Special Elite under Apache 2.0); the licenses are in <c>Menus/Fonts/licenses</c>. They are
/// embedded in this assembly once, so the API embeds them into PDFs and the website serves the same
/// files to the page, and a menu's page and its PDF are set in the same type.</para>
///
/// <para><b>Static, not variable.</b> The repository has several of these only as variable fonts,
/// which a PDF writer can't be relied on to embed at the right weight; the files here are the fixed
/// regular, semibold, bold and italic cuts.</para>
/// </remarks>
public static class MenuFonts
{
    /// <summary>One file: its family, weight (400, 600, 700), whether it's italic, and its name.</summary>
    public sealed record FontFile(string Family, int Weight, bool Italic, string File);

    public static readonly IReadOnlyList<FontFile> Files =
    [
        new("Cinzel", 400, false, "Cinzel-Regular.ttf"),
        new("Cinzel", 700, false, "Cinzel-Bold.ttf"),
        new("Cormorant Garamond", 400, false, "CormorantGaramond-Regular.ttf"),
        new("Cormorant Garamond", 400, true, "CormorantGaramond-Italic.ttf"),
        new("Cormorant Garamond", 600, false, "CormorantGaramond-SemiBold.ttf"),
        new("Cormorant Garamond", 700, false, "CormorantGaramond-Bold.ttf"),
        new("IM Fell English", 400, false, "IMFellEnglish-Regular.ttf"),
        new("IM Fell English", 400, true, "IMFellEnglish-Italic.ttf"),
        new("Pirata One", 400, false, "PirataOne-Regular.ttf"),
        new("Special Elite", 400, false, "SpecialElite-Regular.ttf"),
        new("Share Tech Mono", 400, false, "ShareTechMono-Regular.ttf"),
        new("Playfair Display", 400, false, "PlayfairDisplay-Regular.ttf"),
        new("Playfair Display", 400, true, "PlayfairDisplay-Italic.ttf"),
        new("Playfair Display", 700, false, "PlayfairDisplay-Bold.ttf"),
        new("Lora", 400, false, "Lora-Regular.ttf"),
        new("Lora", 400, true, "Lora-Italic.ttf"),
        new("Lora", 700, false, "Lora-Bold.ttf"),
        new("Montserrat", 400, false, "Montserrat-Regular.ttf"),
        new("Montserrat", 400, true, "Montserrat-Italic.ttf"),
        new("Montserrat", 600, false, "Montserrat-SemiBold.ttf"),
        new("Montserrat", 700, false, "Montserrat-Bold.ttf"),
        new("Amatic SC", 400, false, "AmaticSC-Regular.ttf"),
        new("Amatic SC", 700, false, "AmaticSC-Bold.ttf"),
        new("Arvo", 400, false, "Arvo-Regular.ttf"),
        new("Arvo", 400, true, "Arvo-Italic.ttf"),
        new("Arvo", 700, false, "Arvo-Bold.ttf"),
        new("Dancing Script", 400, false, "DancingScript-Regular.ttf"),
        new("Dancing Script", 700, false, "DancingScript-Bold.ttf"),
        // The second set, for designs 13 to 25 (10/13/2026).
        new("Creepster", 400, false, "Creepster-Regular.ttf"),
        new("Cinzel Decorative", 400, false, "CinzelDecorative-Regular.ttf"),
        new("Cinzel Decorative", 700, false, "CinzelDecorative-Bold.ttf"),
        new("Uncial Antiqua", 400, false, "UncialAntiqua-Regular.ttf"),
        new("Fondamento", 400, false, "Fondamento-Regular.ttf"),
        new("Fondamento", 400, true, "Fondamento-Italic.ttf"),
        new("Righteous", 400, false, "Righteous-Regular.ttf"),
        new("Poiret One", 400, false, "PoiretOne-Regular.ttf"),
        new("Josefin Sans", 400, false, "JosefinSans-Regular.ttf"),
        new("Josefin Sans", 400, true, "JosefinSans-Italic.ttf"),
        new("Josefin Sans", 600, false, "JosefinSans-SemiBold.ttf"),
        new("Josefin Sans", 700, false, "JosefinSans-Bold.ttf"),
        new("Pacifico", 400, false, "Pacifico-Regular.ttf"),
        new("Abril Fatface", 400, false, "AbrilFatface-Regular.ttf"),
        new("Bebas Neue", 400, false, "BebasNeue-Regular.ttf"),
        new("Shadows Into Light", 400, false, "ShadowsIntoLight-Regular.ttf"),
    ];

    /// <summary>
    /// The file closest to what's asked for: the exact cut, else the nearest weight in the same style,
    /// else the upright one. Null when the family isn't one of these.
    /// </summary>
    /// <remarks>
    /// Pirata One and Special Elite come in one weight, Cinzel has no italic: asked for those, the
    /// design gets the face it has rather than a different family, which is what keeps its look.
    /// </remarks>
    public static FontFile? Closest(string family, int weight, bool italic)
    {
        var faces = Files.Where(f => f.Family == family).ToList();
        if (faces.Count == 0) return null;
        var styled = faces.Where(f => f.Italic == italic).ToList();
        var pool = styled.Count > 0 ? styled : faces.Where(f => !f.Italic).ToList();
        return pool.OrderBy(f => Math.Abs(f.Weight - weight)).ThenByDescending(f => f.Weight).First();
    }

    /// <summary>The bytes of one file, or null when there's no such file.</summary>
    public static byte[]? Read(string file)
    {
        if (Files.All(f => f.File != file)) return null;
        using var stream = typeof(MenuFonts).Assembly.GetManifestResourceStream($"MenuFont.{file}");
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The @font-face rules for the page, pointing at <paramref name="baseUrl"/> + file name.
    /// </summary>
    /// <remarks>Built from <see cref="Files"/>, so a font added here reaches the page without a second list.</remarks>
    public static string FontFaceCss(string baseUrl)
        => string.Concat(Files.Select(f =>
            $"@font-face{{font-family:'{f.Family}';font-style:{(f.Italic ? "italic" : "normal")};font-weight:{f.Weight};"
          + $"font-display:swap;src:url('{baseUrl}{f.File}') format('truetype');}}\n"));

    /// <summary>A CSS font stack for a family, falling back to the design's own kind of type.</summary>
    public static string Stack(string? family, MenuFont fallback)
    {
        var generic = fallback switch
        {
            MenuFont.Sans => "\"Helvetica Neue\", Helvetica, Arial, sans-serif",
            MenuFont.Typewriter => "\"Courier New\", Courier, monospace",
            _ => "Georgia, \"Times New Roman\", Times, serif",
        };
        return string.IsNullOrWhiteSpace(family) ? generic : $"'{family}', {generic}";
    }
}
