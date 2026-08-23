namespace Ben.Service.Models.Admin;

/// <summary>One labelled number, for a bar or a donut.</summary>
public sealed record StatSlice(string Label, int Count);

/// <summary>One day's number, for a time series.</summary>
public sealed record StatPoint(DateOnly Day, int Count);

/// <summary>
/// The headline counts for the administrator's dashboard.
/// </summary>
/// <param name="People">Registered accounts.</param>
/// <param name="PeopleInAGroup">
/// Accounts with at least one active group membership. The gap against <paramref name="People"/>
/// is the funnel number: people who registered and never joined anything.
/// </param>
/// <param name="Groups">Organizations.</param>
/// <param name="Cases">Cases in any state.</param>
/// <param name="Investigations">Investigations in any state.</param>
/// <param name="NewPeopleThisWeek">Registrations in the last seven days.</param>
/// <param name="NewCasesThisWeek">Cases opened in the last seven days.</param>
/// <param name="SignInsThisWeek">Successful sign-ins in the last seven days, attempts not people.</param>
/// <param name="ActivePeopleThisWeek">Distinct accounts that signed in successfully in the last seven days.</param>
public sealed record AdminStatsSummary(
    int People,
    int PeopleInAGroup,
    int Groups,
    int Cases,
    int Investigations,
    int NewPeopleThisWeek,
    int NewCasesThisWeek,
    int SignInsThisWeek,
    int ActivePeopleThisWeek);

/// <summary>
/// The dashboard's charts.
/// </summary>
/// <param name="SignInsPerDay">Successful sign-ins by day, oldest first, gaps filled with zero.</param>
/// <param name="RegistrationsPerDay">New accounts by day, same shape.</param>
/// <param name="CasesByStatus">How the caseload is distributed.</param>
/// <param name="TopGroupsByMembers">Busiest groups by active membership.</param>
/// <param name="TopGroupsByActivity">Groups ranked by cases plus investigations in the window.</param>
/// <param name="TopStatesByUser">Where registered people are, by their address state.</param>
/// <param name="TopStatesByCase">Where the work is — cases carry the address the case is about.</param>
/// <param name="TopStatesByInvestigation">Where investigations happen, by the place visited.</param>
public sealed record AdminStatsCharts(
    IReadOnlyList<StatPoint> SignInsPerDay,
    IReadOnlyList<StatPoint> RegistrationsPerDay,
    IReadOnlyList<StatSlice> CasesByStatus,
    IReadOnlyList<StatSlice> TopGroupsByMembers,
    IReadOnlyList<StatSlice> TopGroupsByActivity,
    IReadOnlyList<StatSlice> TopStatesByUser,
    IReadOnlyList<StatSlice> TopStatesByCase,
    IReadOnlyList<StatSlice> TopStatesByInvestigation);

/// <summary>
/// A single group's numbers, for the group's own Details tab.
/// </summary>
/// <remarks>
/// <para>Visible to members of that group, and shaped by what the caller may read: the member
/// count is baseline, but the case and investigation numbers follow the same gate as the Cases
/// and Investigations tabs (Ben, 2026-08-23: "the gates count as tabs"). A seat the tabs are
/// hidden from receives NULL for those parts — never zero, because a zero is a lie the reader
/// cannot distinguish from an idle group.</para>
/// </remarks>
public sealed record OrgStatsSummary(
    int Members,
    int? Cases,
    int? Investigations,
    int? OpenCases,
    IReadOnlyList<StatSlice>? CasesByStatus,
    IReadOnlyList<StatPoint>? CasesPerMonth);
