using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;

namespace Ben.Web.Services.WebApi;

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

        // Ok(null) from a controller becomes 204 WITH AN EMPTY BODY (HttpNoContentOutputFormatter),
        // and ReadFromJsonAsync throws on an empty stream. That exception surfaced inside a page's
        // OnInitializedAsync and killed the circuit — the Price Bands screen died on production
        // precisely when the price list was HEALTHY, because healthy is when the endpoint answers
        // "nothing to report". Null is the honest reading of an empty success either way.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent
            || response.Content.Headers.ContentLength == 0)
            return default;

        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    /// <inheritdoc />
    public Task<LoadResult<T>> GetListAsync<T>(string relativeUrl, CancellationToken token = default)
        => SendListAsync<T>(Auth(HttpMethod.Get, relativeUrl), token);

    /// <inheritdoc />
    public Task<LoadResult<T>> GetAnonymousListAsync<T>(string relativeUrl, CancellationToken token = default)
        => SendListAsync<T>(new HttpRequestMessage(HttpMethod.Get, relativeUrl), token);

    /// <summary>
    /// The body both list fetches share. One implementation on purpose: the authenticated and
    /// anonymous paths differ by a single header, and the whole value of <see cref="LoadResult{T}"/>
    /// is that failure is reported identically wherever it happens.
    /// </summary>
    /// <remarks>
    /// Anonymous surfaces need this as much as signed-in ones. A public group page whose fetch is
    /// refused shows a visitor an organisation with nothing in it, and the visitor has no account,
    /// no error and no reason to try again — the one audience least able to tell a broken page from
    /// an empty one.
    /// </remarks>
    private async Task<LoadResult<T>> SendListAsync<T>(HttpRequestMessage request, CancellationToken token)
    {
        using var req = request;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(req, token);
        }
        catch (HttpRequestException)
        {
            // The API is unreachable. Emphatically not "there is nothing here" — this is the case
            // that used to render as an empty group.
            return LoadResult<T>.Failure();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 before anything else. A dead token is not a broken list: the page should say
                // the session ended and offer a way back, not "couldn't load this — try again",
                // which invites a retry that is certain to fail the same way. Item 133.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return LoadResult<T>.SessionEnded();

                var body = await response.Content.ReadAsStringAsync(token);

                // Same prose test as SendExpectingReasonAsync: a refusal we wrote is a sentence,
                // a framework error is a ProblemDetails blob or an HTML page, and showing either
                // to a person is worse than saying nothing useful.
                var looksLikeProse = !string.IsNullOrWhiteSpace(body)
                                  && body.Length < 400
                                  && !body.TrimStart().StartsWith('{')
                                  && !body.TrimStart().StartsWith('<');

                // Prose when the server wrote a sentence; otherwise the status itself, which is
                // the single most useful thing a person debugging a deployment can be told. A
                // blank page says nothing; "the server answered 404" says the path is wrong and
                // "403" says the path is right and the caller is not allowed. That distinction
                // cost a day of guessing on the ishaunted.com deploy (item 126).
                return LoadResult<T>.Failure(
                    looksLikeProse
                        ? body.Trim('"', ' ', '\n')
                        : $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var items = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: token);
            return LoadResult<T>.Ok(items);
        }
    }

    public async Task<TResponse?> GetAnonymousAsync<TResponse>(string relativeUrl, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;

        // Same empty-success guard as GetAsync — see the comment there.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent
            || response.Content.Headers.ContentLength == 0)
            return default;

        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    /// <inheritdoc />
    public async Task<TResponse?> PostAnonymousReadingBodyAsync<TRequest, TResponse>(
        string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(payload) };
        using var response = await _httpClient.SendAsync(req, token);

        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
        }
        catch (Exception)
        {
            // A 500 or a proxy error page is not the typed body this expects. Null leaves the
            // caller to show its own generic message, which is the right outcome for a failure
            // the server did not describe.
            return default;
        }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    /// <inheritdoc />
    public async Task<(TResponse? Result, string? Error)> SendExpectingReasonAsync<TRequest, TResponse>(
        HttpMethod method, string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(method, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), null);

        var body = await response.Content.ReadAsStringAsync(token);

        // A refusal we wrote is a plain sentence; a framework error is a ProblemDetails blob or an
        // HTML page. Showing either to a person is worse than saying nothing useful, so anything
        // that does not look like prose is dropped.
        var looksLikeProse = !string.IsNullOrWhiteSpace(body)
                          && body.Length < 400
                          && !body.TrimStart().StartsWith('{')
                          && !body.TrimStart().StartsWith('<');

        return (default, looksLikeProse ? body.Trim('"', ' ', '\n') : null);
    }

    /// <inheritdoc />
    public async Task<(TResponse? Result, TConflict? Conflict)> PostExpectingConflictAsync<TRequest, TResponse, TConflict>(
        string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), default);

        if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
            return (default, default);

        // A 409 from one of these endpoints carries our own shape, but a proxy or a framework filter
        // can produce one too — so a body that will not deserialize is an ordinary failure, not a
        // crash.
        try
        {
            return (default, await response.Content.ReadFromJsonAsync<TConflict>(cancellationToken: token));
        }
        catch (System.Text.Json.JsonException)
        {
            return (default, default);
        }
    }

    public async Task<TResponse?> PostAnonymousAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(payload) };
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    public async Task<bool> PostAnonymousVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(payload) };
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
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

    /// <inheritdoc />
    public async Task<(bool Deleted, string? Error)> DeleteExpectingReasonAsync(
        string relativeUrl, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Delete, relativeUrl);
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode) return (true, null);

        var body = await response.Content.ReadAsStringAsync(token);

        // Same prose test as SendExpectingReasonAsync: a refusal we wrote is a sentence, a
        // framework error is a ProblemDetails blob or an HTML page, and showing either to a
        // person is worse than saying nothing useful.
        var looksLikeProse = !string.IsNullOrWhiteSpace(body)
                          && body.Length < 400
                          && !body.TrimStart().StartsWith('{')
                          && !body.TrimStart().StartsWith('<');

        return (false, looksLikeProse ? body.Trim('"', ' ', '\n') : null);
    }

    public async Task<bool> PutVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Put, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> PostVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
    }

    public Task<LoadResult<AppUserRecord>> GetUsersAsync(CancellationToken token = default)
        => GetListAsync<AppUserRecord>("/api/app-users", token);

    public Task<LoadResult<OrganizationSummaryResponse>> GetMyOrganizationsAsync(CancellationToken token = default)
        => GetListAsync<OrganizationSummaryResponse>("/api/security/organizations/mine", token);

    public Task<LoadResult<UserSearchResultResponse>> SearchUsersAsync(string? query, int skip = 0, int take = 25, CancellationToken token = default)
    {        var encodedQuery = Uri.EscapeDataString(query ?? string.Empty);
        var relativeUrl = $"/api/security/organizations/users/search?q={encodedQuery}&skip={skip}&take={take}";
        return GetListAsync<UserSearchResultResponse>(relativeUrl, token);
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

    /// <summary>
    /// The group's roster — who belongs and in what role.
    /// </summary>
    /// <remarks>
    /// <para><b>Reads <c>/roster</c>, not <c>/security/users</c>.</b> They return the same shape,
    /// but the security one is the endpoint behind *managing* access and requires Owner or
    /// Administrator. Every caller of this method wants the list — the Members tab, the case and
    /// investigation team pickers, the role editor — and only the last of those is an
    /// administrator's screen.</para>
    ///
    /// <para>Pointed at the manage endpoint, an ordinary member's own roster came back refused,
    /// and the <c>?? []</c> below turned that into "this group has no members". Item 109.</para>
    /// </remarks>
    public Task<LoadResult<OrganizationUserMembershipResponse>> GetOrganizationUsersAsync(Guid organizationId, CancellationToken token = default)
    {
        var relativeUrl = $"/api/organizations/{organizationId}/roster";
        return GetListAsync<OrganizationUserMembershipResponse>(relativeUrl, token);
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

    public Task<LoadResult<OrgUserDirectoryEntryResponse>> GetOrgUserDirectoryAsync(Guid organizationId, CancellationToken token = default)
        => GetListAsync<OrgUserDirectoryEntryResponse>($"/api/organizations/{organizationId}/user-directory", token);

    // ── Upload File Types ────────────────────────────────────────────────────
    public Task<LoadResult<UploadFileTypeRecord>> GetUploadFileTypesAsync(CancellationToken token = default)
        => GetListAsync<UploadFileTypeRecord>("/api/upload-file-types", token);

    // ── Upload Files ─────────────────────────────────────────────────────────
    public Task<LoadResult<UploadFileRecord>> GetUploadFilesAsync(CancellationToken token = default)
        => GetListAsync<UploadFileRecord>("/api/upload-files", token);

    public async Task<UploadFileRecord?> UploadFileAsync(MultipartFormDataContent content, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, "/api/upload-files");
        req.Content = content;
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UploadFileRecord>(cancellationToken: token);
    }

    public async Task<(ChunkedUploadSessionRecord? Session, string? Error)> StartChunkedUploadAsync(
        StartChunkedUploadRequest request, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, "/api/chunked-uploads");
        req.Content = System.Net.Http.Json.JsonContent.Create(request);
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ChunkedUploadSessionRecord>(cancellationToken: token), null);

        // The server refuses in sentences — a size limit that names the number, an extension
        // policy that names the type. Keep them; a null here degrades to a generic failure.
        var body = await response.Content.ReadAsStringAsync(token);
        var looksLikeProse = !string.IsNullOrWhiteSpace(body)
                          && body.Length < 400
                          && !body.TrimStart().StartsWith('{')
                          && !body.TrimStart().StartsWith('<');
        return (null, looksLikeProse ? body.Trim('"', ' ', '\n') : null);
    }

    public async Task<TResponse?> PostMultipartAsync<TResponse>(string relativeUrl, MultipartFormDataContent content, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = content;
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);
    }

    /// <summary>
    /// Multipart upload that keeps the server's refusal sentence — the item-84 read-only refusal
    /// arrives on file uploads too, and a null that discards "your subscription has ended" leaves
    /// somebody staring at a generic failure while the real answer was one sentence long.
    /// </summary>
    public async Task<(TResponse? Result, string? Error)> PostMultipartExpectingReasonAsync<TResponse>(
        string relativeUrl, MultipartFormDataContent content, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = content;
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), null);

        var body = await response.Content.ReadAsStringAsync(token);
        var looksLikeProse = !string.IsNullOrWhiteSpace(body)
                          && body.Length < 400
                          && !body.TrimStart().StartsWith('{')
                          && !body.TrimStart().StartsWith('<');

        return (default, looksLikeProse ? body.Trim('"', ' ', '\n') : null);
    }

    public Task<UploadFileRecord?> UpdateUploadFileAsync(Guid id, UpdateUploadFileRequest request, CancellationToken token = default)
        => PutAsync<UpdateUploadFileRequest, UploadFileRecord>($"/api/upload-files/{id}", request, token);

    public Task<bool> DeleteUploadFileAsync(Guid id, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{id}", token);

    // ── Upload File — Replace (item #6 phase 3) ─────────────────────────────────
    public Task<UploadFileRecord?> ReplaceUploadFileAsync(Guid id, MultipartFormDataContent content, CancellationToken token = default)
        => PostMultipartAsync<UploadFileRecord>($"/api/upload-files/{id}/replace", content, token);

    public Task<ReplaceImpactRecord?> GetReplaceImpactAsync(Guid id, CancellationToken token = default)
        => GetAsync<ReplaceImpactRecord>($"/api/upload-files/{id}/replace-impact", token);

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
    public Task<LoadResult<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default)
        => GetListAsync<UploadFileRegionNoteRecord>($"/api/upload-files/{fileId}/region-notes", token);

    public Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default)
        => PostAsync<CreateRegionNoteRequest, UploadFileRegionNoteRecord>($"/api/upload-files/{fileId}/region-notes", request, token);

    public Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default)
        => PutAsync<UpdateRegionNoteRequest, UploadFileRegionNoteRecord>($"/api/upload-files/{fileId}/region-notes/{noteId}", request, token);

    public Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{fileId}/region-notes/{noteId}", token);

    // ── File Comments (item #6 phase 2) ───────────────────────────────────────
    public Task<LoadResult<UploadFileCommentRecord>> GetFileCommentsAsync(Guid fileId, CancellationToken token = default)
        => GetListAsync<UploadFileCommentRecord>($"/api/upload-files/{fileId}/comments", token);

    public Task<UploadFileCommentRecord?> CreateFileCommentAsync(Guid fileId, CreateFileCommentRequest request, CancellationToken token = default)
        => PostAsync<CreateFileCommentRequest, UploadFileCommentRecord>($"/api/upload-files/{fileId}/comments", request, token);

    public Task<UploadFileCommentRecord?> UpdateFileCommentAsync(Guid fileId, Guid commentId, UpdateFileCommentRequest request, CancellationToken token = default)
        => PutAsync<UpdateFileCommentRequest, UploadFileCommentRecord>($"/api/upload-files/{fileId}/comments/{commentId}", request, token);

    public Task<bool> DeleteFileCommentAsync(Guid fileId, Guid commentId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{fileId}/comments/{commentId}", token);

    public Task<FileCommentSettingsRecord?> GetFileCommentSettingsAsync(Guid fileId, CancellationToken token = default)
        => GetAsync<FileCommentSettingsRecord>($"/api/upload-files/{fileId}/comments/settings", token);

    public Task<FileCommentSettingsRecord?> UpdateFileCommentSettingsAsync(Guid fileId, FileCommentSettingsRecord request, CancellationToken token = default)
        => PutAsync<FileCommentSettingsRecord, FileCommentSettingsRecord>($"/api/upload-files/{fileId}/comments/settings", request, token);

    // ── Audio Markers (EVP) ──────────────────────────────────────────────────
    public Task<LoadResult<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default)
        => GetListAsync<AudioMarkerRecord>($"/api/upload-files/{fileId}/audio-markers", token);

    public Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default)
        => PostAsync<CreateAudioMarkerRequest, AudioMarkerRecord>($"/api/upload-files/{fileId}/audio-markers", request, token);

    public Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default)
        => PutAsync<UpdateAudioMarkerRequest, AudioMarkerRecord>($"/api/upload-files/{fileId}/audio-markers/{markerId}", request, token);

    public Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-files/{fileId}/audio-markers/{markerId}", token);

    public async Task<IReadOnlyList<AudioMarkerRecord>?> ReplaceAudioCandidatesAsync(Guid fileId, BulkCreateAudioCandidatesRequest request, CancellationToken token = default)
    {
        var result = await PostAsync<BulkCreateAudioCandidatesRequest, List<AudioMarkerRecord>>(
            $"/api/upload-files/{fileId}/audio-markers/candidates", request, token);

        // Null means the replace did not happen. Returning an empty list would say the file now
        // has no candidates, which is a claim about the recording rather than about the request.
        return result is null ? null : (IReadOnlyList<AudioMarkerRecord>)result;
    }

    public Task<AudioMarkerRecord?> ReviewAudioMarkerAsync(Guid fileId, Guid markerId, ReviewAudioMarkerRequest request, CancellationToken token = default)
        => PutAsync<ReviewAudioMarkerRequest, AudioMarkerRecord>(
            $"/api/upload-files/{fileId}/audio-markers/{markerId}/review", request, token);

    /// <summary>
    /// Runs the EVP detector over a file and returns what it marked, or null if it did not run.
    /// </summary>
    /// <remarks>
    /// <b>Null, not an empty list.</b> "The scan found nothing" and "the scan did not happen" are
    /// different answers, and on this site the first one is a finding somebody may act on — it is
    /// the whole point of the feature. Handing back an empty list on a refused or failed request
    /// reports a clean recording that was never examined.
    /// </remarks>
    public async Task<IReadOnlyList<AudioMarkerRecord>?> ScanAudioForEvpAsync(Guid fileId, EvpSensitivity sensitivity, EvpDetectionOptions? options = null, CancellationToken token = default)
    {
        var result = await PostAsync<EvpDetectionOptions?, List<AudioMarkerRecord>>(
            $"/api/upload-files/{fileId}/audio-markers/scan?sensitivity={sensitivity}", options, token);

        return result;
    }

    // ── Audio Clip ────────────────────────────────────────────────────────────
    public Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default)
        => PostAsync<ClipAudioRequest, UploadFileRecord>($"/api/upload-files/{fileId}/clip", request, token);

    // ── Audio Edit (destructive) ─────────────────────────────────────────────
    public Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default)
        => PostAsync<AudioEditRequest, UploadFileRecord>($"/api/upload-files/{fileId}/audio-edit", request, token);

    public Task<LoadResult<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default)
        => GetListAsync<UploadFileRecord>($"/api/upload-files/{fileId}/clips", token);

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
    public Task<LoadResult<UploadFileOrgShareResponse>> GetFileOrgSharesAsync(Guid fileId, CancellationToken token = default)
        => GetListAsync<UploadFileOrgShareResponse>($"/api/upload-files/{fileId}/shares", token);

    public Task<LoadResult<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default)
        => GetListAsync<UploadFileRecord>($"/api/upload-files/org/{orgId}", token);

    public Task<UploadFileOrgShareResponse?> ShareFileWithOrgAsync(Guid fileId, ShareFileWithOrgRequest request, CancellationToken token = default)
        => PostAsync<ShareFileWithOrgRequest, UploadFileOrgShareResponse>($"/api/upload-files/{fileId}/shares", request, token);

    public Task<UploadFileOrgShareResponse?> UpdateOrgShareVisibilityAsync(Guid shareId, UpdateOrgShareVisibilityRequest request, CancellationToken token = default)
        => PutAsync<UpdateOrgShareVisibilityRequest, UploadFileOrgShareResponse>($"/api/upload-file-shares/{shareId}/visibility", request, token);

    public Task<bool> RemoveOrgShareAsync(Guid shareId, CancellationToken token = default)
        => DeleteAsync($"/api/upload-file-shares/{shareId}", token);

    // ── Permission Requests ──────────────────────────────────────────────────
    public Task<LoadResult<UploadFilePermissionRequestResponse>> GetFilePermissionRequestsAsync(Guid fileId, CancellationToken token = default)
        => GetListAsync<UploadFilePermissionRequestResponse>($"/api/upload-files/{fileId}/permission-requests", token);

    public Task<LoadResult<UploadFilePermissionRequestResponse>> GetPendingPermissionRequestsForReviewerAsync(Guid reviewerUserId, CancellationToken token = default)
        => GetListAsync<UploadFilePermissionRequestResponse>($"/api/upload-file-permission-requests/pending-for/{reviewerUserId}", token);

    public Task<UploadFilePermissionRequestResponse?> SubmitPermissionRequestAsync(Guid fileId, SubmitPermissionRequestRequest request, CancellationToken token = default)
        => PostAsync<SubmitPermissionRequestRequest, UploadFilePermissionRequestResponse>($"/api/upload-files/{fileId}/permission-requests", request, token);

    public Task<UploadFilePermissionRequestResponse?> ReviewPermissionRequestAsync(Guid requestId, ReviewPermissionRequestRequest request, CancellationToken token = default)
        => PutAsync<ReviewPermissionRequestRequest, UploadFilePermissionRequestResponse>($"/api/upload-file-permission-requests/{requestId}/review", request, token);

    public Task<WebApiTokenResponse?> ImpersonateAsync(Guid targetUserId, CancellationToken token = default)
        => PostAsync<object, WebApiTokenResponse>($"/api/admin/impersonate/{targetUserId}", new { }, token);

    // ── Entra registration and account linking ───────────────────────────────
    // Both send the caller-supplied Entra access token explicitly rather than via Auth()/
    // TokenStore — see IWebApiClient's doc comment on these two methods.

    public async Task<EntraRegisterResponse?> EntraRegisterAsync(string entraAccessToken, EntraRegisterPayload request, CancellationToken token = default)
    {
        using var req = EntraAuth(HttpMethod.Post, "/api/auth/entra/register", entraAccessToken);
        req.Content = JsonContent.Create(request);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<EntraRegisterResponse>(cancellationToken: token);
    }

    public async Task<bool> EntraLinkAsync(string entraAccessToken, EntraLinkPayload request, CancellationToken token = default)
    {
        using var req = EntraAuth(HttpMethod.Post, "/api/auth/entra/link", entraAccessToken);
        req.Content = JsonContent.Create(request);
        using var response = await _httpClient.SendAsync(req, token);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Like <see cref="Auth"/>, but attaches an explicitly-supplied bearer token instead
    /// of reading <see cref="_tokenStore"/> — used only for the two Entra actions above, where the
    /// caller must present the Entra access token specifically, which may not be what's currently
    /// sitting in the token store (e.g. after a local sign-in has since overwritten it).</summary>
    private static HttpRequestMessage EntraAuth(HttpMethod method, string url, string entraAccessToken)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(entraAccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entraAccessToken);
        return req;
    }

    // ── Sub-client invite accept flow (item #4) ───────────────────────────────
    public Task<InviteInfoRecord?> GetInviteInfoAsync(string token, CancellationToken cancellationToken = default)
        => GetAnonymousAsync<InviteInfoRecord>($"/api/case-invites/{Uri.EscapeDataString(token)}", cancellationToken);

    public Task<AcceptInviteResult?> AcceptInviteAsync(string token, AcceptInviteRequest request, CancellationToken cancellationToken = default)
        => PostAnonymousAsync<AcceptInviteRequest, AcceptInviteResult>($"/api/case-invites/{Uri.EscapeDataString(token)}/accept", request, cancellationToken);

    public Task<AcceptInviteResult?> AcceptInviteExistingAsync(string token, CancellationToken cancellationToken = default)
        => PostAsync<object, AcceptInviteResult>($"/api/case-invites/{Uri.EscapeDataString(token)}/accept-existing", new { }, cancellationToken);
}
