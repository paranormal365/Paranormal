using Ben.Service.Models.People;
using Ben.Service.Models.Entities;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services.WebApi;

public interface IWebApiClient
{
    /// <summary>
    /// Fetches one object, answering <c>null</c> for every kind of failure.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="GetItemAsync{TResponse}"/> on any surface a person looks at. This overload
    /// cannot tell a 401 from a 404 from an empty success, which is precisely the confusion item 120
    /// removed from lists; it stays because ~90 call sites use it and converting them is a migration,
    /// not an edit. It is no longer able to kill a circuit, though — see <c>SendItemAsync</c>.
    /// </remarks>
    Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken token = default);

    /// <summary>The anonymous counterpart to <see cref="GetAsync{TResponse}"/>, with the same caveat.</summary>
    Task<TResponse?> GetAnonymousAsync<TResponse>(string relativeUrl, CancellationToken token = default);

    /// <summary>
    /// Fetches one object and reports what actually happened.
    /// </summary>
    /// <remarks>
    /// <para>The counterpart to <see cref="GetListAsync{T}"/> for endpoints that return an object.
    /// <see cref="GetAsync{TResponse}"/> answers any refusal — 401, 403, 404, 500, an unreachable
    /// API — with the same <c>null</c> a genuinely absent record produces, so the page is left
    /// guessing at a sentence to show. <see cref="ItemResult{T}"/> carries the difference.</para>
    /// </remarks>
    Task<ItemResult<TResponse>> GetItemAsync<TResponse>(string relativeUrl, CancellationToken token = default);

    /// <summary>The same as <see cref="GetItemAsync{TResponse}"/> for an endpoint that takes no bearer token.</summary>
    /// <remarks>
    /// Anonymous surfaces need this as much as signed-in ones — a public page whose fetch was
    /// refused shows a visitor nothing, and that visitor has no account and no reason to retry.
    /// </remarks>
    Task<ItemResult<TResponse>> GetAnonymousItemAsync<TResponse>(string relativeUrl, CancellationToken token = default);
    Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<TResponse?> PostAnonymousAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default);

    /// <summary>
    /// Anonymous POST for endpoints that return 204. Reading a body here would fail on an empty
    /// response, which is what <c>PostAnonymousAsync</c> would do.
    /// </summary>
    Task<bool> PostAnonymousVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default);

    /// <summary>
    /// Anonymous POST that returns the body <b>whatever the status</b>.
    /// </summary>
    /// <remarks>
    /// For endpoints whose refusal is the answer rather than an error — sign-up, where "that name
    /// is taken" arrives as a 400 carrying a typed result with the message and the field to point
    /// at. <c>PostAnonymousAsync</c> would turn all of that into <c>null</c>, and the form would
    /// have nothing to say but "something went wrong".
    /// </remarks>
    Task<TResponse?> PostAnonymousReadingBodyAsync<TRequest, TResponse>(
        string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<TResponse?> PostMultipartAsync<TResponse>(string relativeUrl, MultipartFormDataContent content, CancellationToken token = default);

    /// <summary>Multipart upload that keeps the server's refusal sentence — see the implementation.</summary>
    Task<(TResponse? Result, string? Error)> PostMultipartExpectingReasonAsync<TResponse>(
        string relativeUrl, MultipartFormDataContent content, CancellationToken token = default);
    Task<TResponse?> PutAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default);

    /// <summary>
    /// Sends, and returns either the result or <b>the server's own refusal message</b>.
    /// </summary>
    /// <remarks>
    /// The ordinary Post/Put swallow a non-2xx into <c>null</c>, which leaves the caller with
    /// nothing to say but "Save failed." That is fine for a network blip and useless for a rule —
    /// an organizer told a public event cannot be at a private residence can fix it; one told
    /// "Save failed" cannot. Use this wherever the endpoint refuses for a reason worth reading.
    /// </remarks>
    Task<(TResponse? Result, string? Error)> SendExpectingReasonAsync<TRequest, TResponse>(
        HttpMethod method, string relativeUrl, TRequest payload, CancellationToken token = default);

    /// <summary>
    /// Posts, and returns either the result or <b>a typed 409 body</b> the caller can act on.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SendExpectingReasonAsync"/>, which recovers a sentence to display.
    /// This recovers a structure to build a choice from — the taxonomy endpoints answer a probable
    /// typo with the names it might have been, and a "did you mean" prompt needs the names, not a
    /// paragraph about them. Any other status is an ordinary failure and comes back as two nulls.
    /// </remarks>
    Task<(TResponse? Result, TConflict? Conflict)> PostExpectingConflictAsync<TRequest, TResponse, TConflict>(
        string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<bool> PutVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<bool> PostVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default);
    /// <summary>
    /// Fetches a list, distinguishing "the server said no" from "there is nothing here".
    /// </summary>
    /// <remarks>
    /// <para>The counterpart to <c>GetAsync</c> for list endpoints. <c>GetAsync</c> answers any
    /// non-2xx with <c>default</c>, which the adapters turn into an empty list — so a 403 renders
    /// as "No records available" and the page tells somebody their group is empty when in fact
    /// they were refused. Item 120, and the shared cause of three bugs on 2026-08-20.</para>
    ///
    /// <para>Prefer this for any list a person could be refused. <see cref="LoadResult{T}.Items"/>
    /// is always safe to enumerate, so adopting it never makes a call site worse; rendering the
    /// difference is a separate, opt-in step.</para>
    /// </remarks>
    Task<LoadResult<T>> GetListAsync<T>(string relativeUrl, CancellationToken token = default);

    /// <summary>
    /// The same as <see cref="GetListAsync{T}"/> for an endpoint that takes no bearer token.
    /// </summary>
    /// <remarks>
    /// Public pages need to distinguish "refused" from "empty" more than signed-in ones do, not
    /// less: a visitor who sees an empty group has no account, no error and no way to tell the
    /// difference.
    /// </remarks>
    Task<LoadResult<T>> GetAnonymousListAsync<T>(string relativeUrl, CancellationToken token = default);

    Task<bool> DeleteAsync(string relativeUrl, CancellationToken token = default);

    /// <summary>
    /// Deletes, and recovers the server's sentence when it refuses.
    /// </summary>
    /// <remarks>
    /// The same argument as <see cref="SendExpectingReasonAsync"/>, for the verb that has no body
    /// to send. A delete refused because the thing still has posts in it is a rule the person can
    /// act on — "Couldn't delete that" is not, and a guard whose reason the UI throws away is
    /// barely better than no guard.
    /// </remarks>
    Task<(bool Deleted, string? Error)> DeleteExpectingReasonAsync(
        string relativeUrl, CancellationToken token = default);

    /// <summary>Downloads raw bytes from any authenticated endpoint (e.g. PDF export).</summary>
    Task<(byte[] Data, string ContentType, string FileName)?> GetBytesAsync(string relativeUrl, string fallbackFileName, CancellationToken token = default);

    // ── Sub-client invite accept flow (item #4) — consumed by InviteAccept.razor, anonymous by necessity ──
    Task<InviteInfoRecord?> GetInviteInfoAsync(string token, CancellationToken cancellationToken = default);
    Task<AcceptInviteResult?> AcceptInviteAsync(string token, AcceptInviteRequest request, CancellationToken cancellationToken = default);
    Task<AcceptInviteResult?> AcceptInviteExistingAsync(string token, CancellationToken cancellationToken = default);

    // Example typed endpoint usage using service models.
    Task<LoadResult<AppUserRecord>> GetUsersAsync(CancellationToken token = default);

    Task<LoadResult<OrganizationSummaryResponse>> GetMyOrganizationsAsync(CancellationToken token = default);
    Task<LoadResult<UserSearchResultResponse>> SearchUsersAsync(string? query, int skip = 0, int take = 25, CancellationToken token = default);
    Task<OrganizationSummaryResponse?> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken token = default);
    Task<bool?> CheckMyOrganizationAccessAsync(Guid organizationId, OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken token = default);
    Task<bool?> CheckOrganizationAccessAsync(Guid organizationId, CheckOrganizationAccessRequest request, CancellationToken token = default);
    Task<LoadResult<OrganizationUserMembershipResponse>> GetOrganizationUsersAsync(Guid organizationId, CancellationToken token = default);
    Task<OrganizationUserMembershipResponse?> UpsertOrganizationMembershipAsync(Guid organizationId, Guid targetUserId, UpsertOrganizationMembershipRequest request, CancellationToken token = default);
    Task<OrganizationAccessGrantResponse?> SetOrganizationGrantAsync(Guid organizationId, Guid targetUserId, SetOrganizationGrantRequest request, CancellationToken token = default);

    /// <summary>Minimal Id+DisplayName directory of an org's active members — see
    /// OrganizationController.GetUserDirectory's doc comment for why this exists instead of the
    /// full AppUserRecord (now SuperAdmin-only).</summary>
    Task<LoadResult<OrgUserDirectoryEntryResponse>> GetOrgUserDirectoryAsync(Guid organizationId, CancellationToken token = default);

    // Upload Files
    Task<LoadResult<UploadFileTypeRecord>> GetUploadFileTypesAsync(CancellationToken token = default);
    Task<LoadResult<UploadFileRecord>> GetUploadFilesAsync(CancellationToken token = default);
    Task<UploadFileRecord?> UploadFileAsync(MultipartFormDataContent content, CancellationToken token = default);
    /// <summary>Opens a chunked upload session; the browser then sends the chunks through the website relays. Returns (record, refusal-sentence).</summary>
    Task<(ChunkedUploadSessionRecord? Session, string? Error)> StartChunkedUploadAsync(StartChunkedUploadRequest request, CancellationToken token = default);
    Task<UploadFileRecord?> UpdateUploadFileAsync(Guid id, UpdateUploadFileRequest request, CancellationToken token = default);
    Task<bool> DeleteUploadFileAsync(Guid id, CancellationToken token = default);

    // Upload File — delete-and-reassign (item 180 Phase B)
    /// <summary>Where the file is in use beyond the owner's library — what the delete questions are about.</summary>
    Task<FileUsageRecord?> GetUploadFileUsageAsync(Guid id, CancellationToken token = default);
    /// <summary>First answer: remove it everywhere it is shared, then delete it.</summary>
    Task<DeleteEverywhereResult?> DeleteUploadFileEverywhereAsync(Guid id, CancellationToken token = default);
    /// <summary>Second answer: hand the file to the group using it instead of destroying it.</summary>
    Task<UploadFileRecord?> ReassignUploadFileAsync(Guid id, Guid organizationId, CancellationToken token = default);

    // Upload File — Replace (item #6 phase 3)
    Task<UploadFileRecord?> ReplaceUploadFileAsync(Guid id, MultipartFormDataContent content, CancellationToken token = default);
    Task<ReplaceImpactRecord?> GetReplaceImpactAsync(Guid id, CancellationToken token = default);
    Task<(byte[] Data, string ContentType, string FileName)?> DownloadFileAsync(Guid id, CancellationToken token = default);

    // Upload File — Audio Config
    Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default);
    Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default);

    // Upload File — Region Notes
    Task<LoadResult<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default);
    Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default);
    Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default);

    Task<LoadResult<UploadFileCommentRecord>> GetFileCommentsAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileCommentRecord?> CreateFileCommentAsync(Guid fileId, CreateFileCommentRequest request, CancellationToken token = default);
    Task<UploadFileCommentRecord?> UpdateFileCommentAsync(Guid fileId, Guid commentId, UpdateFileCommentRequest request, CancellationToken token = default);
    Task<bool> DeleteFileCommentAsync(Guid fileId, Guid commentId, CancellationToken token = default);
    Task<FileCommentSettingsRecord?> GetFileCommentSettingsAsync(Guid fileId, CancellationToken token = default);
    Task<FileCommentSettingsRecord?> UpdateFileCommentSettingsAsync(Guid fileId, FileCommentSettingsRecord request, CancellationToken token = default);

    // Upload File — Audio Markers (EVP)
    Task<LoadResult<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default);
    Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default);
    Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default);
    Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default);
    Task<IReadOnlyList<AudioMarkerRecord>?> ReplaceAudioCandidatesAsync(Guid fileId, BulkCreateAudioCandidatesRequest request, CancellationToken token = default);
    Task<AudioMarkerRecord?> ReviewAudioMarkerAsync(Guid fileId, Guid markerId, ReviewAudioMarkerRequest request, CancellationToken token = default);
    Task<IReadOnlyList<AudioMarkerRecord>?> ScanAudioForEvpAsync(Guid fileId, EvpSensitivity sensitivity, EvpDetectionOptions? options = null, CancellationToken token = default);

    // Upload File — Audio Clip
    Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default);
    Task<LoadResult<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default);
    Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default);

    // Upload File — Audio Edit (destructive)
    Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default);

    // Upload File — Votes
    Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default);
    Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default);

    // Upload File — Org Sharing
    Task<LoadResult<UploadFileOrgShareResponse>> GetFileOrgSharesAsync(Guid fileId, CancellationToken token = default);
    Task<LoadResult<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default);
    Task<UploadFileOrgShareResponse?> ShareFileWithOrgAsync(Guid fileId, ShareFileWithOrgRequest request, CancellationToken token = default);
    Task<UploadFileOrgShareResponse?> UpdateOrgShareVisibilityAsync(Guid shareId, UpdateOrgShareVisibilityRequest request, CancellationToken token = default);
    Task<bool> RemoveOrgShareAsync(Guid shareId, CancellationToken token = default);

    // Upload File — Permission Requests
    Task<LoadResult<UploadFilePermissionRequestResponse>> GetFilePermissionRequestsAsync(Guid fileId, CancellationToken token = default);
    Task<LoadResult<UploadFilePermissionRequestResponse>> GetPendingPermissionRequestsForReviewerAsync(Guid reviewerUserId, CancellationToken token = default);
    Task<UploadFilePermissionRequestResponse?> SubmitPermissionRequestAsync(Guid fileId, SubmitPermissionRequestRequest request, CancellationToken token = default);
    Task<UploadFilePermissionRequestResponse?> ReviewPermissionRequestAsync(Guid requestId, ReviewPermissionRequestRequest request, CancellationToken token = default);

    // Impersonation (SuperAdmin only)
    Task<WebApiTokenResponse?> ImpersonateAsync(Guid targetUserId, CancellationToken token = default);

    // Entra account registration and linking — both take the Entra access token explicitly
    // (rather than reading IWebApiTokenStore) so the caller is never at the mercy of whatever
    // token happens to be sitting in the store at call time; the server validates it via the
    // "Entra" JWT scheme and reads OID/email from its own claims, never from the payload.
    Task<EntraRegisterResponse?> EntraRegisterAsync(string entraAccessToken, EntraRegisterPayload request, CancellationToken token = default);
    Task<bool> EntraLinkAsync(string entraAccessToken, EntraLinkPayload request, CancellationToken token = default);
}

// ── Entra request/response records ───────────────────────────────────────────

/// <summary>Sent to POST /api/auth/entra/register — creates a local AppUser linked to the caller's
/// (validated, token-derived) Entra identity. Carries only what the server can't determine itself.</summary>
public sealed record EntraRegisterPayload(string DisplayName);

/// <summary>Response from POST /api/auth/entra/register.</summary>
public sealed record EntraRegisterResponse(Guid UserId, string Email);

/// <summary>Sent to POST /api/auth/entra/link — identifies the target local account to link the
/// caller's (validated, token-derived) Entra identity to; ownership of that account is proven by
/// <see cref="Password"/>, checked server-side.</summary>
public sealed record EntraLinkPayload(string Email, string Password);

// ── Sub-client invite accept-flow records (item #4) — mirrors api/case-invites' shapes; this
// project has no reference to Ben.Data.WebApi (HTTP-only boundary), so the DTOs are duplicated
// here rather than shared, same convention as CoClientItem in Ben.Web.Library's IBenAdminClient.cs. ──

public enum InviteStatus { Valid, Used, Expired, Revoked }

public sealed record InviteInfoRecord(Guid CaseId, string CaseTitle, string InviterDisplayName, string Email, InviteStatus Status, bool AccountExists);
public sealed record AcceptInviteRequest(string DisplayName, string Password);
public sealed record AcceptInviteResult(Guid CaseId);
