using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// Single source of truth for how a <see cref="CaseStatus"/> is labeled and colored across the
/// app. Previously reimplemented independently in 5+ components, which had already drifted
/// (Haunted rendered a different badge color depending on which page you were on).
/// </summary>
public static class CaseStatusExtensions
{
    /// <summary>The badge — the same words the API puts in a client's mail (item 206).</summary>
    public static string Label(this CaseStatus status) => CaseStatusWording.Label(status);

    /// <summary>The line under the badge, written to the client — and the line in their mail.</summary>
    public static string ClientSentence(this CaseStatus status) => CaseStatusWording.ClientSentence(status);

    public static string BadgeClass(this CaseStatus status) => status switch
    {
        CaseStatus.Proposed    => "bg-secondary",
        CaseStatus.Accepted    => "bg-primary",
        CaseStatus.Active      => "bg-success",
        CaseStatus.Summarized  => "bg-warning text-dark",
        CaseStatus.Closed      => "bg-dark",
        // NOT text-dark. Bootstrap's own --bs-info is a bright cyan that wants dark text, and this
        // line was written for it; this template remaps --bs-info to #66366c, a deep purple, where
        // dark text measures 2.31:1 and white measures 9.09:1. Found 2026-09-22 on the public case
        // list and the case header. The other text-dark pairings below are on --bs-warning
        // (#aaa256, an olive), where dark text is right at 8.01:1.
        CaseStatus.Public      => "bg-info",
        CaseStatus.Haunted     => "bg-warning text-dark",
        CaseStatus.Transferred => "bg-secondary",
        // Danger, not warning: a paused case needs somebody to act (renew, or reassign), and the
        // yellow family is already spoken for by two working states.
        CaseStatus.Paused      => "bg-danger",
        _                      => "bg-secondary",
    };
}
