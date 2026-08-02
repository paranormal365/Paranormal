using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;

namespace Ben.Web.WebApp.Services.WebApi;

public sealed class WebApiClient : IWebApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IWebApiTokenStore _tokenStore;

    // NOTE: WebApiClient is resolved as a typed transient from the Blazor circuit scope,
    // so IWebApiTokenStore here is the correct circuit-scoped instance.
    // WebApiBearerTokenHandler was removed from the pipeline because IHttpClientFactory
    // resolves handlers from the ROOT scope, not the circuit scope — injecting IWebApiTokenStore
    // there always gave an empty, unrelated instance.
    public WebApiClient(HttpClient httpClient, IWebApiTokenStore tokenStore)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
    }

    /// <summary>Creates an HttpRequestMessage with the current bearer token attached.</summary>
    private HttpRequestMessage Auth(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(_tokenStore.AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenStore.AccessToken);
        return req;
    }

    public async Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Get, relativeUrl);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    public async Task<TResponse?> GetAnonymousAsync<TResponse>(string relativeUrl, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Put, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    public async Task<bool> DeleteAsync(string relativeUrl, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Delete, relativeUrl);
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> PutVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Put, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<AppUserRecord>> GetUsersAsync(CancellationToken token = default)
    {
        var users = await GetAsync<List<AppUserRecord>>("/api/app-users", token);
        return users ?? [];
    }

    public async Task<IReadOnlyList<OrganizationSummaryResponse>> GetMyOrganizationsAsync(CancellationToken token = default)
    {
        var organizations = await GetAsync<List<OrganizationSummaryResponse>>("/api/security/organizations/mine", token);
        return organizations ?? [];
    }

    public async Task<IReadOnlyList<UserSearchResultResponse>> SearchUsersAsync(string? query, int skip = 0, int take = 25, CancellationToken token = default)
    {
        var encodedQuery = Uri.EscapeDataString(query ?? string.Empty);
        var relativeUrl = $"/api/security/organizations/users/search?q={encodedQuery}&skip={skip}&take={take}";
        var users = await GetAsync<List<UserSearchResultResponse>>(relativeUrl, token);
        return users ?? [];
    }

    public Task<OrganizationSummaryResponse?> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken token = default)
    {
        return PostAsync<RegisterOrganizationRequest, OrganizationSummaryResponse>("/api/security/organizations/register", request, token);
    }

    public Task<bool?> CheckMyOrganizationAccessAsync(Guid organizationId, OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken token = default)
    {
        var relativeUrl = $"/api/organizations/{organizationId}/security/my-access?table={table}&action={action}";
        return GetAsync<bool?>(relativeUrl, token);
    }

    public Task<bool?> CheckOrganizationAccessAsync(Guid organizationId, CheckOrganizationAccessRequest request, CancellationToken token = default)
    {
        var relativeUrl = $"/api/organizations/{organizationId}/security/check-access";
        return PostAsync<CheckOrganizationAccessRequest, bool?>(relativeUrl, request, token);
    }

    public async Task<IReadOnlyList<OrganizationUserMembershipResponse>> GetOrganizationUsersAsync(Guid organizationId, CancellationToken token = default)
    {
        var relativeUrl = $"/api/organizations/{organizationId}/security/users";
        var users = await GetAsync<List<OrganizationUserMembershipResponse>>(relativeUrl, token);
        return users ?? [];
    }

    public Task<OrganizationUserMembershipResponse?> UpsertOrganizationMembershipAsync(Guid organizationId, Guid targetUserId, UpsertOrganizationMembershipRequest request, CancellationToken token = default)
    {
        var relativeUrl = $"/api/organizations/{organizationId}/security/users/{targetUserId}/membership";
        return PutAsync<UpsertOrganizationMembershipRequest, OrganizationUserMembershipResponse>(relativeUrl, request, token);
    }

    public Task<OrganizationAccessGrantResponse?> SetOrganizationGrantAsync(Guid organizationId, Guid targetUserId, SetOrganizationGrantRequest request, CancellationToken token = default)
    {
        var relativeUrl = $"/api/organizations/{organizationId}/security/users/{targetUserId}/grants";
        return PutAsync<SetOrganizationGrantRequest, OrganizationAccessGrantResponse>(relativeUrl, request, token);
    }

    // ── Upload File Types ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<UploadFileTypeRecord>> GetUploadFileTypesAsync(CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFileTypeRecord>>("/api/upload-file-types", token);
        return result ?? [];
    }

    // ── Upload Files ─────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<UploadFileRecord>> GetUploadFilesAsync(CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFileRecord>>("/api/upload-files", token);
        return result ?? [];
    }

    public async Task<UploadFileRecord?> UploadFileAsync(MultipartFormDataContent content, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, "/api/upload-files");
        req.Content = content;
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UploadFileRecord>(cancellationToken: token);
    }

    public async Task<TResponse?> PostMultipartAsync<TResponse>(string relativeUrl, MultipartFormDataContent content, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = content;
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    public Task<UploadFileRecord?> UpdateUploadFileAsync(Guid id, UpdateUploadFileRequest request, CancellationToken token = default)
        => PutAsync<UpdateUploadFileRequest, UploadFileRecord>($"/api/upload-files/{id}", request, token);

    public Task<bool> DeleteUploadFileAsync(Guid id, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{id}", token);

    public async Task<(byte[] Data, string ContentType, string FileName)?> DownloadFileAsync(Guid id, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Get, $"/api/upload-files/{id}/download");
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return null;
        var data = await response.Content.ReadAsByteArrayAsync(token);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "file";
        return (data, contentType, fileName);
    }

    public async Task<(byte[] Data, string ContentType, string FileName)?> GetBytesAsync(string relativeUrl, string fallbackFileName, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Get, relativeUrl);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return null;
        var data = await response.Content.ReadAsByteArrayAsync(token);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/pdf";
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? fallbackFileName;
        return (data, contentType, fileName);
    }

    // ── Audio Config ──────────────────────────────────────────────────────────
    public Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default)
        => GetAsync<UploadFileAudioConfigRecord>($"/api/upload-files/{fileId}/audio-config", token);

    public Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default)
        => PutAsync<UpsertAudioConfigRequest, UploadFileAudioConfigRecord>($"/api/upload-files/{fileId}/audio-config", request, token);

    public Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{fileId}/audio-config", token);

    // ── Region Notes ──────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFileRegionNoteRecord>>($"/api/upload-files/{fileId}/region-notes", token);
        return result ?? [];
    }

    public Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default)
        => PostAsync<CreateRegionNoteRequest, UploadFileRegionNoteRecord>($"/api/upload-files/{fileId}/region-notes", request, token);

    public Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default)
        => PutAsync<UpdateRegionNoteRequest, UploadFileRegionNoteRecord>($"/api/upload-files/{fileId}/region-notes/{noteId}", request, token);

    public Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{fileId}/region-notes/{noteId}", token);

    // ── Audio Clip ────────────────────────────────────────────────────────────
    public Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default)
        => PostAsync<ClipAudioRequest, UploadFileRecord>($"/api/upload-files/{fileId}/clip", request, token);

    public async Task<IReadOnlyList<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFileRecord>>($"/api/upload-files/{fileId}/clips", token);
        return result ?? [];
    }

    public async Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Get,
            $"/api/upload-files/{fileId}/clip/preview?start={start.ToString(System.Globalization.CultureInfo.InvariantCulture)}&end={end.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return null;
        var data        = await response.Content.ReadAsByteArrayAsync(token);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "audio/wav";
        return (data, contentType);
    }

    // ── Votes ──────────────────────────────────────────────────
    public Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default)
        => GetAsync<UploadFileVoteSummary>($"/api/upload-files/{fileId}/votes", token);

    public Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default)
        => PutAsync<UpsertVoteRequest, UploadFileVoteRecord>(
                $"/api/upload-files/{fileId}/votes/my-vote", new UpsertVoteRequest(score), token);

    public Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{fileId}/votes/my-vote", token);

    // ── Org Sharing ──────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<UploadFileOrgShareResponse>> GetFileOrgSharesAsync(Guid fileId, CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFileOrgShareResponse>>($"/api/upload-files/{fileId}/shares", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFileRecord>>($"/api/upload-files/org/{orgId}", token);
        return result ?? [];
    }

    public Task<UploadFileOrgShareResponse?> ShareFileWithOrgAsync(Guid fileId, ShareFileWithOrgRequest request, CancellationToken token = default)
        => PostAsync<ShareFileWithOrgRequest, UploadFileOrgShareResponse>($"/api/upload-files/{fileId}/shares", request, token);

    public Task<UploadFileOrgShareResponse?> UpdateOrgShareVisibilityAsync(Guid shareId, UpdateOrgShareVisibilityRequest request, CancellationToken token = default)
        => PutAsync<UpdateOrgShareVisibilityRequest, UploadFileOrgShareResponse>($"/api/upload-file-shares/{shareId}/visibility", request, token);

    public Task<bool> RemoveOrgShareAsync(Guid shareId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-file-shares/{shareId}", token);

    // ── Permission Requests ──────────────────────────────────────────────────
    public async Task<IReadOnlyList<UploadFilePermissionRequestResponse>> GetFilePermissionRequestsAsync(Guid fileId, CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFilePermissionRequestResponse>>($"/api/upload-files/{fileId}/permission-requests", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<UploadFilePermissionRequestResponse>> GetPendingPermissionRequestsForReviewerAsync(Guid reviewerUserId, CancellationToken token = default)
    {
        var result = await GetAsync<List<UploadFilePermissionRequestResponse>>($"/api/upload-file-permission-requests/pending-for/{reviewerUserId}", token);
        return result ?? [];
    }

    public Task<UploadFilePermissionRequestResponse?> SubmitPermissionRequestAsync(Guid fileId, SubmitPermissionRequestRequest request, CancellationToken token = default)
        => PostAsync<SubmitPermissionRequestRequest, UploadFilePermissionRequestResponse>($"/api/upload-files/{fileId}/permission-requests", request, token);

    public Task<UploadFilePermissionRequestResponse?> ReviewPermissionRequestAsync(Guid requestId, ReviewPermissionRequestRequest request, CancellationToken token = default)
        => PutAsync<ReviewPermissionRequestRequest, UploadFilePermissionRequestResponse>($"/api/upload-file-permission-requests/{requestId}/review", request, token);

    public Task<WebApiTokenResponse?> ImpersonateAsync(Guid targetUserId, CancellationToken token = default)
        => PostAsync<object, WebApiTokenResponse>($"/api/admin/impersonate/{targetUserId}", new { }, token);

    // ── Entra registration and account linking ───────────────────────────────

    public Task<EntraRegisterResponse?> EntraRegisterAsync(EntraRegisterPayload request, CancellationToken token = default)
        => PostAsync<EntraRegisterPayload, EntraRegisterResponse>("/api/auth/entra/register", request, token);

    public async Task<bool> EntraLinkAsync(EntraLinkPayload request, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, "/api/auth/entra/link");
        req.Content = JsonContent.Create(request);
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
    }
}
