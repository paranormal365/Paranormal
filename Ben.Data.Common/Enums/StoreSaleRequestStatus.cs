namespace Ben.Data.Common.Enums;

/// <summary>Where a seller's request to put an item on sale stands (store sellers, backlog 251, P3).</summary>
public enum StoreSaleRequestStatus
{
    /// <summary>Waiting for the store. At most one per item.</summary>
    Open = 0,

    /// <summary>The store priced the item and put it on sale.</summary>
    Approved = 1,

    /// <summary>The store said no, with a note saying why.</summary>
    Declined = 2,

    /// <summary>The seller took the request back.</summary>
    Withdrawn = 3,
}
