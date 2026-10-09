using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Organization.Cms;

/// <summary>
/// What each kind of CMS section is called, what it does, and a thumbnail sketch of where things go in it.
/// </summary>
/// <remarks>
/// <para>Ben, 10/09/2026: "show thumbnail-sized examples of what each Section looks like and where things
/// go." Choosing from a list of names meant guessing what "Our investigations" would put on the page. Each
/// sketch is drawn from what the public renderer (OrgPublicSection) actually lays out: a text section is
/// lines under a heading, a case section is two cards side by side, a case's photos are a grid.</para>
///
/// <para>The sketches are inline SVG in the page's own colors (currentColor and the theme's primary), so
/// they follow light and dark mode and cost no request.</para>
/// </remarks>
public static class CmsSectionTypes
{
    /// <summary>One kind of section, as the editor offers it.</summary>
    public sealed record Kind(CmsSectionType Type, string Label, string Description, string Sketch, bool Offered = true);

    /// <summary>
    /// The kinds, in the order the picker shows them: what most pages need first.
    /// </summary>
    /// <remarks>
    /// Every kind is offered. Contact details, the file gallery and the member roster were held back while the
    /// public page could only draw them as gray placeholders; backlog 256 (10/09/2026) built them.
    /// </remarks>
    public static IReadOnlyList<Kind> All { get; } =
    [
        new(CmsSectionType.RichText, "Text",
            "Words, headings, lists and pictures, written in the editor. Ready-made blocks give you a start.",
            Heading + Lines(19, 6)),
        new(CmsSectionType.ImageBanner, "Image or banner",
            "One wide picture from your library, with words for screen readers and an optional link.",
            Heading + Photo(8, 18, 104, 50)),
        new(CmsSectionType.ContactInfo, "Contact details",
            "Your group's public emails, phone numbers, websites and addresses, kept up to date for you.",
            Heading + ContactRow(20) + ContactRow(33) + ContactRow(46) + ContactRow(59)),
        new(CmsSectionType.FileGallery, "File gallery",
            "Public pictures, videos and recordings from your files, as a slideshow, plus files to download.",
            Heading + Photo(8, 18, 66, 38) + Photo(77, 18, 35, 18) + Photo(77, 38, 35, 18)
                    + """<rect class="cms-sketch__s" x="8" y="60" width="104" height="8" rx="2"/>"""),
        new(CmsSectionType.MemberRoster, "Our members",
            "The members who agreed to be listed, with their photos and titles.",
            Heading + Person(22) + Person(48) + Person(74) + Person(100)),
        new(CmsSectionType.EmbeddedCases, "Our cases",
            "Cards for the cases you choose. A client appears only by the alias they chose, never their name.",
            Heading + Card(8, 18) + Card(62, 18)),
        new(CmsSectionType.EmbeddedInvestigations, "Our investigations",
            "Cards for the investigations you choose, with what you found. Exact addresses are never shown.",
            Heading + Card(8, 18, found: true) + Card(62, 18, found: true)),
        new(CmsSectionType.CaseMedia, "Photos from a case",
            "Pictures from a case's public timeline. If an entry is made private later, its picture comes off the page automatically.",
            Heading + Photo(8, 18, 33, 23) + Photo(43.5, 18, 33, 23) + Photo(79, 18, 33, 23)
                    + Photo(8, 45, 33, 23) + Photo(43.5, 45, 33, 23) + Photo(79, 45, 33, 23)),
        new(CmsSectionType.EventBooking, "An event: dates and ask for a place",
            "The card visitors use to ask for a place at one of your published events.",
            Heading + Booking),
        new(CmsSectionType.EventProgramme, "An event's program",
            "An event's sessions by night, with rooms and places left, kept up to date for you.",
            Heading + Session(19) + Session(35) + Session(51)),
        new(CmsSectionType.EventGallery, "An event's pictures",
            "The photo wall from one of your events.",
            Heading + Photo(8, 18, 50, 50) + Photo(61, 18, 24, 23.5) + Photo(88, 18, 24, 23.5)
                    + Photo(61, 44.5, 24, 23.5) + Photo(88, 44.5, 24, 23.5)),
        new(CmsSectionType.EventVenue, "An event's venue",
            "Where the event is held: the place on a map, the building's story and its rooms.",
            Heading + Venue),
        new(CmsSectionType.CustomHtml, "Custom HTML",
            "Your own HTML, for anything the other kinds can't do. Forms, scripts and styles are removed when you save.",
            Heading + """<text x="60" y="52" text-anchor="middle" class="cms-sketch__a" font-size="22" font-family="ui-monospace, monospace" font-weight="700">&lt;/&gt;</text>"""),
    ];

    /// <summary>The kinds a new section can be.</summary>
    public static IReadOnlyList<Kind> Offered { get; } = [.. All.Where(k => k.Offered)];

    /// <summary>What a kind is called in the editor. Unlisted values fall back to the enum name.</summary>
    public static string Label(CmsSectionType type) => All.FirstOrDefault(k => k.Type == type)?.Label ?? type.ToString();

    /// <summary>The sketch for a kind, as a complete SVG element.</summary>
    public static string Svg(CmsSectionType type)
        => $"""<svg class="cms-sketch" viewBox="0 0 120 76" role="img" aria-hidden="true" focusable="false"><rect class="cms-sketch__frame" x="0.5" y="0.5" width="119" height="75" rx="6"/>{All.FirstOrDefault(k => k.Type == type)?.Sketch ?? Lines(10, 6)}</svg>""";

    // ── The parts the sketches are drawn from ───────────────────────────────

    /// <summary>A coordinate, always with a decimal point: a comma from the reader's culture would break the SVG.</summary>
    private static string N(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The section's optional title, across the top.</summary>
    private const string Heading = """<rect class="cms-sketch__h" x="8" y="7" width="48" height="6" rx="2"/>""";

    private static string Lines(double top, int count)
    {
        var widths = new[] { 104, 96, 100, 70, 104, 88, 62 };
        var lines = "";
        for (var i = 0; i < count; i++)
            lines += $"""<rect class="cms-sketch__l" x="8" y="{N(top + i * 8.5)}" width="{N(widths[i % widths.Length])}" height="4" rx="2"/>""";
        return lines;
    }

    private static string Photo(double x, double y, double w, double h)
        => $"""<rect class="cms-sketch__s" x="{N(x)}" y="{N(y)}" width="{N(w)}" height="{N(h)}" rx="3"/>"""
         + $"""<path class="cms-sketch__a" d="M{N(x + w * .12)} {N(y + h * .82)} L{N(x + w * .38)} {N(y + h * .45)} L{N(x + w * .55)} {N(y + h * .66)} L{N(x + w * .68)} {N(y + h * .52)} L{N(x + w * .88)} {N(y + h * .82)} Z"/>"""
         + $"""<circle class="cms-sketch__a" cx="{N(x + w * .74)}" cy="{N(y + h * .26)}" r="{N(Math.Min(w, h) * .09)}"/>""";

    private static string Card(double x, double y, bool found = false)
        => $"""<rect class="cms-sketch__s" x="{N(x)}" y="{N(y)}" width="50" height="50" rx="4"/>"""
         + $"""<rect class="cms-sketch__h" x="{N(x + 5)}" y="{N(y + 6)}" width="32" height="5" rx="2"/>"""
         + $"""<rect class="cms-sketch__l" x="{N(x + 5)}" y="{N(y + 15)}" width="20" height="3" rx="1.5"/>"""
         + $"""<rect class="cms-sketch__l" x="{N(x + 5)}" y="{N(y + 22)}" width="40" height="3" rx="1.5"/>"""
         + $"""<rect class="cms-sketch__l" x="{N(x + 5)}" y="{N(y + 28)}" width="36" height="3" rx="1.5"/>"""
         + (found
             ? $"""<rect class="cms-sketch__a" x="{N(x + 5)}" y="{N(y + 36)}" width="2" height="9"/><rect class="cms-sketch__l" x="{N(x + 10)}" y="{N(y + 37)}" width="30" height="3" rx="1.5"/><rect class="cms-sketch__l" x="{N(x + 10)}" y="{N(y + 42)}" width="24" height="3" rx="1.5"/>"""
             : $"""<rect class="cms-sketch__l" x="{N(x + 5)}" y="{N(y + 34)}" width="30" height="3" rx="1.5"/>""");

    private static string Session(double y)
        => $"""<rect class="cms-sketch__a" x="8" y="{N(y)}" width="20" height="10" rx="5"/>"""
         + $"""<rect class="cms-sketch__l" x="33" y="{N(y + 1)}" width="50" height="3.5" rx="1.75"/>"""
         + $"""<rect class="cms-sketch__l" x="33" y="{N(y + 6.5)}" width="34" height="3" rx="1.5"/>"""
         + $"""<rect class="cms-sketch__s" x="92" y="{N(y + 1)}" width="20" height="8" rx="4"/>""";

    private static string ContactRow(double y)
        => $"""<circle class="cms-sketch__a" cx="13" cy="{N(y + 3)}" r="3.5"/>"""
         + $"""<rect class="cms-sketch__l" x="22" y="{N(y + 1)}" width="{N(y < 40 ? 60 : 78)}" height="4" rx="2"/>""";

    private static string Person(double cx)
        => $"""<circle class="cms-sketch__s" cx="{N(cx)}" cy="34" r="10"/>"""
         + $"""<circle class="cms-sketch__a" cx="{N(cx)}" cy="31" r="4"/>"""
         + $"""<path class="cms-sketch__a" d="M{N(cx - 7)} 42 Q{N(cx)} 34 {N(cx + 7)} 42 Z"/>"""
         + $"""<rect class="cms-sketch__h" x="{N(cx - 9)}" y="49" width="18" height="4" rx="2"/>"""
         + $"""<rect class="cms-sketch__l" x="{N(cx - 7)}" y="56" width="14" height="3" rx="1.5"/>""";

    private const string Booking =
        """<rect class="cms-sketch__s" x="8" y="18" width="104" height="50" rx="4"/>"""
      + """<rect class="cms-sketch__a" x="14" y="24" width="22" height="24" rx="3"/>"""
      + """<rect class="cms-sketch__frame-on-a" x="18" y="34" width="14" height="3" rx="1.5"/>"""
      + """<rect class="cms-sketch__h" x="42" y="25" width="50" height="5" rx="2"/>"""
      + """<rect class="cms-sketch__l" x="42" y="34" width="40" height="3" rx="1.5"/>"""
      + """<rect class="cms-sketch__l" x="42" y="40" width="56" height="3" rx="1.5"/>"""
      + """<rect class="cms-sketch__a" x="72" y="53" width="34" height="10" rx="5"/>""";

    private const string Venue =
        """<rect class="cms-sketch__s" x="8" y="18" width="46" height="50" rx="4"/>"""
      + """<path class="cms-sketch__road" d="M8 40 L54 32 M20 18 L28 68 M8 56 L54 50"/>"""
      + """<path class="cms-sketch__a" d="M31 30 a7 7 0 1 1 0.01 0 Z M31 49 L25.5 34 L36.5 34 Z"/>"""
      + """<rect class="cms-sketch__h" x="60" y="19" width="40" height="5" rx="2"/>"""
      + """<rect class="cms-sketch__l" x="60" y="28" width="52" height="3" rx="1.5"/>"""
      + """<rect class="cms-sketch__l" x="60" y="34" width="46" height="3" rx="1.5"/>"""
      + """<rect class="cms-sketch__s" x="60" y="43" width="52" height="10" rx="2"/>"""
      + """<rect class="cms-sketch__s" x="60" y="57" width="52" height="10" rx="2"/>""";
}
