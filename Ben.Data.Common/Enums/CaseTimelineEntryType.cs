namespace Ben.Data.Common.Enums;

/// <summary>Identifies who authored a case timeline entry and what kind of content it contains.</summary>
public enum CaseTimelineEntryType
{
    /// <summary>An experience reported by the client or a linked witness.</summary>
    ClientReport      = 0,

    /// <summary>A note or observation from an investigator during or after an investigation.</summary>
    InvestigatorNote  = 1,

    /// <summary>Submitted evidence (file + description).</summary>
    Evidence          = 2,

    /// <summary>Historical or contextual research on the location.</summary>
    ResearchNote      = 3,
}
