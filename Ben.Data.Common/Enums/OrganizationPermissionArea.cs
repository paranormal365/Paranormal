namespace Ben.Data.Common.Enums;

/// <summary>
/// The areas a subscription tier can include for custom-role permissions (item 156, decision D1).
/// </summary>
/// <remarks>
/// <para>A tier carries a checklist of these; the role editor grays out permission sections whose
/// area the group's tier does not include, and (from Phase D) role grants in an excluded area
/// stop applying at runtime — grayed-but-remembered, resuming on upgrade (D4).</para>
///
/// <para><b>Numbered explicitly and never renumbered</b>, for the same reason as
/// <see cref="SubscriptionLimit"/>: these end up in rows that outlive the deployment that wrote
/// them, and a reordered enum silently turns an Equipment entitlement into a Cases one.</para>
///
/// <para>Every organization-scoped <see cref="OrganizationSecurityTable"/> value maps to exactly
/// one area — <c>PermissionAreas.AreaFor</c> is the map, and a guard test keeps it total.</para>
/// </remarks>
public enum OrganizationPermissionArea
{
    /// <summary>The group's own profile: settings, addresses, contact types, member groups.</summary>
    OrganizationProfile = 1,

    /// <summary>Membership requests and who gets in.</summary>
    Membership = 2,

    /// <summary>Cases: the case record and everything that hangs off it.</summary>
    Cases = 3,

    /// <summary>Investigations and their scheduling.</summary>
    Investigations = 4,

    /// <summary>Equipment, catalogues and checkouts.</summary>
    Equipment = 5,

    /// <summary>The public pages a group authors (CMS).</summary>
    PublicPages = 6,

    /// <summary>The group's file collection.</summary>
    Files = 7,

    /// <summary>Client requests and client relations.</summary>
    Clients = 8,

    /// <summary>The group calendar and its events.</summary>
    Calendar = 9,
}
