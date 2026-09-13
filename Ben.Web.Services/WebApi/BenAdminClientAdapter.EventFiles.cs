using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The event files slice — see <see cref="IBenEventFileClient"/>.</summary>
public sealed partial class BenAdminClientAdapter
{
    private static string EventFilesUrl(Guid orgId, Guid eventId) => $"/api/organizations/{orgId}/events/{eventId}/files";

    public Task<LoadResult<HostedEventFileRecord>> GetEventFilesAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetListAsync<HostedEventFileRecord>(EventFilesUrl(orgId, eventId), token);

    public Task<(List<HostedEventFileRecord>? Result, string? Error)> AddEventFileAsync(
        Guid orgId, Guid eventId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<List<HostedEventFileRecord>>(EventFilesUrl(orgId, eventId), content, token);

    public Task<(List<HostedEventFileRecord>? Result, string? Error)> UpdateEventFileAsync(
        Guid orgId, Guid eventId, Guid fileId, UpdateHostedEventFileRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpdateHostedEventFileRequest, List<HostedEventFileRecord>>(
               HttpMethod.Put, $"{EventFilesUrl(orgId, eventId)}/{fileId}", request, token);

    public Task<(List<HostedEventFileRecord>? Result, string? Error)> DeleteEventFileAsync(
        Guid orgId, Guid eventId, Guid fileId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, List<HostedEventFileRecord>>(
               HttpMethod.Delete, $"{EventFilesUrl(orgId, eventId)}/{fileId}", new { }, token);

    public Task<LoadResult<HostedEventFileRecord>> GetPublicEventFilesAsync(Guid eventId, bool signedIn, CancellationToken token = default)
        => signedIn
            ? _api.GetListAsync<HostedEventFileRecord>($"/api/public/hosted-events/{eventId}/files", token)
            : _api.GetAnonymousListAsync<HostedEventFileRecord>($"/api/public/hosted-events/{eventId}/files", token);
}
