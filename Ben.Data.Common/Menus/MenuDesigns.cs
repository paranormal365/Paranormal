namespace Ben.Data.Common.Menus;

/// <summary>The type a design is set in.</summary>
/// <remarks>
/// Only the three families every PDF reader carries (Times, Helvetica, Courier), so a menu's PDF and
/// its page match without the site shipping font files. The web page uses the same three looks:
/// a serif, a sans-serif and a typewriter face.
/// </remarks>
public enum MenuFont { Serif, Sans, Typewriter }

/// <summary>The small drawing above the heading and between sections.</summary>
public enum MenuOrnament { None, Flourish, Crescent, Stars, Candle, Planchette, Waveform, Leaf, Stamp }

/// <summary>How the page is edged.</summary>
public enum MenuFrame { None, Single, Double, Corners }

/// <summary>
/// One ready-made look for a menu (Ben, 10/13/2026).
/// </summary>
/// <remarks>
/// <para>Ben: "You should make menu designs as other menus online look. Also, have menus that are
/// named and made to emphasize the ghost hunting aspect. Not all will be this way there will also be
/// some generic-based or all-purpose looks."</para>
///
/// <para><b>One description drives both drawings</b>: the page (<c>EventMenuDocument</c>) and the PDF
/// (<c>EventMenuPdf</c>) read the same colors, type, frame and ornament from here, so the preview a
/// planner judges is the PDF they hand out.</para>
/// </remarks>
/// <param name="Paper">The page color.</param>
/// <param name="Ink">Body text.</param>
/// <param name="Accent">Headings, rules, ornaments and prices.</param>
/// <param name="Muted">Descriptions and small print.</param>
/// <param name="UpperHeadings">Section headings in capitals, letter-spaced.</param>
/// <param name="TwoColumns">Sections in two columns on a wide page and in the PDF.</param>
/// <param name="DottedLeaders">A dotted line from an item's name to its price.</param>
public sealed record MenuDesign(
    string Key,
    string Name,
    string Family,
    string Description,
    MenuFont Font,
    string Paper,
    string Ink,
    string Accent,
    string Muted,
    MenuFrame Frame,
    MenuOrnament Ornament,
    bool UpperHeadings,
    bool TwoColumns,
    bool DottedLeaders)
{
    public bool IsDark => Luminance(Paper) < 0.35;

    private static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        double C(int i) => Convert.ToInt32(h.Substring(i, 2), 16) / 255.0;
        return 0.2126 * C(0) + 0.7152 * C(2) + 0.0722 * C(4);
    }
}

/// <summary>Every design, ghost hunting first.</summary>
public static class MenuDesigns
{
    public const string GhostHunting = "Ghost hunting";
    public const string General = "General";

    public static readonly IReadOnlyList<MenuDesign> All =
    [
        new("seance-parlor", "Séance Parlor", GhostHunting,
            "Victorian gold on deep oxblood, framed twice, like a card left on a parlor table.",
            MenuFont.Serif, "#2A0E12", "#F1E4C8", "#C9A24A", "#C8B79A",
            MenuFrame.Double, MenuOrnament.Flourish, UpperHeadings: true, TwoColumns: false, DottedLeaders: true),
        new("midnight-vigil", "Midnight Vigil", GhostHunting,
            "Candlelight on a midnight sky, for the meal before the lights go out.",
            MenuFont.Serif, "#0F1A2E", "#E8E6DF", "#F2C46D", "#A9B3C4",
            MenuFrame.Single, MenuOrnament.Crescent, UpperHeadings: false, TwoColumns: false, DottedLeaders: false),
        new("spirit-board", "Spirit Board", GhostHunting,
            "Aged sepia and a planchette, in the style of an old talking board.",
            MenuFont.Serif, "#E9D8B4", "#3B2A17", "#7A4B1F", "#6E5A40",
            MenuFrame.Corners, MenuOrnament.Planchette, UpperHeadings: true, TwoColumns: true, DottedLeaders: true),
        new("haunted-manor", "Haunted Manor", GhostHunting,
            "Gothic black and crimson, for a house with a reputation.",
            MenuFont.Serif, "#121012", "#ECE6E6", "#B3263A", "#A79C9E",
            MenuFrame.Double, MenuOrnament.Candle, UpperHeadings: true, TwoColumns: false, DottedLeaders: false),
        new("case-file", "Case File", GhostHunting,
            "Typed on a manila folder and stamped, like the investigation's own paperwork.",
            MenuFont.Typewriter, "#EFE3C2", "#1F1F1F", "#B3261E", "#5A5348",
            MenuFrame.Single, MenuOrnament.Stamp, UpperHeadings: true, TwoColumns: false, DottedLeaders: true),
        new("evp-static", "EVP Static", GhostHunting,
            "A recorder's waveform on a dark screen: modern, spare and a little uneasy.",
            MenuFont.Sans, "#0B0F14", "#D7F9FF", "#22D3EE", "#7FA3AD",
            MenuFrame.None, MenuOrnament.Waveform, UpperHeadings: true, TwoColumns: true, DottedLeaders: false),

        new("classic-bistro", "Classic Bistro", General,
            "Cream paper, a burgundy flourish and a serif face: the restaurant menu everybody knows.",
            MenuFont.Serif, "#FBF6EC", "#2B2B2B", "#7B2D26", "#6B6460",
            MenuFrame.Single, MenuOrnament.Flourish, UpperHeadings: true, TwoColumns: false, DottedLeaders: true),
        new("modern-minimal", "Modern Minimal", General,
            "White space, clean type and nothing else.",
            MenuFont.Sans, "#FFFFFF", "#151515", "#151515", "#6B6B6B",
            MenuFrame.None, MenuOrnament.None, UpperHeadings: true, TwoColumns: true, DottedLeaders: false),
        new("chalkboard-cafe", "Chalkboard Café", General,
            "Chalk on a slate board, like the specials by the door.",
            MenuFont.Sans, "#2B3330", "#F4F1EA", "#F6D77B", "#C3C7C0",
            MenuFrame.Single, MenuOrnament.Stars, UpperHeadings: true, TwoColumns: false, DottedLeaders: true),
        new("rustic-farmhouse", "Rustic Farmhouse", General,
            "Kraft paper and forest green, for a long table and a big pot.",
            MenuFont.Serif, "#E7DCC6", "#3A2F25", "#4E6B3A", "#6D604F",
            MenuFrame.Double, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: false, DottedLeaders: true),
        new("garden-party", "Garden Party", General,
            "Soft green and rose, light enough for a brunch in the garden.",
            MenuFont.Serif, "#F6FBF4", "#24352A", "#C2557A", "#5E7365",
            MenuFrame.Corners, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: true, DottedLeaders: false),
        new("elegant-evening", "Elegant Evening", General,
            "Black and gold, spaced capitals and a fine double frame, for a dressed-up dinner.",
            MenuFont.Serif, "#0D0D0D", "#F5F1E8", "#D4AF37", "#B8B0A0",
            MenuFrame.Double, MenuOrnament.Stars, UpperHeadings: true, TwoColumns: false, DottedLeaders: true),
    ];

    /// <summary>What a menu is drawn in when it hasn't chosen, or chose one that has since gone.</summary>
    public static MenuDesign Default => All.First(d => d.Key == "classic-bistro");

    public static MenuDesign? Find(string? key) => All.FirstOrDefault(d => d.Key == key);

    public static MenuDesign FindOrDefault(string? key) => Find(key) ?? Default;

    /// <summary>The credit at the foot of every menu (Ben, 10/13/2026).</summary>
    public static string Credit(int year) => $"Menu made with IsHaunted.com · © {year} ISHAUNTED LLC";
}
