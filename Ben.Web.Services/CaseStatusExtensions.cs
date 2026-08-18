using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// Single source of truth for how a <see cref="CaseStatus"/> is labeled and colored across the
/// app. Previously reimplemented independently in 5+ components, which had already drifted
/// (Haunted rendered a different badge color depending on which page you were on).
/// </summary>
public static class CaseStatusExtensions
{
    public static string Label(this CaseStatus status) => status switch
    {
        CaseStatus.Proposed    => "Proposed",
        CaseStatus.Accepted    => "Accepted",
        CaseStatus.Active      => "Active",
        CaseStatus.Summarized  => "Summarizing",
        CaseStatus.Closed      => "Closed",
        CaseStatus.Public      => "Public",
        CaseStatus.Haunted     => "Haunted",
        CaseStatus.Transferred => "Transferred",
        _                      => status.ToString(),
    };

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
        _                      => "bg-secondary",
    };
}
