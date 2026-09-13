namespace Ben.Data.Common.Enums;

/// <summary>
/// Where one group's request to hold an event at another group's venue stands (item 235 phase 9).
/// </summary>
/// <remarks>Append only. The numbers are stored.</remarks>
public enum VenueHostingRequestStatus
{
    /// <summary>Asked, and the venue has not answered.</summary>
    Pending = 0,

    /// <summary>The venue said yes, and a grant now exists for those dates.</summary>
    Approved = 1,

    /// <summary>The venue said no, with a reason the organizer is shown.</summary>
    Declined = 2,

    /// <summary>The organizer took the request back before it was answered.</summary>
    Withdrawn = 3,
}
