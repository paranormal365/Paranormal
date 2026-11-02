using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The founding groups slice — see <see cref="IBenFoundingGroupsClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    public Task<ItemResult<FoundingGroupsPublicRecord>> GetFoundingGroupsAsync(CancellationToken token = default)
        => _api.GetAnonymousItemAsync<FoundingGroupsPublicRecord>("/api/public/founding-groups", token);

    public Task<ItemResult<FoundingGroupStatusRecord>> GetFoundingGroupAsync(Guid organizationId, CancellationToken token = default)
        => _api.GetItemAsync<FoundingGroupStatusRecord>($"/api/organizations/{organizationId}/founding-group", token);

    public Task<(FoundingGroupStatusRecord? Result, string? Error)> ClaimFoundingGroupSpotAsync(Guid organizationId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, FoundingGroupStatusRecord>(
               HttpMethod.Post, $"/api/organizations/{organizationId}/founding-group/claim", new { }, token);

    public Task<ItemResult<GroupReferralRecord>> GetGroupReferralAsync(Guid organizationId, CancellationToken token = default)
        => _api.GetItemAsync<GroupReferralRecord>($"/api/organizations/{organizationId}/referral", token);

    public Task<(GroupReferralRecord? Result, string? Error)> SetGroupReferrerAsync(Guid organizationId, Guid? referredBy, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetGroupReferrerRequest, GroupReferralRecord>(
               HttpMethod.Put, $"/api/organizations/{organizationId}/referral", new SetGroupReferrerRequest(referredBy), token);

    public Task<LoadResult<GroupPickRecord>> FindReferrerGroupsAsync(Guid organizationId, string text, CancellationToken token = default)
        => _api.GetListAsync<GroupPickRecord>(
               $"/api/organizations/{organizationId}/referral/groups?q={Uri.EscapeDataString(text)}", token);

    public Task<ItemResult<AdminFoundingGroupsRecord>> GetAdminFoundingGroupsAsync(CancellationToken token = default)
        => _api.GetItemAsync<AdminFoundingGroupsRecord>("/api/admin/founding-groups", token);

    public Task<(AdminFoundingGroupsRecord? Result, string? Error)> SaveFoundingGroupsSettingsAsync(FoundingGroupsSettingsRecord settings, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<FoundingGroupsSettingsRecord, AdminFoundingGroupsRecord>(
               HttpMethod.Put, "/api/admin/founding-groups", settings, token);

    public Task<(AdminFoundingGroupsRecord? Result, string? Error)> DisqualifyFoundingGroupAsync(Guid entryId, string reason, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DisqualifyFoundingGroupRequest, AdminFoundingGroupsRecord>(
               HttpMethod.Post, $"/api/admin/founding-groups/entries/{entryId}/disqualify", new DisqualifyFoundingGroupRequest(reason), token);

    public Task<(AdminFoundingGroupsRecord? Result, string? Error)> GiveFreeMonthsAsync(GiveFreeMonthsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<GiveFreeMonthsRequest, AdminFoundingGroupsRecord>(
               HttpMethod.Post, "/api/admin/founding-groups/free-months", request, token);

    public Task<(AdminFoundingGroupsRecord? Result, string? Error)> RevokeFreeMonthsAsync(Guid id, string? reason, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, AdminFoundingGroupsRecord>(
               HttpMethod.Delete, $"/api/admin/founding-groups/free-months/{id}?reason={Uri.EscapeDataString(reason ?? "")}", new { }, token);
}
