namespace Ben.Data.Common.Enums;

/// <summary>
/// Lifecycle status of a client investigation request.
/// </summary>
public enum ClientRequestStatus
{
    /// <summary>Saved but not yet submitted to any organization.</summary>
    Draft = 0,

    /// <summary>Submitted and pending review by one or more organizations.</summary>
    Submitted = 1,

    /// <summary>Accepted and assigned to an organization — becomes a Case.</summary>
    Assigned = 2,

    /// <summary>Closed by the client or by mutual agreement.</summary>
    Closed = 3,

    /// <summary>Withdrawn by the client before assignment.</summary>
    Withdrawn = 4,

    /// <summary>Every organization the request was sent to has declined it.</summary>
    Declined = 5,
}
