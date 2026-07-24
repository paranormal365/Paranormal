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
}
