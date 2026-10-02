namespace Ben.Web.Website.Library.Kit;

/// <summary>One entry in a page's own rail menu.</summary>
/// <param name="Group">A heading this entry sits under; entries sharing one are drawn together.</param>
/// <param name="TourFor">The tour selector this entry stands in for — "#tab-members" — so a
/// walkthrough written against the page's tabs still finds its target when the rail has replaced
/// them (BenTour.razor.js).</param>
public sealed record RailMenuItem(
    string Text,
    string Icon,
    string Href,
    bool Active = false,
    string? Group = null,
    bool External = false,
    string? TourFor = null);

/// <summary>
/// The menu a page puts in the rail in place of its section's: a group's own pages, an event's own
/// screens — with the way back to the menu it came from at the top.
/// </summary>
/// <param name="Depth">How far in this menu is. The deepest one registered is the one shown, so an
/// event's menu wins over its group's while both are known.</param>
public sealed record RailMenuModel(
    string Title,
    IReadOnlyList<RailMenuItem> Items,
    string? BackText = null,
    string? BackHref = null,
    string? Kicker = null,
    int Depth = 1);

/// <summary>
/// Which page-supplied menu the rail is showing, if any (Signal, 2026-10-01).
/// </summary>
/// <remarks>
/// <para>Ben: "Instead of tabs for an organization… the left-side menu would be a good place and
/// when you navigate to the sub-page, the menu becomes the menu for that page with a back button at
/// the top." The rail lives in the layout (BenNav) and knows the site's sections; it does not know
/// what a particular group lets this person open. The page side does — so the page side supplies
/// the menu here, and the rail draws it. Gates stay with the code that already owns them.</para>
/// <para>Scoped per circuit. Owners register and clear their own entry, so a menu cannot outlive
/// the context that made it.</para>
/// </remarks>
public sealed class RailMenuState
{
    private readonly Dictionary<object, RailMenuModel> _menus = new(ReferenceEqualityComparer.Instance);

    public event Action? Changed;

    /// <summary>The deepest menu registered, or null to show the section's own.</summary>
    public RailMenuModel? Current => _menus.Count == 0 ? null : _menus.Values.MaxBy(m => m.Depth);

    public void Set(object owner, RailMenuModel menu)
    {
        if (_menus.TryGetValue(owner, out var existing) && existing == menu) return;
        _menus[owner] = menu;
        Changed?.Invoke();
    }

    public void Clear(object owner)
    {
        if (_menus.Remove(owner)) Changed?.Invoke();
    }
}
