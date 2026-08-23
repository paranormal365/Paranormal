namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// One pickable thing, source-agnostic (item 175). The picker renders these; where they come
/// from is the caller's business — a person's shareable uploads today, a group's library or a
/// case's media tomorrow.
/// </summary>
/// <param name="Id">What the caller gets back on selection.</param>
/// <param name="Name">The primary line: a file name, a page title.</param>
/// <param name="TypeLabel">Short kind label ("Image", "Audio", "PDF") — the type filter's values.</param>
/// <param name="MetaLine">Secondary line: owner, size, date — whatever helps someone choose.</param>
/// <param name="Facets">
/// Named context filters, e.g. "Source" → "Shared with this group", "Investigation" →
/// "Oct 12 visit", "Location" → "The Hermitage". The picker builds one dropdown per facet
/// name present — Ben's "filtered by type and investigation or location", made generic so
/// each surface filters by what its content actually has.
/// </param>
/// <param name="ContentType">MIME type when the item is a media file, for thumbnail rendering.</param>
/// <param name="FileSize">Size when the item is a file.</param>
public sealed record ContentPickerItem(
    Guid Id,
    string Name,
    string TypeLabel,
    string? MetaLine = null,
    IReadOnlyDictionary<string, string>? Facets = null,
    string? ContentType = null,
    long? FileSize = null);

/// <summary>
/// The picker's filtering, held apart from rendering so xUnit can pin it (the WizardModel
/// pattern): search + type + facet choices in, the visible subset out.
/// </summary>
public sealed class ContentPickerModel
{
    private readonly List<ContentPickerItem> _items = [];

    public string Search { get; set; } = "";
    public string? TypeFilter { get; set; }
    public Dictionary<string, string> FacetFilters { get; } = [];

    public IReadOnlyList<ContentPickerItem> Items => _items;

    public void SetItems(IEnumerable<ContentPickerItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        // Filters referring to values that no longer exist would silently show nothing.
        if (TypeFilter is { } t && !_items.Any(i => i.TypeLabel == t)) TypeFilter = null;
        foreach (var key in FacetFilters.Keys.Where(k => !FacetNames.Contains(k)).ToList())
            FacetFilters.Remove(key);
    }

    /// <summary>Distinct type labels, for the type dropdown.</summary>
    public IReadOnlyList<string> TypeLabels =>
        [.. _items.Select(i => i.TypeLabel).Distinct().OrderBy(t => t)];

    /// <summary>Facet names any item carries, one dropdown each.</summary>
    public IReadOnlyList<string> FacetNames =>
        [.. _items.Where(i => i.Facets is not null)
                  .SelectMany(i => i.Facets!.Keys).Distinct().OrderBy(n => n)];

    /// <summary>Distinct values for one facet, for its dropdown.</summary>
    public IReadOnlyList<string> FacetValues(string facetName) =>
        [.. _items.Where(i => i.Facets is not null && i.Facets.ContainsKey(facetName))
                  .Select(i => i.Facets![facetName]).Distinct().OrderBy(v => v)];

    /// <summary>What the picker shows: every filter must agree. Search matches name, type and
    /// meta, case-insensitively — a person searching remembers SOMETHING, not which field.</summary>
    public IReadOnlyList<ContentPickerItem> Visible()
    {
        IEnumerable<ContentPickerItem> result = _items;

        if (TypeFilter is { Length: > 0 } type)
            result = result.Where(i => i.TypeLabel == type);

        foreach (var (facet, value) in FacetFilters)
            if (!string.IsNullOrEmpty(value))
                result = result.Where(i => i.Facets is not null
                    && i.Facets.TryGetValue(facet, out var v) && v == value);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var needle = Search.Trim();
            result = result.Where(i =>
                i.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || i.TypeLabel.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (i.MetaLine?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return [.. result];
    }
}
