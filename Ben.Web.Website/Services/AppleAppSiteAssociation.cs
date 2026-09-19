using System.Text.Json.Serialization;

namespace Ben.Web.Website.Services;

/// <summary>
/// The document iOS fetches to decide whether a link to this site should open the app (item 209).
/// </summary>
/// <remarks>
/// <para><b>What a universal link actually is.</b> When somebody taps an <c>ishaunted.com</c> link
/// in Mail or Messages on a phone that has the app, iOS opens the app instead of Safari — but only
/// for paths this document claims, and only if the app carries a matching associated-domains
/// entitlement. On a phone without the app, nothing changes: the link opens the website, which is
/// the correct outcome and needs no fallback of its own.</para>
///
/// <para><b>Claiming a path the app cannot render is worse than claiming nothing.</b> The link
/// leaves Safari — where the real page exists — opens the app, and lands the person on whatever
/// the app can make of it. That is the whole risk in this feature, and it is why the list below is
/// short and why every omission is written down. Two of them are paths the app's own
/// <c>DeepLinkParser</c> parses perfectly well and its router then sends to a "Coming soon"
/// placeholder.</para>
///
/// <para><b>No wildcards that need an exclusion.</b> Apple's component matching has ordering rules
/// that are easy to get subtly wrong, and getting them wrong here fails silently on a stranger's
/// phone. So nothing is claimed broadly and then carved back: <c>/events</c> is claimed exactly and
/// <c>/events/*</c> simply is not claimed, rather than claiming <c>/events/*</c> and excluding the
/// detail route. The document has no <c>exclude</c> entries at all, and therefore no order
/// dependence.</para>
///
/// <para><b>Served from an endpoint, not from wwwroot.</b> The file has no extension, so static
/// file middleware has no content type for it, and iOS requires <c>application/json</c> over HTTPS
/// with no redirect. An endpoint gets all three right and lets the app identifier come from
/// configuration rather than from a literal in a JSON blob nothing validates.</para>
/// </remarks>
public static class AppleAppSiteAssociation
{
    /// <summary>
    /// Paths that open the app, each because it reaches a real screen.
    /// </summary>
    /// <remarks>
    /// Every entry here corresponds to a case in the app's <c>DeepLinkParser</c> that its
    /// <c>Router</c> sends to a view in <c>RootShell.destination</c> — not to the <c>default:</c>
    /// placeholder. <c>AppleAppSiteAssociationTests</c> pins the list; the Swift
    /// <c>DeepLinkParserTests</c> pins the other half.
    /// </remarks>
    public static readonly string[] ClaimedPaths =
    [
        // The feed, and everything hanging off it. /feed/{anything} falls back to the feed itself
        // in the parser, so a malformed one still lands somewhere real.
        "/feed",
        "/feed/*",

        // The events LIST only. /events/{id} is deliberately absent — see UnclaimedPaths.
        "/events",

        // A client's own cases. /my-cases/{id} reaches CaseDetailView.
        "/my-cases",
        "/my-cases/*",

        "/my-investigations",
        "/notifications",
        "/profile",

        // The emailed confirmation link. Opening it in the app is strictly better than in Safari:
        // the app confirms and the person is already signed in where they wanted to be.
        "/validate-email/*",
    ];

    /// <summary>
    /// Paths deliberately left to the website, and why. Read this before adding one.
    /// </summary>
    /// <remarks>
    /// <para>Kept as data rather than as prose in a comment so a test can assert each one is
    /// absent from the served document. An accidental <c>/events/*</c> would be invisible on every
    /// machine except a real phone with the app installed.</para>
    /// </remarks>
    public static readonly (string Path, string Why)[] UnclaimedPaths =
    [
        ("/events/*",
         "The parser returns .eventDetail, but no case in RootShell.destination renders it, so the "
       + "router's default arm shows a \"Coming soon\" placeholder. The website has the real page."),

        ("/organizations/*",
         "Only /organizations/{orgId}/cases/{caseId} parses at all, and it too falls through to the "
       + "\"Coming soon\" placeholder. Claiming the prefix would also capture every public group "
       + "page, none of which the app has a screen for."),

        ("/attending/*",
         "The parser reads the RSVP token and the router then throws it away and opens the events "
       + "list — its own comment says the flow stays on the website until this file exists. "
       + "Claiming it would silently lose somebody's RSVP. Claim it when the screen exists."),

        ("/s/*",
         "Share links (item 207) are for people with no account, who by definition are the least "
       + "likely to have the app. The shared player is a website page and has no app equivalent."),

        ("/o/*",
         "Public group pages. No app screen."),

        ("/admin/*",
         "SuperAdmin screens are website-only, and always will be."),
    ];

    /// <summary>Builds the document for one app identifier.</summary>
    /// <param name="appId">Team ID and bundle identifier, as <c>TEAMID.bundle.id</c>.</param>
    public static AppLinksDocument For(string appId) =>
        new(new AppLinks([new AppLinkDetail([appId],
            [.. ClaimedPaths.Select(p => new AppLinkComponent(p))])]));

    // ── the document's shape, as Apple defines it ─────────────────────────────

    /// <summary>The root object. Only <c>applinks</c> — no webcredentials service yet.</summary>
    /// <remarks>
    /// Password autofill (<c>webcredentials</c>) needs its own entitlement and is a separate
    /// decision; adding the key here without the entitlement would claim a capability the app does
    /// not have.
    /// </remarks>
    public sealed record AppLinksDocument(
        [property: JsonPropertyName("applinks")] AppLinks AppLinks);

    public sealed record AppLinks(
        [property: JsonPropertyName("details")] IReadOnlyList<AppLinkDetail> Details);

    /// <summary>
    /// One app, and what it claims.
    /// </summary>
    /// <remarks>
    /// <c>appIDs</c> is the modern spelling; <c>appID</c> singular is the pre-iOS-13 one. Only the
    /// modern form is emitted — the app's own deployment target is well past that.
    /// </remarks>
    public sealed record AppLinkDetail(
        [property: JsonPropertyName("appIDs")] IReadOnlyList<string> AppIds,
        [property: JsonPropertyName("components")] IReadOnlyList<AppLinkComponent> Components);

    /// <summary>One path pattern. <c>*</c> matches any run of characters, <c>?</c> exactly one.</summary>
    public sealed record AppLinkComponent(
        [property: JsonPropertyName("/")] string Path);
}
