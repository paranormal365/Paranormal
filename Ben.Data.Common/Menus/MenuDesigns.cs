namespace Ben.Data.Common.Menus;

/// <summary>The type a design is set in.</summary>
/// <remarks>
/// Only the three families every PDF reader carries (Times, Helvetica, Courier), so a menu's PDF and
/// its page match without the site shipping font files. The web page uses the same three looks:
/// a serif, a sans-serif and a typewriter face.
/// </remarks>
public enum MenuFont { Serif, Sans, Typewriter }

/// <summary>The small drawing above the heading and between sections.</summary>
public enum MenuOrnament { None, Flourish, Crescent, Stars, Candle, Planchette, Waveform, Leaf, Stamp, Ghost, Bat }

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
/// <param name="HeadingFace">The family the heading and section titles are set in (<see cref="MenuFonts"/>).</param>
/// <param name="BodyFace">The family everything else is set in.</param>
/// <param name="HeadingWeight">The heading's weight: 700, or 400 for a face that is already heavy.</param>
/// <param name="HeadingScale">How much larger or smaller the heading sets than a plain serif would,
/// for a face like Amatic SC that is tall and narrow.</param>
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
    bool DottedLeaders,
    string? HeadingFace = null,
    string? BodyFace = null,
    int HeadingWeight = 700,
    double HeadingScale = 1.0)
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

    /// <remarks>In the order the picker shows them, the strongest of each family first (Ben, 10/13/2026:
    /// "update the previews to use the best looking designs").</remarks>
    public static readonly IReadOnlyList<MenuDesign> All =
    [
        new("seance-parlor", "Séance Parlor", GhostHunting,
            "Victorian gold on deep oxblood, framed twice, like a card left on a parlor table.",
            MenuFont.Serif, "#2A0E12", "#F1E4C8", "#C9A24A", "#C8B79A",
            MenuFrame.Double, MenuOrnament.Flourish, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Cinzel", BodyFace: "Cormorant Garamond", HeadingScale: 0.92),
        new("haunted-manor", "Haunted Manor", GhostHunting,
            "Gothic black and crimson in a blackletter hand, for a house with a reputation.",
            MenuFont.Serif, "#121012", "#ECE6E6", "#C22E44", "#A79C9E",
            MenuFrame.Double, MenuOrnament.Candle, UpperHeadings: false, TwoColumns: false, DottedLeaders: false,
            HeadingFace: "Pirata One", BodyFace: "Cormorant Garamond", HeadingWeight: 400, HeadingScale: 1.15),
        new("spirit-board", "Spirit Board", GhostHunting,
            "Aged sepia, old print and a planchette, in the style of a talking board.",
            MenuFont.Serif, "#E9D8B4", "#3B2A17", "#7A4B1F", "#6E5A40",
            MenuFrame.Corners, MenuOrnament.Planchette, UpperHeadings: true, TwoColumns: true, DottedLeaders: true,
            HeadingFace: "IM Fell English", BodyFace: "IM Fell English", HeadingWeight: 400, HeadingScale: 1.05),
        new("midnight-vigil", "Midnight Vigil", GhostHunting,
            "Candlelight on a midnight sky, for the meal before the lights go out.",
            MenuFont.Serif, "#0F1A2E", "#E8E6DF", "#F2C46D", "#A9B3C4",
            MenuFrame.Single, MenuOrnament.Crescent, UpperHeadings: false, TwoColumns: false, DottedLeaders: false,
            HeadingFace: "Cormorant Garamond", BodyFace: "Cormorant Garamond", HeadingScale: 1.1),
        new("case-file", "Case File", GhostHunting,
            "Typed on a manila folder and stamped, like the investigation's own paperwork.",
            MenuFont.Typewriter, "#EFE3C2", "#1F1F1F", "#B3261E", "#5A5348",
            MenuFrame.Single, MenuOrnament.Stamp, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Special Elite", BodyFace: "Special Elite", HeadingWeight: 400, HeadingScale: 0.95),
        new("evp-static", "EVP Static", GhostHunting,
            "A recorder's waveform on a dark screen: modern, spare and a little uneasy.",
            MenuFont.Sans, "#0B0F14", "#D7F9FF", "#22D3EE", "#7FA3AD",
            MenuFrame.None, MenuOrnament.Waveform, UpperHeadings: true, TwoColumns: true, DottedLeaders: false,
            HeadingFace: "Share Tech Mono", BodyFace: "Montserrat", HeadingWeight: 400, HeadingScale: 0.95),
        new("witching-hour", "Witching Hour", GhostHunting,
            "Lavender on deep violet in ornate capitals, for the meal at midnight.",
            MenuFont.Serif, "#1E1030", "#EFE7FA", "#C9A7FF", "#A897C2",
            MenuFrame.Double, MenuOrnament.Crescent, UpperHeadings: false, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Cinzel Decorative", BodyFace: "Cormorant Garamond", HeadingScale: 0.9),
        new("graveyard-shift", "Graveyard Shift", GhostHunting,
            "Dripping letters in moss green on charcoal, for a night that doesn't take itself too seriously.",
            MenuFont.Sans, "#16191A", "#E4E9E2", "#8BC34A", "#98A39A",
            MenuFrame.Single, MenuOrnament.Bat, UpperHeadings: false, TwoColumns: false, DottedLeaders: false,
            HeadingFace: "Creepster", BodyFace: "Montserrat", HeadingWeight: 400, HeadingScale: 1.1),
        new("victorian-mourning", "Victorian Mourning", GhostHunting,
            "Black-edged white card and an engraved italic, as a house in mourning would set its table.",
            MenuFont.Serif, "#F7F4EE", "#1A1A1A", "#1A1A1A", "#5E5A54",
            MenuFrame.Double, MenuOrnament.Flourish, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Playfair Display", BodyFace: "Cormorant Garamond"),
        new("hallowed-ground", "Hallowed Ground", GhostHunting,
            "Weathered stone and an old uncial hand, like the lettering on a churchyard gate.",
            MenuFont.Serif, "#E4E1DA", "#2E2A26", "#3E5641", "#6B665E",
            MenuFrame.Corners, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Uncial Antiqua", BodyFace: "Lora", HeadingWeight: 400, HeadingScale: 0.95),
        new("lantern-light", "Lantern Light", GhostHunting,
            "Amber paper and a calligraphic hand, read by the light of a lantern on a ghost walk.",
            MenuFont.Serif, "#F3E3C3", "#3A2A1A", "#A4471C", "#7A6248",
            MenuFrame.Corners, MenuOrnament.Candle, UpperHeadings: false, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Fondamento", BodyFace: "Lora", HeadingWeight: 400, HeadingScale: 1.05),
        new("ectoplasm", "Ectoplasm", GhostHunting,
            "Glowing green on black and a rounded display face: a spirit caught on a night-vision camera.",
            MenuFont.Sans, "#0C110C", "#DDF5D6", "#7CFF6B", "#86A882",
            MenuFrame.None, MenuOrnament.Ghost, UpperHeadings: true, TwoColumns: true, DottedLeaders: false,
            HeadingFace: "Righteous", BodyFace: "Montserrat", HeadingWeight: 400),
        new("asylum-records", "Asylum Records", GhostHunting,
            "Institutional green paper, a typewriter and a rubber stamp, from the files of a closed hospital.",
            MenuFont.Typewriter, "#DDE5D8", "#1E2620", "#4A5D4F", "#5C6A5F",
            MenuFrame.Single, MenuOrnament.Stamp, UpperHeadings: true, TwoColumns: true, DottedLeaders: true,
            HeadingFace: "Special Elite", BodyFace: "Special Elite", HeadingWeight: 400, HeadingScale: 0.95),

        new("classic-bistro", "Classic Bistro", General,
            "Cream paper, a burgundy flourish and a fine serif: the restaurant menu everybody knows.",
            MenuFont.Serif, "#FBF6EC", "#2B2B2B", "#7B2D26", "#6B6460",
            MenuFrame.Single, MenuOrnament.Flourish, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Playfair Display", BodyFace: "Lora"),
        new("elegant-evening", "Elegant Evening", General,
            "Black and gold, spaced capitals and a fine double frame, for a dressed-up dinner.",
            MenuFont.Serif, "#0D0D0D", "#F5F1E8", "#D4AF37", "#B8B0A0",
            MenuFrame.Double, MenuOrnament.Stars, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Cinzel", BodyFace: "Cormorant Garamond", HeadingScale: 0.92),
        new("garden-party", "Garden Party", General,
            "Soft green and rose with a handwritten heading, light enough for a brunch in the garden.",
            MenuFont.Serif, "#F6FBF4", "#24352A", "#C2557A", "#5E7365",
            MenuFrame.Corners, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: true, DottedLeaders: false,
            HeadingFace: "Dancing Script", BodyFace: "Lora", HeadingScale: 1.2),
        new("chalkboard-cafe", "Chalkboard Café", General,
            "Chalk lettering on a slate board, like the specials by the door.",
            MenuFont.Sans, "#2B3330", "#F4F1EA", "#F6D77B", "#C3C7C0",
            MenuFrame.Single, MenuOrnament.Stars, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Amatic SC", BodyFace: "Montserrat", HeadingScale: 1.45),
        new("rustic-farmhouse", "Rustic Farmhouse", General,
            "Kraft paper, a sturdy slab face and forest green, for a long table and a big pot.",
            MenuFont.Serif, "#E7DCC6", "#3A2F25", "#4E6B3A", "#6D604F",
            MenuFrame.Double, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Arvo", BodyFace: "Arvo", HeadingScale: 0.95),
        new("modern-minimal", "Modern Minimal", General,
            "White space, a clean geometric face and nothing else.",
            MenuFont.Sans, "#FFFFFF", "#151515", "#151515", "#6B6B6B",
            MenuFrame.None, MenuOrnament.None, UpperHeadings: true, TwoColumns: true, DottedLeaders: false,
            HeadingFace: "Montserrat", BodyFace: "Montserrat", HeadingScale: 0.95),
        new("art-deco", "Art Deco", General,
            "Navy and gold, thin geometric capitals and a double frame, from a 1920s hotel ballroom.",
            MenuFont.Sans, "#14213D", "#F2EBDD", "#E5C07B", "#AEB6C8",
            MenuFrame.Double, MenuOrnament.Stars, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Poiret One", BodyFace: "Josefin Sans", HeadingWeight: 400, HeadingScale: 1.05),
        new("retro-diner", "Retro Diner", General,
            "Cherry red and a looping script on cream, like the menu at a roadside diner.",
            MenuFont.Sans, "#FFF6E5", "#2A2A2A", "#D7263D", "#5F7B7A",
            MenuFrame.Single, MenuOrnament.Stars, UpperHeadings: false, TwoColumns: true, DottedLeaders: true,
            HeadingFace: "Pacifico", BodyFace: "Montserrat", HeadingWeight: 400, HeadingScale: 1.0),
        new("harvest-table", "Harvest Table", General,
            "A heavy display face in pumpkin orange on warm paper, for an autumn supper.",
            MenuFont.Serif, "#FBF1E6", "#3B2B1F", "#B5541C", "#7C6652",
            MenuFrame.Corners, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Abril Fatface", BodyFace: "Lora", HeadingWeight: 400, HeadingScale: 1.0),
        new("bold-poster", "Bold Poster", General,
            "Tall condensed capitals in black and red, like a gig poster on a lamppost.",
            MenuFont.Sans, "#FFFFFF", "#111111", "#E63946", "#555555",
            MenuFrame.None, MenuOrnament.None, UpperHeadings: true, TwoColumns: true, DottedLeaders: false,
            HeadingFace: "Bebas Neue", BodyFace: "Montserrat", HeadingWeight: 400, HeadingScale: 1.35),
        new("coastal-breeze", "Coastal Breeze", General,
            "Sea blue on pale sky and a clean geometric face, for a lighthouse or a lake house.",
            MenuFont.Sans, "#EEF6FA", "#16324A", "#1F5F8B", "#5B7A90",
            MenuFrame.Single, MenuOrnament.Waveform, UpperHeadings: true, TwoColumns: false, DottedLeaders: true,
            HeadingFace: "Josefin Sans", BodyFace: "Josefin Sans", HeadingScale: 1.0),
        new("handwritten-notes", "Handwritten Notes", General,
            "Blue ink handwriting on white, as if the cook wrote tonight's menu by hand.",
            MenuFont.Serif, "#FDFCF7", "#2B2F3A", "#3A5BA0", "#6A7080",
            MenuFrame.Corners, MenuOrnament.Leaf, UpperHeadings: false, TwoColumns: false, DottedLeaders: false,
            HeadingFace: "Shadows Into Light", BodyFace: "Lora", HeadingWeight: 400, HeadingScale: 1.3),
    ];

    /// <summary>The look a new menu starts in: the strongest of the ghost-hunting designs.</summary>
    public static MenuDesign Featured => All[0];

    /// <summary>What a menu is drawn in when it hasn't chosen, or chose one that has since gone.</summary>
    public static MenuDesign Default => All.First(d => d.Key == "classic-bistro");

    public static MenuDesign? Find(string? key) => All.FirstOrDefault(d => d.Key == key);

    public static MenuDesign FindOrDefault(string? key) => Find(key) ?? Default;

    /// <summary>The credit at the foot of every menu (Ben, 10/13/2026).</summary>
    public static string Credit(int year) => $"IsHaunted.com © {year} ISHAUNTED LLC";

    /// <summary>
    /// Where the website serves the small site mark drawn beside the credit (event-scenario-gaps: the
    /// menus branch's notes promised "the small site mark beside it", and no design drew one).
    /// </summary>
    public const string CreditMarkPath = "/favicon.png";

    /// <summary>The same mark as JPEG bytes, for the PDF (Telerik draws JPEG without an image decoder). 64 pixels square.</summary>
    public static byte[] CreditMarkJpeg()
    {
        using var stream = typeof(MenuDesigns).Assembly.GetManifestResourceStream("MenuMark.site-mark.jpg")
            ?? throw new InvalidOperationException("The menu credit's site mark is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

/// <summary>
/// Every design and every font file, for an app that draws menus itself (10/13/2026: the iPhone app).
/// </summary>
/// <remarks>Sent rather than copied into the app, so a design added here reaches the app with no update.</remarks>
public sealed record MenuDesignCatalog(IReadOnlyList<MenuDesign> Designs, IReadOnlyList<MenuFonts.FontFile> Fonts);
