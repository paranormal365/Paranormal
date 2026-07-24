namespace Ben.Data.Common.Enums;

/// <summary>Lifecycle status of an investigation case.</summary>
public enum CaseStatus
{
    /// <summary>Proposed by a member — awaiting org-admin acceptance.</summary>
    Proposed   = 0,

    /// <summary>Accepted by the org; case manager assigned; CMS pages auto-generated.</summary>
    Accepted   = 1,

    /// <summary>Active investigation underway.</summary>
    Active     = 2,

    /// <summary>Investigations complete; org reviewing collected data.</summary>
    Summarized = 3,

    /// <summary>Closed with no determination.</summary>
    Closed     = 4,

    /// <summary>Published publicly (with optional pseudonyms/redactions).</summary>
    Public     = 5,

    /// <summary>Evidence deemed conclusive — case marked as haunted and made public.</summary>
    Haunted    = 6,

    /// <summary>Transferred to another organization.</summary>
    Transferred = 7,
}
