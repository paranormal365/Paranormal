using Ben.Service.Models.People;
using Ben.Service.Models.Entities;
using Ben.Data.Common.Enums;

namespace Ben.Web.WebApp.Services.WebApi;

public interface IWebApiClient
{
    Task<TResponse?> GetAsync<TResponse>(string relativeUrl, CancellationToken token = default);
    Task<TResponse?> GetAnonymousAsync<TResponse>(string relativeUrl, CancellationToken token = default);
    Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<TResponse?> PostMultipartAsync<TResponse>(string relativeUrl, MultipartFormDataContent content, CancellationToken token = default);
    Task<TResponse?> PutAsync<TRequest, TResponse>(string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<bool> PutVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<bool> PostVoidAsync<TRequest>(string relativeUrl, TRequest payload, CancellationToken token = default);
    Task<bool> DeleteAsync(string relativeUrl, CancellationToken token = default);

    /// <summary>Downloads raw bytes from any authenticated endpoint (e.g. PDF export).</summary>
    Task<(byte[] Data, string ContentType, string FileName)?> GetBytesAsync(string relativeUrl, string fallbackFileName, CancellationToken token = default);

    // Example typed endpoint usage using service models.
    Task<IReadOnlyList<AppUserRecord>> GetUsersAsync(CancellationToken token = default);

    Task<IReadOnlyList<OrganizationSummaryResponse>> GetMyOrganizationsAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserSearchResultResponse>> SearchUsersAsync(string? query, int skip = 0, int take = 25, CancellationToken token = default);
    Task<OrganizationSummaryResponse?> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken token = default);
    Task<bool?> CheckMyOrganizationAccessAsync(Guid organizationId, OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken token = default);
    Task<bool?> CheckOrganizationAccessAsync(Guid organizationId, CheckOrganizationAccessRequest request, CancellationToken token = default);
    Task<IReadOnlyList<OrganizationUserMembershipResponse>> GetOrganizationUsersAsync(Guid organizationId, CancellationToken token = default);
    Task<OrganizationUserMembershipResponse?> UpsertOrganizationMembershipAsync(Guid organizationId, Guid targetUserId, UpsertOrganizationMembershipRequest request, CancellationToken token = default);
    Task<OrganizationAccessGrantResponse?> SetOrganizationGrantAsync(Guid organizationId, Guid targetUserId, SetOrganizationGrantRequest request, CancellationToken token = default);

    // Upload Files
    Task<IReadOnlyList<UploadFileTypeRecord>> GetUploadFileTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UploadFileRecord>> GetUploadFilesAsync(CancellationToken token = default);
    Task<UploadFileRecord?> UploadFileAsync(MultipartFormDataContent content, CancellationToken token = default);
    Task<UploadFileRecord?> UpdateUploadFileAsync(Guid id, UpdateUploadFileRequest request, CancellationToken token = default);
    Task<bool> DeleteUploadFileAsync(Guid id, CancellationToken token = default);

    // Upload File — Replace (item #6 phase 3)
    Task<UploadFileRecord?> ReplaceUploadFileAsync(Guid id, MultipartFormDataContent content, CancellationToken token = default);
    Task<ReplaceImpactRecord?> GetReplaceImpactAsync(Guid id, CancellationToken token = default);
    Task<(byte[] Data, string ContentType, string FileName)?> DownloadFileAsync(Guid id, CancellationToken token = default);

    // Upload File — Audio Config
    Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default);
    Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default);

    // Upload File — Region Notes
    Task<IReadOnlyList<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default);
    Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default);
    Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default);

    Task<IReadOnlyList<UploadFileCommentRecord>> GetFileCommentsAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileCommentRecord?> CreateFileCommentAsync(Guid fileId, CreateFileCommentRequest request, CancellationToken token = default);
    Task<UploadFileCommentRecord?> UpdateFileCommentAsync(Guid fileId, Guid commentId, UpdateFileCommentRequest request, CancellationToken token = default);
    Task<bool> DeleteFileCommentAsync(Guid fileId, Guid commentId, CancellationToken token = default);
    Task<FileCommentSettingsRecord?> GetFileCommentSettingsAsync(Guid fileId, CancellationToken token = default);
    Task<FileCommentSettingsRecord?> UpdateFileCommentSettingsAsync(Guid fileId, FileCommentSettingsRecord request, CancellationToken token = default);

    // Upload File — Audio Markers (EVP)
    Task<IReadOnlyList<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default);
    Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default);
    Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default);
    Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default);

    // Upload File — Audio Clip
    Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default);
    Task<IReadOnlyList<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default);
    Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default);

    // Upload File — Audio Edit (destructive)
    Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default);

    // Upload File — Votes
    Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default);
    Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default);
    Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default);

    // Upload File — Org Sharing
    Task<IReadOnlyList<UploadFileOrgShareResponse>> GetFileOrgSharesAsync(Guid fileId, CancellationToken token = default);
    Task<IReadOnlyList<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default);
    Task<UploadFileOrgShareResponse?> ShareFileWithOrgAsync(Guid fileId, ShareFileWithOrgRequest request, CancellationToken token = default);
    Task<UploadFileOrgShareResponse?> UpdateOrgShareVisibilityAsync(Guid shareId, UpdateOrgShareVisibilityRequest request, CancellationToken token = default);
    Task<bool> RemoveOrgShareAsync(Guid shareId, CancellationToken token = default);

    // Upload File — Permission Requests
    Task<IReadOnlyList<UploadFilePermissionRequestResponse>> GetFilePermissionRequestsAsync(Guid fileId, CancellationToken token = default);
    Task<IReadOnlyList<UploadFilePermissionRequestResponse>> GetPendingPermissionRequestsForReviewerAsync(Guid reviewerUserId, CancellationToken token = default);
    Task<UploadFilePermissionRequestResponse?> SubmitPermissionRequestAsync(Guid fileId, SubmitPermissionRequestRequest request, CancellationToken token = default);
    Task<UploadFilePermissionRequestResponse?> ReviewPermissionRequestAsync(Guid requestId, ReviewPermissionRequestRequest request, CancellationToken token = default);

    // Impersonation (SuperAdmin only)
    Task<WebApiTokenResponse?> ImpersonateAsync(Guid targetUserId, CancellationToken token = default);

    // Entra account registration and linking
    Task<EntraRegisterResponse?> EntraRegisterAsync(EntraRegisterPayload request, CancellationToken token = default);
    Task<bool> EntraLinkAsync(EntraLinkPayload request, CancellationToken token = default);
}

// ── Entra request/response records ───────────────────────────────────────────

/// <summary>Sent to POST /api/auth/entra/register — creates a local AppUser linked to an Entra identity.</summary>
public sealed record EntraRegisterPayload(
    string EntraOid,
    string EntraEmail,
    string DisplayName,
    string? ProviderDisplayName = null);

/// <summary>Response from POST /api/auth/entra/register.</summary>
public sealed record EntraRegisterResponse(Guid UserId, string Email);

/// <summary>Sent to POST /api/auth/entra/link — links an Entra identity to the current local user.</summary>
public sealed record EntraLinkPayload(
    string EntraOid,
    string EntraEmail,
    string? ProviderDisplayName = null);
