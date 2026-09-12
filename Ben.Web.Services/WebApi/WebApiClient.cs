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

    /// <inheritdoc />
    public async Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken token = default)
        => (await GetItemAsync<TResponse>(relativeUrl, token)).Item;

    /// <inheritdoc />
    public Task<ItemResult<TResponse>> GetItemAsync<TResponse>(string relativeUrl, CancellationToken token = default)
        => SendItemAsync<TResponse>(Auth(HttpMethod.Get, relativeUrl), token);

    /// <inheritdoc />
    public Task<ItemResult<TResponse>> GetAnonymousItemAsync<TResponse>(string relativeUrl, CancellationToken token = default)
        => SendItemAsync<TResponse>(new HttpRequestMessage(HttpMethod.Get, relativeUrl), token);

    /// <summary>
    /// The body every single-object GET shares — the counterpart to <see cref="SendListAsync{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>The two untyped entry points above it (<c>GetAsync</c>, <c>GetAnonymousAsync</c>) are
    /// now thin wrappers that drop the outcome and keep the value. That is on purpose: it makes the
    /// ~90 existing call sites behave exactly as they did, while every one of them silently gains
    /// the <c>HttpRequestException</c> catch they never had. An unreachable API used to throw
    /// straight out of <c>OnInitializedAsync</c> and kill the circuit; the list path has caught this
    /// since item 120 and the object path never did.</para>
    /// </remarks>
    private Task<ItemResult<T>> SendItemAsync<T>(HttpRequestMessage request, CancellationToken token)
        => ApiResponseMapper.ReadItemAsync<T>(_httpClient, request, token);

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
    private Task<LoadResult<T>> SendListAsync<T>(HttpRequestMessage request, CancellationToken token)
        => ApiResponseMapper.ReadListAsync<T>(_httpClient, request, token);

    /// <inheritdoc />
    public async Task<TResponse?> GetAnonymousAsync<TResponse>(string relativeUrl, CancellationToken token = default)
        => (await GetAnonymousItemAsync<TResponse>(relativeUrl, token)).Item;

    /// <inheritdoc />
    public async Task<TResponse?> PostAnonymousReadingBodyAsync<TRequest, TResponse>(
        string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(payload) };
        using var response = await _httpClient.SendAsync(req, token);

        try
        {
            return await BodyOrDefaultAsync<TResponse>(response, token);
        }
        catch (Exception)
        {
            // A 500 or a proxy error page is not the typed body this expects. Null leaves the
            // caller to show its own generic message, which is the right outcome for a failure
            // the server did not describe.
            return default;
        }
    }

    /// <summary>
    /// A success response's body, or <c>default</c> when there is none.
    /// </summary>
    /// <remarks>
    /// <para>A 204 carries no body and <c>ReadFromJsonAsync</c> THROWS on an empty stream, so every
    /// void endpoint (<c>return NoContent()</c>) — and every <c>Ok(null)</c>, which the framework
    /// turns into a 204 — reaches this. Three methods already knew; four did not, and the rule was
    /// being restated rather than shared.</para>
    ///
    /// <para>What the fourth one cost: <c>CompleteMyOnboardingAsync</c> posts to a 204 endpoint, the
    /// throw was swallowed by the page's own catch, and the first-run wizard was never stamped as
    /// answered. Every account created on the live site was therefore asked to set itself up again
    /// on every visit, and no amount of clicking Skip could stop it. Found on 2026-09-09 while
    /// checking what App Review's demo account would see.</para>
    /// </remarks>
    private static async Task<TResponse?> BodyOrDefaultAsync<TResponse>(
        HttpResponseMessage response, CancellationToken token)
        => response.StatusCode == System.Net.HttpStatusCode.NoContent
           || response.Content.Headers.ContentLength == 0
            ? default
            : await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token);

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(HttpMethod.Post, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await BodyOrDefaultAsync<TResponse>(response, token);
    }

    /// <inheritdoc />
    public async Task<(TResponse? Result, string? Error)> SendExpectingReasonAsync<TRequest, TResponse>(
        HttpMethod method, string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(method, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode)
        {
            // A 204 carries no body, and ReadFromJsonAsync THROWS on an empty stream. Because
            // nothing between here and the button catches it, that exception escaped the page's
            // click handler and left the busy flag set — the Change-password panel sat on
            // "Saving…" for ever while the password had in fact been changed. Every void endpoint
            // (`return NoContent()`) reaches this line, so the guard belongs here and not in the
            // dozen call sites. SendItemAsync already learned this; see the note there.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
                return (default, null);

            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), null);
        }

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
        {
            // Same empty-body trap as SendExpectingReasonAsync above.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
                return (default, default);

            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), default);
        }

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

    /// <inheritdoc />
    public async Task<(TResponse? Result, string? Error, TConflict? Conflict)> SendExpectingConflictAsync<TRequest, TResponse, TConflict>(
        HttpMethod method, string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = Auth(method, relativeUrl);
        req.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(req, token);

        if (response.IsSuccessStatusCode)
        {
            // Same empty-body trap as SendExpectingReasonAsync above.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
                return (default, null, default);

            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), null, default);
        }

        // Read once, as text. Trying the JSON reader first and the string reader second would read
        // the same stream twice, and the second read of an unbuffered response is empty.
        var body = await response.Content.ReadAsStringAsync(token);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Our own 409 carries the typed shape. One from a proxy or a framework filter does not,
            // and must not crash the page — it falls through to the prose test below, where an
            // HTML page is dropped and a plain sentence is kept.
            try
            {
                var conflict = System.Text.Json.JsonSerializer.Deserialize<TConflict>(body, WebJson);
                if (conflict is not null) return (default, null, conflict);
            }
            catch (System.Text.Json.JsonException) { }
        }

        // The same prose test as SendExpectingReasonAsync: a refusal we wrote is a sentence, a
        // framework error is a ProblemDetails blob or an HTML page.
        var looksLikeProse = !string.IsNullOrWhiteSpace(body)
                          && body.Length < 400
                          && !body.TrimStart().StartsWith('{')
                          && !body.TrimStart().StartsWith('<');

        return (default, looksLikeProse ? body.Trim('"', ' ', '\n') : null, default);
    }

    /// <summary>What <c>ReadFromJsonAsync</c> uses when nothing is passed: the web defaults.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions WebJson
        = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<TResponse?> PostAnonymousAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = JsonContent.Create(payload) };
        using var response = await _httpClient.SendAsync(req, token);
        if (!response.IsSuccessStatusCode) return default;
        return await BodyOrDefaultAsync<TResponse>(response, token);
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
        return await BodyOrDefaultAsync<TResponse>(response, token);
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
        {
            // Same empty-body trap as SendExpectingReasonAsync: an upload endpoint that answers
            // NoContent would otherwise throw here, inside whatever handler started the upload.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
                return (default, null);

            return (await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: token), null);
        }

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

    // ── Upload File — delete-and-reassign (item 180 Phase B) ────────────────────
    public Task<FileUsageRecord?> GetUploadFileUsageAsync(Guid id, CancellationToken token = default)
        => GetAsync<FileUsageRecord>($"/api/upload-files/{id}/usage", token);

    public Task<DeleteEverywhereResult?> DeleteUploadFileEverywhereAsync(Guid id, CancellationToken token = default)
        => PostAsync<object, DeleteEverywhereResult>($"/api/upload-files/{id}/delete-everywhere", new { }, token);

    public Task<UploadFileRecord?> ReassignUploadFileAsync(Guid id, Guid organizationId, CancellationToken token = default)
        => PostAsync<ReassignUploadFileRequest, UploadFileRecord>($"/api/upload-files/{id}/reassign", new ReassignUploadFileRequest(organizationId), token);

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

    /// <inheritdoc />
    public Task<(UploadFileAudioConfigRecord? Result, string? Error)> UpsertAudioConfigWithReasonAsync(
        Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default)
        => SendExpectingReasonAsync<UpsertAudioConfigRequest, UploadFileAudioConfigRecord>(
            HttpMethod.Put, $"/api/upload-files/{fileId}/audio-config", request, token);

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

    /// <inheritdoc />
    public Task<(UploadFileRecord? Result, string? Error)> ClipAudioWithReasonAsync(
        Guid fileId, ClipAudioRequest request, CancellationToken token = default)
        => SendExpectingReasonAsync<ClipAudioRequest, UploadFileRecord>(
            HttpMethod.Post, $"/api/upload-files/{fileId}/clip", request, token);

    // ── Audio Edit (destructive) ─────────────────────────────────────────────
    public Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default)
        => PostAsync<AudioEditRequest, UploadFileRecord>($"/api/upload-files/{fileId}/audio-edit", request, token);

    /// <inheritdoc />
    public Task<(UploadFileRecord? Result, string? Error)> EditAudioWithReasonAsync(
        Guid fileId, AudioEditRequest request, CancellationToken token = default)
        => SendExpectingReasonAsync<AudioEditRequest, UploadFileRecord>(
            HttpMethod.Post, $"/api/upload-files/{fileId}/audio-edit", request, token);

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

    public async Task<EntraLinkOutcome> EntraLinkAsync(string entraAccessToken, EntraLinkPayload request, CancellationToken token = default)
    {
        using var req = EntraAuth(HttpMethod.Post, "/api/auth/entra/link", entraAccessToken);
        req.Content = JsonContent.Create(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(req, token);
        }
        catch (HttpRequestException)
        {
            return new EntraLinkOutcome(false, Message: "Couldn't reach the server. Nothing was linked.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return EntraLinkOutcome.Ok;

            // A 401 is /login's problem-detail - the SAME four words the Apple link and the password
            // form use - so the failure mapping is shared rather than copied. A 409 still carries a
            // sentence of its own.
            var body = await response.Content.ReadAsStringAsync(token);
            var detail = ReadJsonString(body, "detail");
            if (detail is not null)
            {
                var failure = LoginFailureMapping.From(new LoginAttempt(null, (int)response.StatusCode, detail));
                return new EntraLinkOutcome(false, failure == LoginFailure.RequiresTwoFactor, failure switch
                {
                    LoginFailure.RequiresTwoFactor => "That account uses two-step verification. Enter the code from your authenticator app.",
                    LoginFailure.EmailNotConfirmed => "That account's email address hasn't been confirmed yet. Use the link we sent, or ask for another.",
                    LoginFailure.LockedOut => "That account is locked after too many attempts. Waiting is the only thing that helps.",
                    LoginFailure.InvalidCredentials => "Invalid email or password.",
                    _ => "That sign-in was refused without a reason. Try again - and if it keeps happening, it isn't your password.",
                });
            }

            return new EntraLinkOutcome(false, false, ReadJsonString(body, "message"));
        }
    }

    /// <summary>One string-valued property out of a small JSON body, or null. Never throws.</summary>
    private static string? ReadJsonString(string body, string property)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
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
