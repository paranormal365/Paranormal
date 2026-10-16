using Microsoft.AspNetCore.Components;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// The address of a spot on the page being shown: <c>/o/apple-beta/events/app-review-night#event-act</c>,
/// never a bare <c>#event-act</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a bare fragment is wrong here.</b> The site has <c>&lt;base href="/"&gt;</c>, and a
/// browser resolves a bare fragment against the base, not against the page. On a hosted event's
/// public page "Ask for a place" was <c>href="#event-act"</c>, which means
/// <c>https://ishaunted.com/#event-act</c>: the home page. Blazor saw a different path and went
/// there, so a visitor who pressed it on a phone landed on the home page instead of the request
/// form (production, 10/16/2026). Every in-page link on the site had the same fault; the ones on
/// the home page only worked because the home page's path is <c>/</c>.</para>
///
/// <para><b>Why this works.</b> Blazor treats a link whose path AND query match the page's own,
/// with a fragment, as "this page, a different spot": it scrolls to the element and navigates
/// nowhere. So the page's own path and query go in front of the fragment. Root-relative, like
/// every other address on this site, so nothing depends on <c>&lt;base&gt;</c> to find its way —
/// the same rule App.razor's assets follow after the Safari base-href fix.</para>
///
/// <para>Used in a plain <c>&lt;a&gt;</c> rather than wrapped in a component, so a page's scoped
/// CSS still reaches the link (the phone's sticky "Ask for a place" is styled that way), and so a
/// component that takes an href as a string — a fact rail's tile, a stat card — can be handed one.
/// <c>SamePageLinkGuardTests</c> fails the build on a bare <c>href="#something"</c>.</para>
/// </remarks>
public static class JumpLink
{
    /// <summary>The page being shown, at the element with this id.</summary>
    public static string To(NavigationManager nav, string elementId) => To(nav.Uri, elementId);

    /// <summary>
    /// <c>/path?query#id</c> for an absolute address and an element id. The page's own query is
    /// kept — Blazor counts a different query as a different page — and any fragment the address
    /// already carried is replaced.
    /// </summary>
    public static string To(string absoluteUri, string elementId)
    {
        var id = elementId.TrimStart('#');
        var page = Uri.TryCreate(absoluteUri, UriKind.Absolute, out var uri) ? uri.PathAndQuery : "/";
        return $"{page}#{id}";
    }
}
