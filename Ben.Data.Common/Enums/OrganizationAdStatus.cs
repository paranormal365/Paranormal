namespace Ben.Data.Common.Enums;

/// <summary>
/// The life of a group's promotional ad (item 166 W3).
/// </summary>
/// <remarks>
/// The one invariant every consumer leans on: <b>the public site renders only
/// <see cref="Approved"/></b>. A draft belongs to its group, a submission sits in the
/// SuperAdmin queue, a rejection carries its reason back — and none of the three ever
/// reaches an anonymous visitor. Append-only; never renumber.
/// </remarks>
public enum OrganizationAdStatus
{
    /// <summary>Being written; visible only to the group's administrators.</summary>
    Draft = 0,
    /// <summary>Sent for review; frozen while the queue holds it.</summary>
    Submitted = 1,
    /// <summary>Reviewed and live in the public placements.</summary>
    Approved = 2,
    /// <summary>Reviewed and declined, with the reason recorded; editable back into Draft.</summary>
    Rejected = 3,
}
