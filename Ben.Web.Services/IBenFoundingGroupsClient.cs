using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>The founding groups promotion and group-to-group referrals (11/02/2026).</summary>
public interface IBenFoundingGroupsClient
{
    /// <summary>What anybody sees: the offer, its numbers, spots left and the leaderboard. Anonymous.</summary>
    Task<ItemResult<FoundingGroupsPublicRecord>> GetFoundingGroupsAsync(CancellationToken token = default);

    /// <summary>A group's own standing: its week, who counts and why not, its plan and score.</summary>
    Task<ItemResult<FoundingGroupStatusRecord>> GetFoundingGroupAsync(Guid organizationId, CancellationToken token = default);

    /// <summary>Takes a spot for the group and starts its week.</summary>
    Task<(FoundingGroupStatusRecord? Result, string? Error)> ClaimFoundingGroupSpotAsync(Guid organizationId, CancellationToken token = default);

    /// <summary>The group that referred this one, and the free months this group has earned.</summary>
    Task<ItemResult<GroupReferralRecord>> GetGroupReferralAsync(Guid organizationId, CancellationToken token = default);

    /// <summary>Names (or clears) the group that referred this one.</summary>
    Task<(GroupReferralRecord? Result, string? Error)> SetGroupReferrerAsync(Guid organizationId, Guid? referredBy, CancellationToken token = default);

    /// <summary>Groups whose name matches, to pick a referrer from.</summary>
    Task<LoadResult<GroupPickRecord>> FindReferrerGroupsAsync(Guid organizationId, string text, CancellationToken token = default);

    // ── administration ──
    Task<ItemResult<AdminFoundingGroupsRecord>> GetAdminFoundingGroupsAsync(CancellationToken token = default);
    Task<(AdminFoundingGroupsRecord? Result, string? Error)> SaveFoundingGroupsSettingsAsync(FoundingGroupsSettingsRecord settings, CancellationToken token = default);
    Task<(AdminFoundingGroupsRecord? Result, string? Error)> DisqualifyFoundingGroupAsync(Guid entryId, string reason, CancellationToken token = default);
    Task<(AdminFoundingGroupsRecord? Result, string? Error)> GiveFreeMonthsAsync(GiveFreeMonthsRequest request, CancellationToken token = default);
    Task<(AdminFoundingGroupsRecord? Result, string? Error)> RevokeFreeMonthsAsync(Guid id, string? reason, CancellationToken token = default);
}
