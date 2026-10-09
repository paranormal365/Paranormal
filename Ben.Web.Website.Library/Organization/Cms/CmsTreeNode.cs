using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Organization.Cms;

/// <summary>
/// One row of a CMS tree (the page placer, the Ordering window): a page, or a section under its page.
/// </summary>
/// <remarks>
/// Property names are Telerik's TreeView defaults (Id, ParentId, Text, HasChildren), so the flat binding
/// needs no field mapping. A section's ParentId is its page's id; page and section ids are both GUIDs, so the
/// two kinds share one id space without colliding.
/// </remarks>
public sealed class CmsTreeNode
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Text { get; init; } = "";
    public bool HasChildren { get; set; }

    /// <summary>A section row rather than a page row.</summary>
    public bool IsSection { get; init; }
    public CmsSectionType? SectionType { get; init; }

    /// <summary>The page being placed — the only one the page placer lets you drag.</summary>
    public bool IsCurrent { get; init; }

    public bool IsPublished { get; init; }
    public bool IsPublic { get; init; } = true;

    /// <summary>A section shown to visitors. False shows "Hidden" beside it.</summary>
    public bool IsActive { get; init; } = true;
    public int Depth { get; init; }

    // Telerik compares tree items by reference for expansion and selection, but rebuilds of the list make new
    // objects; equal ids are the same row.
    public override bool Equals(object? obj) => obj is CmsTreeNode other && other.Id == Id;
    public override int GetHashCode() => Id.GetHashCode();
}
