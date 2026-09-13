using Ben.Service.Models.Admin;

namespace Ben.Web.Services.WebApi;

/// <summary>The event oversight slice — see <see cref="IBenEventOversightClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    public Task<ItemResult<AdminHostedEventsRecord>> GetAdminHostedEventsAsync(CancellationToken token = default)
        => _api.GetItemAsync<AdminHostedEventsRecord>("/api/admin/hosted-events", token);

    public Task<ItemResult<AdminHostedEventStats>> GetAdminHostedEventStatsAsync(int days = 30, CancellationToken token = default)
        => _api.GetItemAsync<AdminHostedEventStats>($"/api/admin/hosted-events/stats?days={days}", token);

    public Task<ItemResult<AdminHostedEventRemovalEffect>> GetHostedEventRemovalEffectAsync(Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<AdminHostedEventRemovalEffect>($"/api/admin/hosted-events/{eventId}/removal-effect", token);

    public Task<(AdminHostedEventsRecord? Result, string? Error)> RemoveHostedEventAsync(
        Guid eventId, RemoveHostedEventRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RemoveHostedEventRequest, AdminHostedEventsRecord>(
               HttpMethod.Post, $"/api/admin/hosted-events/{eventId}/remove", request, token);

    public Task<(AdminHostedEventsRecord? Result, string? Error)> DecideHostedEventAppealAsync(
        Guid removalId, DecideHostedEventAppealRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DecideHostedEventAppealRequest, AdminHostedEventsRecord>(
               HttpMethod.Post, $"/api/admin/hosted-events/appeals/{removalId}/decide", request, token);

    public Task<ItemResult<HostedEventRemovalRecord>> GetHostedEventRemovalAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventRemovalRecord>($"/api/organizations/{orgId}/events/{eventId}/removal", token);

    public Task<(HostedEventRemovalRecord? Result, string? Error)> AppealHostedEventRemovalAsync(
        Guid orgId, Guid eventId, AppealHostedEventRemovalRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AppealHostedEventRemovalRequest, HostedEventRemovalRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/events/{eventId}/removal/appeal", request, token);
}
