namespace Ben.Data.Common.Enums;

/// <summary>Identifies the communication channel/type for an OrgMessage.</summary>
public enum OrgMessageChannel
{
    /// <summary>Broadcast to all active org members.</summary>
    OrgBroadcast    = 0,

    /// <summary>Private direct message between two users.</summary>
    DirectMessage   = 1,

    /// <summary>Message scoped to a specific case's team (case manager + assigned members).</summary>
    CaseTeam        = 2,

    /// <summary>Public post — visible outside the organization (social feed).</summary>
    PublicFeed      = 3,

    /// <summary>
    /// A comment somebody left on a published case (item 233, Ben 2026-09-11).
    /// </summary>
    /// <remarks>
    /// <para>A comment is an <c>OrgMessage</c> rather than a table of its own, and that is the
    /// whole reason it can be moderated on day one: hiding, reporting, the author trail and the
    /// audit columns already exist on this entity and already work. A second table would have
    /// arrived with none of them and needed each one written again.</para>
    ///
    /// <para>It carries its case in <c>CaseId</c>, belongs to no organization, and is public by
    /// definition — a comment on a published case is published with it.</para>
    /// </remarks>
    PublicCaseComment = 4,


    /// <summary>
    /// The room for one hosted event: what the people who are there share with each other while
    /// they are there (item 235).
    /// </summary>
    /// <remarks>
    /// <para>An <c>OrgMessage</c> for the same reason a case comment is one — hiding, reporting,
    /// the author trail, the media screening queue and the audit columns already exist here and
    /// already work.</para>
    ///
    /// <para>It carries its event in <c>HostedEventId</c> and is the first channel whose audience
    /// is neither an organization nor the public: it is the people with a confirmed booking, plus
    /// the staff. Every public feed query must exclude it, and a guard test says so.</para>
    /// </remarks>
    EventRoom = 5,
}
