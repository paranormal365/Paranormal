namespace Ben.Data.Common.Enums;

/// <summary>
/// The words the site uses for a case's status — one source, so a mail to the client says exactly
/// what the page they open will say (item 206).
/// </summary>
/// <remarks>
/// <c>Label</c> is the badge. <c>ClientSentence</c> is the line under it, written to the client:
/// what this status means for them and what happens next. Both live here, in the common project,
/// because the API composes the mail and the website draws the page, and neither may drift from
/// the other.
/// </remarks>
public static class CaseStatusWording
{
    public static string Label(CaseStatus status) => status switch
    {
        CaseStatus.Proposed    => "Proposed",
        CaseStatus.Accepted    => "Accepted",
        CaseStatus.Active      => "Active",
        CaseStatus.Summarized  => "Summarizing",
        CaseStatus.Closed      => "Closed",
        CaseStatus.Public      => "Public",
        CaseStatus.Haunted     => "Haunted",
        CaseStatus.Transferred => "Transferred",
        CaseStatus.Paused      => "Paused",
        _                      => status.ToString(),
    };

    /// <summary>One sentence to the client: what this status means and what comes next.</summary>
    public static string ClientSentence(CaseStatus status) => status switch
    {
        CaseStatus.Proposed    => "The group is looking at your request and has not yet decided whether to take it on.",
        CaseStatus.Accepted    => "The group has taken your case and will be in touch to arrange a visit.",
        CaseStatus.Active      => "The group is working your case. Visits, findings and messages appear on your case page as they happen.",
        CaseStatus.Summarized  => "The visits are done and the group is writing up what they found. Their report will appear on your case page when it is ready.",
        CaseStatus.Closed      => "Your case is closed. Everything the group shared with you stays on your case page.",
        CaseStatus.Public      => "Your case is closed and, with your consent, its findings are published on the group's public page.",
        CaseStatus.Haunted     => "Your case is closed and the group has recorded it as haunted. Everything they shared with you stays on your case page.",
        CaseStatus.Transferred => "Your case has been passed to another group, who will take it from here. Nothing you shared is lost.",
        CaseStatus.Paused      => "Your case is paused while the group's account is sorted out. Nothing is lost, and work resumes when it is.",
        _                      => "Your case's status has changed. Open your case page to see where it stands.",
    };
}
