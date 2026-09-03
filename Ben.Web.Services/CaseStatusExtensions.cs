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
        CaseStatus.Public      => "bg-info text-dark",
        CaseStatus.Haunted     => "bg-warning text-dark",
        CaseStatus.Transferred => "bg-secondary",
        // Danger, not warning: a paused case needs somebody to act (renew, or reassign), and the
        // yellow family is already spoken for by two working states.
        CaseStatus.Paused      => "bg-danger",
        _                      => "bg-secondary",
    };
}
