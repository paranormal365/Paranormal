namespace Ben.Data.Common.Enums;

/// <summary>
/// Represents the set of CRUD operations permitted on an <c>OrganizationAccessGrant</c> row.
/// </summary>
/// <remarks>
/// Stored as a single <c>int</c> bitmask column.  Each grant row holds all permitted
/// actions for a (user, organization, table) combination.
/// </remarks>
[Flags]
public enum OrganizationSecurityAction
{
    None = 0,

    /// <summary>Permission to create new records in the target table.</summary>
    Create = 1,

    /// <summary>Permission to read or list records in the target table.</summary>
    Read = 2,

    /// <summary>Permission to modify existing records in the target table.</summary>
    Update = 4,

    /// <summary>Permission to delete records from the target table.</summary>
    Delete = 8,

    All = Create | Read | Update | Delete
}