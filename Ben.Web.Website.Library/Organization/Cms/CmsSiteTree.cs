using Ben.Data.Common;

namespace Ben.Web.Website.Library.Organization.Cms;

/// <summary>
/// A group's CMS pages as a tree: where a dragged page lands, the moves the arrow buttons make, and the
/// menu order flattened for lists. Pure, so it is tested without a browser.
/// </summary>
/// <remarks>
/// <para>Ben, 10/09/2026: a page's place in the menu is chosen on a tree, by dragging, not by typing a sort
/// number. The same answers drive the page dialog's tree, the page editor's tree and the Ordering window,
/// and the server renumbers the same way (OrgCmsPageController.Move), so what the tree shows after a drop is
/// what gets stored.</para>
///
/// <para>Depth is <see cref="CmsPageTree"/>'s rule, shared with the server.</para>
/// </remarks>
public static class CmsSiteTree
{
    /// <summary>A page and where it sits.</summary>
    public sealed record Page(Guid Id, Guid? ParentPageId, int SortOrder, string Title);

    /// <summary>Where a page goes: under a page (null for the top level), at a position from 0.</summary>
    public sealed record Placement(Guid? ParentPageId, int Index);

    /// <summary>Where a dragged item was let go, relative to the item under it.</summary>
    public enum DropPlace { Before, After, Over }

    /// <summary>The arrow buttons beside a tree.</summary>
    public enum Nudge { Up, Down, Out, In }

    /// <summary>The pages under <paramref name="parentId"/>, in order, leaving out <paramref name="except"/>.</summary>
    public static List<Page> Siblings(IReadOnlyList<Page> pages, Guid? parentId, Guid? except = null)
        => [.. pages.Where(p => p.ParentPageId == parentId && p.Id != except)
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Where a page sits now.</summary>
    public static Placement PlacementOf(IReadOnlyList<Page> pages, Guid pageId)
    {
        var page = pages.First(p => p.Id == pageId);
        return new(page.ParentPageId, Siblings(pages, page.ParentPageId).FindIndex(p => p.Id == pageId));
    }

    /// <summary>Where a page dropped on <paramref name="destination"/> goes.</summary>
    /// <remarks>Over a page puts it under that page, last; before or after puts it beside.</remarks>
    public static Placement WhereDropped(IReadOnlyList<Page> pages, Guid dragged, Guid destination, DropPlace place)
    {
        if (place == DropPlace.Over)
            return new(destination, Siblings(pages, destination, except: dragged).Count);

        var target = pages.First(p => p.Id == destination);
        var row = Siblings(pages, target.ParentPageId, except: dragged);
        var at = row.FindIndex(p => p.Id == destination);
        return new(target.ParentPageId, place == DropPlace.Before ? at : at + 1);
    }

    /// <summary>Why a page cannot go under <paramref name="parentId"/>, or null when it can.</summary>
    public static string? WhyNot(IReadOnlyList<Page> pages, Guid pageId, Guid? parentId)
        => CmsPageTree.WhyNotPlace(pageId, parentId, pages.ToDictionary(p => p.Id, p => p.ParentPageId));

    /// <summary>
    /// The pages with one moved, renumbered 1, 2, 3… in both the row it left and the row it joined — the
    /// same renumbering the server does, so the tree can show the result before the answer comes back.
    /// </summary>
    public static List<Page> Apply(IReadOnlyList<Page> pages, Guid pageId, Placement to)
    {
        var moving = pages.First(p => p.Id == pageId);
        var result = pages.Where(p => p.Id != pageId).ToDictionary(p => p.Id);

        var left = Siblings(pages, moving.ParentPageId, except: pageId);
        for (var i = 0; i < left.Count; i++) result[left[i].Id] = left[i] with { SortOrder = i + 1 };

        var joined = Siblings([.. result.Values], to.ParentPageId);
        joined.Insert(Math.Clamp(to.Index, 0, joined.Count), moving with { ParentPageId = to.ParentPageId });
        for (var i = 0; i < joined.Count; i++) result[joined[i].Id] = joined[i] with { SortOrder = i + 1 };

        return [.. result.Values];
    }

    /// <summary>Where an arrow button would put a page, or null when it can't go that way.</summary>
    /// <remarks>
    /// Up and Down swap with the page beside it. Out moves it up a level, just after the page it was
    /// under. In puts it under the page just above it, last. A move the depth rule refuses is null too.
    /// </remarks>
    public static Placement? Nudged(IReadOnlyList<Page> pages, Guid pageId, Nudge nudge)
    {
        var now = PlacementOf(pages, pageId);
        var row = Siblings(pages, now.ParentPageId);
        Placement? to = nudge switch
        {
            Nudge.Up => now.Index > 0 ? now with { Index = now.Index - 1 } : null,
            Nudge.Down => now.Index < row.Count - 1 ? now with { Index = now.Index + 1 } : null,
            Nudge.Out => now.ParentPageId is Guid parent
                ? new Placement(pages.First(p => p.Id == parent).ParentPageId, PlacementOf(pages, parent).Index + 1)
                : null,
            Nudge.In => now.Index > 0
                ? new Placement(row[now.Index - 1].Id, Siblings(pages, row[now.Index - 1].Id).Count)
                : null,
            _ => null,
        };
        return to is not null && WhyNot(pages, pageId, to.ParentPageId) is null ? to : null;
    }

    /// <summary>The pages in menu order, each with how deep it sits — a parent, then what is under it.</summary>
    public static List<(Page Page, int Depth)> InMenuOrder(IReadOnlyList<Page> pages)
    {
        var known = pages.Select(p => p.Id).ToHashSet();
        var list = new List<(Page, int)>();
        void Walk(Guid? parent, int depth)
        {
            // A page whose parent is missing (a draft's, or one deleted elsewhere) is shown at the top level
            // rather than lost.
            var row = parent is null
                ? [.. pages.Where(p => p.ParentPageId is null || !known.Contains(p.ParentPageId.Value))
                           .OrderBy(p => p.SortOrder).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)]
                : Siblings(pages, parent);
            foreach (var page in row)
            {
                if (list.Count > pages.Count) return;   // a loop in the data; never spin
                list.Add((page, depth));
                Walk(page.Id, depth + 1);
            }
        }
        Walk(null, 0);
        return list;
    }

    /// <summary>
    /// A page's sections after one is dropped, or null when the drop is refused: sections stay on their own
    /// page, so a drop is allowed only beside another of the same page's sections, or onto the page itself
    /// (which puts it first).
    /// </summary>
    public static List<Guid>? SectionsAfterDrop(IReadOnlyList<Guid> ordered, Guid dragged, Guid destination, DropPlace place, bool destinationIsTheOwnPage)
    {
        if (!ordered.Contains(dragged)) return null;
        var rest = ordered.Where(id => id != dragged).ToList();
        if (destinationIsTheOwnPage)
        {
            rest.Insert(0, dragged);
            return rest;
        }
        var at = rest.IndexOf(destination);
        if (at < 0 || place == DropPlace.Over) return null;
        rest.Insert(place == DropPlace.Before ? at : at + 1, dragged);
        return rest;
    }
}
