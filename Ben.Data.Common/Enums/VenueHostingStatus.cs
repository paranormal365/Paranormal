namespace Ben.Data.Common.Enums;

/// <summary>
/// Where one group's request to hold an event at another's venue stands (item 235).
/// </summary>
/// <remarks>
/// Ben, 2026-09-11: "Thomas House has an event for themselves but then someone else wants to host
/// an event there. Maybe they could ask for permission to use photos and history and number of
/// rooms and people. I want people to be able to work together."
///
/// Approval writes a grant, which is a separate record with its own dates and its own revoke —
/// the request is the conversation, the grant is the permission, and the two must not be one row
/// or revoking would mean rewriting history.
/// </remarks>
public enum VenueHostingStatus
{
    /// <summary>Asked; the venue has not answered.</summary>
    Requested = 0,

    /// <summary>The venue said yes, and a grant exists.</summary>
    Approved = 1,

    /// <summary>The venue said no.</summary>
    Declined = 2,

    /// <summary>The asking group took it back before an answer.</summary>
    Withdrawn = 3,
}
