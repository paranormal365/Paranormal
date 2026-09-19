using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Core.Services;

/// <summary>
/// Narrowing the asset gallery to what somebody typed and picked.
/// </summary>
/// <remarks>
/// A gallery of a few hundred shapes, callouts and stickers is only usable through its search box,
/// so what the box does — which fields it looks at, and how it combines with the type picker — is
/// the whole feature rather than a detail of it. It sat inline in the component, where the only
/// way to check that a tag search found anything was to type into a browser
/// (2026-09-05 audit, callouts-4 and phase 11).
/// </remarks>
public static class AssetFilter
{
    /// <summary>
    /// Returns the assets matching <paramref name="search"/> and <paramref name="type"/>.
    /// </summary>
    /// <param name="assets">Everything the catalogue holds.</param>
    /// <param name="search">
    /// Free text, matched against the name, the category and each tag. Empty or blank matches
    /// everything, so clearing the box restores the gallery rather than emptying it.
    /// </param>
    /// <param name="type">The chosen type, or null for every type.</param>
    /// <remarks>
    /// The two narrow together: a type with a search shows only assets of that type that also
    /// match the text. Tags are searched because that is where an asset's useful words live —
    /// "arrow" is a tag on half the callouts and the name of almost none of them.
    /// </remarks>
    public static IReadOnlyList<VideoAssetCatalogItem> Apply(
        IEnumerable<VideoAssetCatalogItem>? assets,
        string? search,
        VideoAssetType? type)
    {
        if (assets is null) return [];

        var results = assets.AsEnumerable();

        if (type.HasValue)
            results = results.Where(a => a.Type == type.Value);

        var query = search?.Trim();

        if (!string.IsNullOrWhiteSpace(query))
            results = results.Where(a => Matches(a, query));

        return results.ToList();
    }

    private static bool Matches(VideoAssetCatalogItem asset, string query) =>
        asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (asset.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || asset.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
}
