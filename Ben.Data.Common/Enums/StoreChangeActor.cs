namespace Ben.Data.Common.Enums;

/// <summary>Which side of the store made a change to an item (store sellers, backlog 251, P2).</summary>
/// <remarks>
/// Kept on the row rather than worked out from the person's roles later: a seller who is also an
/// admin, or who loses the Seller role, still made each change in the capacity they made it in.
/// </remarks>
public enum StoreChangeActor
{
    /// <summary>The store's own staff, from the admin pages.</summary>
    Store = 0,

    /// <summary>The item's seller, from the Selling workspace.</summary>
    Seller = 1,
}
