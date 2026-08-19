namespace Ben.Data.Common;

/// <summary>
/// Words an organization cannot use as a page address, because the site already routes them.
/// </summary>
/// <remarks>
/// <para><b>The bug this closes.</b> A CMS page saved with the slug <c>cases</c> succeeded and was
/// then permanently unreachable: <c>/o/{org}/cases</c> matches the case-list route first, and
/// nothing anywhere told the person who made it. A page that saves and cannot be opened is worse
/// than one that refuses to save, because only the second kind gets fixed.</para>
///
/// <para><b>Every prefix added to the site steals a word from this namespace.</b> That is the cost
/// of putting CMS pages at the root of <c>/o/{org}/</c> rather than under a prefix of their own, and
/// it is worth paying — <c>/o/ghost-squad/about</c> is the address somebody would guess. But the
/// cost has to be paid deliberately, which means this list and the routes cannot drift apart. A test
/// scans the routes and fails if one appears here that is missing, so adding a route without adding
/// the word is caught rather than discovered by an organization months later.</para>
///
/// <para>A few extras are held back for names the site will want and does not yet have. Reserving
/// one costs an organization nothing today; taking it back after somebody has built a page there
/// means breaking their link.</para>
/// </remarks>
public static class CmsReservedSlugs
{
    /// <summary>
    /// Words routed by the site itself, or held for routes it will want.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Routed today.
        "cases",
        "events",

        // Held back. Investigations and places are already modelled and will want public routes;
        // the rest are the words a site of this shape always ends up needing.
        "investigations",
        "places",
        "equipment",
        "team",
        "members",
        "search",
        "feed",
        "rss",
        "sitemap",
        "robots",
        "api",
        "admin",
        "login",
        "logout",
        "assets",
        "static",
    };

    /// <summary>Whether this slug is one the site has already claimed.</summary>
    public static bool IsReserved(string? slug)
        => !string.IsNullOrWhiteSpace(slug) && All.Contains(slug.Trim());

    /// <summary>
    /// Why a slug cannot be used, written to be shown to the person who typed it, or null when it
    /// is fine.
    /// </summary>
    public static string? RefusalFor(string? slug)
        => IsReserved(slug)
            ? $"\"{slug!.Trim()}\" is used by the site itself, so a page there could never be opened. "
            + "Try something more specific — \"our-cases\" or \"upcoming-events\", for instance."
            : null;
}
