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

// ── Sign-in insights ─────────────────────────────────────────────────────────
// Unlike everything above, these records NAME accounts. See AdminStatsController's remarks for
// why that boundary is crossed deliberately here and nowhere else on the dashboard.

/// <summary>One account, with its sign-in activity over the window.</summary>
/// <param name="Name">Display name, falling back to the handle and then to the account id.</param>
/// <param name="Handle">The permanent @name, when they have one.</param>
/// <param name="Count">Attempts of the kind this list is about — successes or failures.</param>
/// <param name="LastUtc">The most recent of those attempts.</param>
public sealed record SignInPerson(
    Guid AppUserId,
    string Name,
    string? Handle,
    int Count,
    DateTime LastUtc);

/// <summary>One sign-in, for the "who just arrived" list.</summary>
/// <param name="Method">"password" or "apple" — see <c>RecordingSignInManager</c>.</param>
public sealed record RecentSignIn(
    Guid AppUserId,
    string Name,
    string? Handle,
    string Method,
    DateTime Utc);

/// <summary>
/// Something in the sign-in record worth a second look.
/// </summary>
/// <remarks>
/// Deliberately a sentence rather than a number. Each of these is a pattern an administrator
/// would act on — and the action is always "go and look", never "the system has decided". Naming
/// the account is the point where one is about a person; <paramref name="AppUserId"/> is null for
/// the ones that are about the site as a whole.
/// </remarks>
/// <param name="Kind">A stable slug, so the UI can pick an icon without matching on prose.</param>
/// <param name="Headline">The finding, in one short line.</param>
/// <param name="Detail">What it is based on, so a reader can judge it rather than trust it.</param>
public sealed record SignInOddity(
    string Kind,
    string Headline,
    string Detail,
    Guid? AppUserId);

/// <summary>
/// Who has been signing in, and anything odd about how.
/// </summary>
/// <param name="Recent">
/// The most recent sign-in of each of the last ten DISTINCT accounts — not the last ten events,
/// which one busy person can fill on their own.
/// </param>
/// <param name="TopPeople">Accounts ranked by successful sign-ins in the window.</param>
/// <param name="TopGroups">
/// Groups ranked by their active members' sign-ins. A person in two groups counts once for each:
/// the question is which group's people are showing up, not how the total divides.
/// </param>
/// <param name="ByMethod">The split between password and Apple sign-ins.</param>
/// <param name="ByHourUtc">
/// Twenty-four buckets, midnight-first, labelled in UTC. Shape, not schedule — the site has no
/// per-account timezone to reduce these to local hours.
/// </param>
/// <param name="MostFailures">Accounts ranked by FAILED attempts in the window.</param>
/// <param name="Oddities">Patterns worth a look, most pointed first.</param>
/// <param name="CoversAppleSignIns">
/// Whether Apple sign-ins are represented in these numbers at all. False on a server whose
/// records predate the change that started recording them, so a reader can tell a genuine absence
/// of Apple sign-ins from a blind spot.
/// </param>
public sealed record AdminSignInInsights(
    IReadOnlyList<RecentSignIn> Recent,
    IReadOnlyList<SignInPerson> TopPeople,
    IReadOnlyList<StatSlice> TopGroups,
    IReadOnlyList<StatSlice> ByMethod,
    IReadOnlyList<StatSlice> ByHourUtc,
    IReadOnlyList<SignInPerson> MostFailures,
    IReadOnlyList<SignInOddity> Oddities,
    bool CoversAppleSignIns);


/// <summary>
/// What deleting a group would destroy, counted before anything is.
/// </summary>
/// <remarks>
/// Mirrors <c>OrganizationPurgePreview</c> beside <c>OrganizationPurge</c> in the WebApi project;
/// decoded from the server's JSON by name, so a rename has to happen on both sides.
/// </remarks>
/// <param name="StoredFiles">Files whose BYTES are deleted from storage, not merely their rows.</param>
public sealed record OrganizationPurgePreview(
    Guid OrganizationId,
    string OrganizationName,
    int Members,
    int Cases,
    int Investigations,
    int Events,
    int FieldSessions,
    int EventEvidence,
    int StoredFiles,
    int CmsPages,
    int BillingRows);

/// <summary>
/// What deleting one person would destroy, and what it would leave standing.
/// </summary>
/// <remarks>
/// <para>Mirror of <c>AppUserPurge</c>'s record — this library cannot reference the WebApi
/// project, so the shape is restated and married by property name.</para>
///
/// <para><b>Two groups of counts, and the difference is the point.</b> The first are records the
/// delete destroys. The second are records it keeps and strips the name out of, because they were
/// authored for a group and a group's history is not one member's to erase. A screen that showed
/// a single total would be describing something the delete does not do.</para>
///
/// <para><c>RowWillSurvive</c> says, before the button is pressed, whether the account row itself
/// will actually disappear. An account holding nothing else does vanish; one that wrote a case
/// note two years ago cannot, because that note still refers to it.</para>
///
/// <para><c>OwnedOrganizations</c> and <c>PaidSubscriptions</c> are notices, never bars (Ben,
/// 2026-09-04). <c>Refusal</c> is the only thing that disables the button.</para>
/// </remarks>
public sealed record AppUserPurgePreview(
    Guid AppUserId,
    string DisplayName,
    string? Email,
    bool AlreadyClosed,

    int PersonalFieldSessions,
    int StoredFiles,
    int Memberships,
    int SignInEvents,
    int MessagesReceived,
    int FollowsAndBlocks,
    int ContactRows,
    int ExternalLogins,

    int CaseNotes,
    int TimelineEntries,
    int GroupMessages,
    int GroupFieldSessions,
    int EventEvidence,
    int OtherAuthoredRecords,

    bool RowWillSurvive,
    IReadOnlyList<string> OwnedOrganizations,
    IReadOnlyList<string> PaidSubscriptions,
    string? Refusal);

/// <summary>What deleting a person actually did.</summary>
/// <remarks>
/// <c>RowRemoved</c> is reported rather than assumed: the screen promised one of two outcomes and
/// has to be able to say which one happened.
/// </remarks>
public sealed record AppUserPurgeResult(
    Guid AppUserId, string DisplayName, bool RowRemoved,
    int PersonalFieldSessions, int StoredFiles, int Memberships, int SignInEvents,
    int MessagesReceived, int FollowsAndBlocks);
