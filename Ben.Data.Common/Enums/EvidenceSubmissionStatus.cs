namespace Ben.Data.Common.Enums;

/// <summary>Where a visitor's evidence submission stands in the group's review.</summary>
public enum EvidenceSubmissionStatus
{
    /// <summary>Offered, not yet looked at. Visible to the submitter and the group only.</summary>
    Pending = 0,

    /// <summary>Part of the record. At a public event that means public — item 87's bargain.</summary>
    Accepted = 1,

    /// <summary>Declined, with a reason the submitter can read.</summary>
    Rejected = 2,
}
