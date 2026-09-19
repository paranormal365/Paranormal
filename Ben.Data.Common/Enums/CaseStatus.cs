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

    /// <summary>
    /// Suspended because the organization's subscription lapsed. Item 84.
    /// </summary>
    /// <remarks>
    /// <para>Everything stays readable — the client, the group, nothing is deleted — but no new
    /// records are added while a case is paused. The client may choose a new organization; the
    /// case's prior status is kept (<c>Case.StatusBeforePause</c>) so a group that renews gets
    /// its cases back exactly as they were.</para>
    ///
    /// <para>Deliberately AFTER <see cref="Transferred"/> in value: several places count "open"
    /// as <c>&lt;= Summarized</c>, and a paused case must not consume the open-case cap or count
    /// as open work. Appended, never renumbered — statuses live in rows.</para>
    /// </remarks>
    Paused = 8,
}
