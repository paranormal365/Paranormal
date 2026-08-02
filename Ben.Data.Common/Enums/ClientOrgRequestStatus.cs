namespace Ben.Data.Common.Enums;

/// <summary>
/// Status of a client's application to a specific organization within a request.
/// </summary>
public enum ClientOrgRequestStatus
{
    /// <summary>Application sent — org has not yet opened it.</summary>
    Pending = 0,

    /// <summary>Organization accepted the request (becomes an active Case).</summary>
    Accepted = 1,

    /// <summary>Organization declined — another org may still accept.</summary>
    Rejected = 2,

    /// <summary>Client withdrew the application, or it was superseded when another org accepted.</summary>
    Cancelled = 3,

    /// <summary>An org member has opened and viewed the full request details.</summary>
    Viewed = 4,

    /// <summary>Org is actively considering the request before deciding.</summary>
    UnderReview = 5,
}
