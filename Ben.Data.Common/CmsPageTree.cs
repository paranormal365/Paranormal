namespace Ben.Data.Common;

/// <summary>
/// Where a group's CMS pages may sit in its menu: the rule both the server and the editor's tree use.
/// </summary>
/// <remarks>
/// <para><b>Three levels and no deeper</b> (Ben, 10/09/2026): a top-level page, a page under it, and
/// a page under that. A visitor's menu shows the top level as pills, and the pages under the page being
/// read as a row beneath it, one row per level (OrgPublicNav); a fourth level would have nowhere to go.</para>
///
/// <para>One rule in one place, because the editor refuses the same drops. If the two
/// disagreed, somebody could drag a page somewhere the server then refused, and the tree would
/// snap back with no reason given.</para>
/// </remarks>
public static class CmsPageTree
{
    /// <summary>The deepest a page may sit: 0 is the top level, so 2 is two levels under it.</summary>
    public const int MaxDepth = 2;

    /// <summary>
    /// Why <paramref name="pageId"/> cannot go under <paramref name="newParentId"/>, or null when it can.
    /// </summary>
    /// <param name="pageId">The page being placed. A page not yet created can pass any id not in the map.</param>
    /// <param name="newParentId">Where it is going; null for the top level.</param>
    /// <param name="parentOf">Every page in the group (drafts excluded) and its current parent.</param>
    public static string? WhyNotPlace(Guid pageId, Guid? newParentId, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        if (newParentId is not Guid parent)
            return HeightOf(pageId, parentOf) > MaxDepth ? TooDeep : null;

        if (parent == pageId) return "A page cannot sit under itself.";
        if (!parentOf.ContainsKey(parent)) return "That page is not one of this group's pages.";

        // Walking up from the new parent: meeting the page itself means it would sit under its own child.
        var depth = 0;
        for (Guid? at = parent; at is Guid step; at = parentOf.GetValueOrDefault(step))
        {
            if (step == pageId) return "A page cannot sit under one of its own pages.";
            if (++depth > parentOf.Count) break;   // a loop already in the data; stop rather than spin
        }

        // depth = how many levels the new parent and its ancestors take up; the page is one more,
        // and everything already under it moves down with it.
        return depth + HeightOf(pageId, parentOf) > MaxDepth ? TooDeep : null;
    }

    /// <summary>How deep a page sits: 0 at the top level.</summary>
    public static int DepthOf(Guid pageId, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var depth = 0;
        for (var at = parentOf.GetValueOrDefault(pageId); at is Guid step && depth <= parentOf.Count; at = parentOf.GetValueOrDefault(step))
            depth++;
        return depth;
    }

    /// <summary>How many levels hang under a page: 0 for a page with nothing under it.</summary>
    public static int HeightOf(Guid pageId, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var children = parentOf.Where(p => p.Value == pageId).Select(p => p.Key).ToList();
        var height = 0;
        for (var level = 1; children.Count > 0 && level <= parentOf.Count; level++)
        {
            height = level;
            var next = children.ToHashSet();
            children = [.. parentOf.Where(p => p.Value is Guid v && next.Contains(v)).Select(p => p.Key)];
        }
        return height;
    }

    /// <summary>The sentence for a placement that would make the menu too deep.</summary>
    public const string TooDeep =
        "Menus go three levels deep at most: a top-level page, a page under it, and a page under that.";
}
