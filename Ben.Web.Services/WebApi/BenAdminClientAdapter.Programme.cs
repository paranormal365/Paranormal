using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The programme slice — see <see cref="IBenProgrammeClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    private static string SessionsUrl(Guid orgId, Guid eventId) => $"/api/organizations/{orgId}/events/{eventId}/sessions";

    private static string PublicProgrammeUrl(Guid eventId) => $"/api/public/hosted-events/{eventId}";

    public Task<ItemResult<HostedEventProgrammeRecord>> GetEventProgrammeAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventProgrammeRecord>(SessionsUrl(orgId, eventId), token);

    public Task<(HostedEventProgrammeRecord? Result, string? Error)> AddEventSessionAsync(
        Guid orgId, Guid eventId, SaveHostedEventSessionRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveHostedEventSessionRequest, HostedEventProgrammeRecord>(
               HttpMethod.Post, SessionsUrl(orgId, eventId), request, token);

    public Task<(HostedEventProgrammeRecord? Result, string? Error)> SaveEventSessionAsync(
        Guid orgId, Guid eventId, Guid sessionId, SaveHostedEventSessionRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveHostedEventSessionRequest, HostedEventProgrammeRecord>(
               HttpMethod.Put, $"{SessionsUrl(orgId, eventId)}/{sessionId}", request, token);

    public Task<(HostedEventProgrammeRecord? Result, string? Error)> CancelEventSessionAsync(
        Guid orgId, Guid eventId, Guid sessionId, CancelHostedEventSessionRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CancelHostedEventSessionRequest, HostedEventProgrammeRecord>(
               HttpMethod.Post, $"{SessionsUrl(orgId, eventId)}/{sessionId}/cancel", request, token);

    public Task<(HostedEventProgrammeRecord? Result, string? Error)> DeleteEventSessionAsync(
        Guid orgId, Guid eventId, Guid sessionId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventProgrammeRecord>(
               HttpMethod.Delete, $"{SessionsUrl(orgId, eventId)}/{sessionId}", new { }, token);

    public Task<(HostedEventProgrammeRecord? Result, string? Error)> PublishEventProgrammeAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventProgrammeRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/events/{eventId}/programme/publish", new { }, token);

    public Task<(HostedEventProgrammeRecord? Result, string? Error)> UnpublishEventProgrammeAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventProgrammeRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/events/{eventId}/programme/unpublish", new { }, token);

    public Task<ItemResult<HostedEventSessionRosterRecord>> GetEventSessionRosterAsync(
        Guid orgId, Guid eventId, Guid sessionId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventSessionRosterRecord>($"{SessionsUrl(orgId, eventId)}/{sessionId}/roster", token);

    public Task<(HostedEventSessionRosterRecord? Result, string? Error)> RemoveEventSessionSignUpAsync(
        Guid orgId, Guid eventId, Guid sessionId, Guid signUpId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventSessionRosterRecord>(
               HttpMethod.Delete, $"{SessionsUrl(orgId, eventId)}/{sessionId}/sign-ups/{signUpId}", new { }, token);

    public Task<ItemResult<PublicProgrammeRecord>> GetPublicProgrammeAsync(Guid eventId, bool signedIn, CancellationToken token = default)
        => signedIn
            ? _api.GetItemAsync<PublicProgrammeRecord>($"{PublicProgrammeUrl(eventId)}/programme", token)
            : _api.GetAnonymousItemAsync<PublicProgrammeRecord>($"{PublicProgrammeUrl(eventId)}/programme", token);

    public Task<(PublicProgrammeRecord? Result, string? Error)> SignUpForSessionAsync(
        Guid eventId, Guid sessionId, SessionSignUpRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SessionSignUpRequest, PublicProgrammeRecord>(
               HttpMethod.Post, $"{PublicProgrammeUrl(eventId)}/sessions/{sessionId}/sign-up", request, token);

    public Task<(PublicProgrammeRecord? Result, string? Error)> LeaveSessionAsync(
        Guid eventId, Guid sessionId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, PublicProgrammeRecord>(
               HttpMethod.Delete, $"{PublicProgrammeUrl(eventId)}/sessions/{sessionId}/sign-up", new { }, token);

    public async Task MarkProgrammeSeenAsync(Guid eventId, CancellationToken token = default)
        => await _api.SendExpectingReasonAsync<object, object>(
               HttpMethod.Post, $"{PublicProgrammeUrl(eventId)}/programme/seen", new { }, token);
}
