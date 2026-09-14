namespace Ben.Data.Common.Enums;

/// <summary>
/// Where a group's claim to be the venue at a place stands (item 235 phase 9).
/// </summary>
/// <remarks>Append only. The numbers are stored.</remarks>
public enum VenueClaimState
{
    /// <summary>Made, and not yet proved — a code sent and not entered, or waiting for a person to review.</summary>
    Pending = 0,

    /// <summary>
    /// Proved by a code to the place's own contact, and standing for a week so the groups who know
    /// the place can object before it takes effect.
    /// </summary>
    Proved = 1,

    /// <summary>Accepted. The group is the venue at this place.</summary>
    Approved = 2,

    /// <summary>Refused by a person, with a reason.</summary>
    Refused = 3,

    /// <summary>The claimant took it back.</summary>
    Withdrawn = 4,

    /// <summary>Somebody objected. A person decides.</summary>
    Contested = 5,
}
