using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// The founding groups promotion as anybody sees it (11/02/2026): what's on offer, the rules' numbers, spots left,
/// and the leaderboard once a second group has qualified.
/// </summary>
/// <param name="Showing">The promotion is open; nothing about it shows while this is false.</param>
/// <param name="ClaimsOpen">Spots can be claimed now: open, spots left, inside the claim window.</param>
public sealed record FoundingGroupsPublicRecord(
    bool Showing,
    bool ClaimsOpen,
    int Spots,
    int SpotsLeft,
    DateTime? ClaimsCloseUtc,
    int GatherDays,
    int MinimumMembers,
    int FreeMonths,
    int Winners,
    int WinnerMonths,
    int GroupOfTheMonthMonths,
    int ReachedScoreMonths,
    int ReachableScorePerMember,
    int PointsPerSession,
    int PointsPerInvestigation,
    int PointsPerEvent,
    int PointsPerEvidence,
    int PointsPerReferral,
    int MinimumReadingsPerSession,
    IReadOnlyList<FoundingGroupsPlanRecord> Plans,
    IReadOnlyList<FoundingGroupsStandingRecord>? Leaderboard,
    IReadOnlyList<FoundingGroupsMonthWinnerRecord> MonthWinners,
    bool Decided);

/// <summary>A plan and the group sizes that earn it, read from the live ladder.</summary>
public sealed record FoundingGroupsPlanRecord(string Name, int MinMembers, int? MaxMembers, decimal? MonthlyPrice);

/// <summary>One group on the leaderboard: points per member per month so far, so groups at different stages compare.</summary>
public sealed record FoundingGroupsStandingRecord(
    int Rank, string GroupName, string? GroupUrlName, decimal PerMemberPerMonth, int MonthsIn, int? FinalRank);

public sealed record FoundingGroupsMonthWinnerRecord(DateTime Month, string GroupName, string? GroupUrlName);

/// <summary>A group's own standing in the promotion, for its members and admins.</summary>
/// <param name="CanManage">The caller may claim and sees every member's score; otherwise only their own.</param>
/// <param name="WhyCantClaim">Why claiming isn't possible now, when there's no entry; null when it is.</param>
public sealed record FoundingGroupStatusRecord(
    Guid OrganizationId,
    string OrganizationName,
    bool CanManage,
    PromotionEntryState? State,
    DateTime? GatherEndsUtc,
    DateTime? QualifiedUtc,
    DateTime? ContestEndsUtc,
    DateTime? FreeUntilUtc,
    string? PlanName,
    string? PlanWouldEarn,
    int MinimumMembers,
    string? WhyCantClaim,
    string? DisqualifiedReason,
    int? FinalRank,
    IReadOnlyList<FoundingGroupMemberRecord> Members,
    FoundingGroupScoreRecord? Score);

/// <summary>One member: counted, or why not.</summary>
public sealed record FoundingGroupMemberRecord(Guid AppUserId, string Name, bool Counts, string? WhyNot);

/// <summary>The group's score so far; <paramref name="People"/> holds everyone for a manager, or just the caller.</summary>
public sealed record FoundingGroupScoreRecord(
    int Members, int Total, decimal PerMember, decimal PerMemberPerMonth, int Events, int Referrals, int GroupPoints,
    IReadOnlyList<FoundingGroupPersonScoreRecord> People);

public sealed record FoundingGroupPersonScoreRecord(
    Guid AppUserId, string Name, int Sessions, int Investigations, int Evidence, int Points);

/// <summary>A group's referrer and the free months it has earned, for its billing page.</summary>
/// <param name="ReferrerFixed">The group has paid, so its referrer can no longer change.</param>
public sealed record GroupReferralRecord(
    Guid? ReferredByOrganizationId, string? ReferredByName, bool ReferrerFixed, bool CanManage,
    IReadOnlyList<FreeMonthsRecord> FreeMonths);

public sealed record FreeMonthsRecord(
    Guid Id, FreeMonthReason Reason, int Months, DateTime EarnedUtc, DateTime? AppliedUtc,
    DateTime? AppliedToPeriodEndUtc, DateTime? RevokedUtc, string? FromGroupName, string? Note);

public sealed record SetGroupReferrerRequest(Guid? ReferredByOrganizationId);

/// <summary>A group a person might name as their referrer.</summary>
public sealed record GroupPickRecord(Guid Id, string Name, string? UrlName);

// ── administration ────────────────────────────────────────────────────────────

/// <summary>The promotion for an administrator: every setting, every entry, every free month.</summary>
public sealed record AdminFoundingGroupsRecord(
    FoundingGroupsSettingsRecord Settings,
    DateTime? OpenedUtc,
    DateTime? ClaimsCloseUtc,
    DateTime? DecidedUtc,
    int SpotsTaken,
    IReadOnlyList<AdminFoundingGroupEntryRecord> Entries,
    IReadOnlyList<AdminFreeMonthsRecord> FreeMonths);

public sealed record FoundingGroupsSettingsRecord(
    bool IsOpen, int ClaimWindowDays, int Spots, int GatherDays, int MinimumMembers, int FreeMonths, int Winners,
    int WinnerMonths, int GroupOfTheMonthMonths, int ReachedScoreMonths, int ReachableScorePerMember,
    int PointsPerSession, int PointsPerInvestigation, int PointsPerEvent, int PointsPerEvidence, int PointsPerReferral,
    int MinimumReadingsPerSession);

public sealed record AdminFoundingGroupEntryRecord(
    Guid Id, Guid OrganizationId, string OrganizationName, PromotionEntryState State, DateTime ClaimedUtc,
    DateTime GatherEndsUtc, DateTime? QualifiedUtc, DateTime? ContestEndsUtc, int CountedMembers, string? PlanName,
    decimal? PerMemberSoFar, decimal? FinalScorePerMember, int? FinalRank, string? DisqualifiedReason);

public sealed record AdminFreeMonthsRecord(
    Guid Id, Guid OrganizationId, string OrganizationName, FreeMonthReason Reason, int Months, DateTime EarnedUtc,
    DateTime? AppliedUtc, DateTime? RevokedUtc, string? FromGroupName, string? Note);

public sealed record DisqualifyFoundingGroupRequest(string Reason);

public sealed record GiveFreeMonthsRequest(Guid OrganizationId, int Months, string Note);
