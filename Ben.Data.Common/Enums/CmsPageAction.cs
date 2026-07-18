namespace Ben.Data.Common.Enums;

/// <summary>
/// Bitmask of per-page CMS actions that can be granted to individual
/// org members or member groups via <c>CmsPagePermission</c>.
/// </summary>
/// <remarks>
/// These flags are intentionally separate from <see cref="OrganizationSecurityAction"/>
/// to allow fine-grained page-level access that is distinct from org-level table grants.
/// </remarks>
[Flags]
public enum CmsPageAction
{
    None   = 0,

    /// <summary>Permission to view this page (even when restricted to specific members).</summary>
    View   = 1,

    /// <summary>Permission to edit this page's content and sections.</summary>
    Edit   = 2,

    /// <summary>Permission to delete this page.</summary>
    Delete = 4
}
