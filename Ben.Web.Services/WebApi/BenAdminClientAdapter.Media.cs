using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Media half of the adapter — implements <see cref="Ben.Web.Services.IBenMediaClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Universal media library sharing ──────────────────────────────────────

    public Task<LoadResult<UploadFileShareRecord>> GetSharesV2Async(Guid fileId, CancellationToken token = default)
            => _api.GetListAsync<UploadFileShareRecord>($"/api/upload-files/{fileId}/shares-v2", token);

    public Task<UploadFileShareRecord?> CreateShareAsync(Guid fileId, CreateShareRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateShareRequest, UploadFileShareRecord>($"/api/upload-files/{fileId}/shares-v2", request, token);

    public Task<bool> RemoveShareV2Async(Guid shareId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/upload-file-shares-v2/{shareId}", token);

    public Task<LoadResult<UploadFileRecord>> GetMediaLibraryFilesAsync(string[]? contentTypePrefixes = null, CancellationToken token = default)
    {
        var url = "/api/media-library/files";
        if (contentTypePrefixes is { Length: > 0 })
            url += $"?contentTypePrefixes={Uri.EscapeDataString(string.Join(',', contentTypePrefixes))}";
        return _api.GetListAsync<UploadFileRecord>(url, token);
    }

    // ── File Types ────────────────────────────────────────────────────────────

    public Task<LoadResult<AdminFileTypeWithExtensionsResponse>> GetFileTypesWithExtensionsAsync(CancellationToken token = default)
        => _api.GetListAsync<AdminFileTypeWithExtensionsResponse>("/api/admin/upload-file-types/with-extensions", token);

    public Task<UploadFileTypeRecord?> CreateFileTypeAsync(AdminCreateFileTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateFileTypeRequest, UploadFileTypeRecord>("/api/admin/upload-file-types", request, token);

    public Task<UploadFileTypeRecord?> UpdateFileTypeAsync(Guid id, AdminUpdateFileTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<AdminUpdateFileTypeRequest, UploadFileTypeRecord>($"/api/admin/upload-file-types/{id}", request, token);

    public Task<bool> DeleteFileTypeAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/upload-file-types/{id}", token);

    // ── File Type Extensions ──────────────────────────────────────────────────

    public Task<UploadFileTypeExtensionRecord?> CreateFileTypeExtensionAsync(AdminCreateFileTypeExtensionRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateFileTypeExtensionRequest, UploadFileTypeExtensionRecord>("/api/admin/upload-file-type-extensions", request, token);

    public Task<UploadFileTypeExtensionRecord?> UpdateFileTypeExtensionAsync(Guid id, string pattern, CancellationToken token = default)
        => _api.PutAsync<object, UploadFileTypeExtensionRecord>($"/api/admin/upload-file-type-extensions/{id}", new { Pattern = pattern }, token);

    public Task<bool> DeleteFileTypeExtensionAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/upload-file-type-extensions/{id}", token);

    // ── File admin delete ─────────────────────────────────────────────────────

    public Task<bool> DeleteUploadFileAdminAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/upload-files/{id}", token);

    // ── CMS File Library ──────────────────────────────────────────────────────

    public Task<LoadResult<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default)
        => _api.GetOrgSharedFilesAsync(orgId, token);

    public async Task<(byte[] Data, string ContentType)?> GetFileDataAsync(Guid fileId, CancellationToken token = default)
    {
        var result = await _api.DownloadFileAsync(fileId, token);
        if (result is null) return null;
        return (result.Value.Data, result.Value.ContentType);
    }

    public Task<LoadResult<UploadFileTypeRecord>> GetPublicFileTypesAsync(CancellationToken token = default)
        => _api.GetUploadFileTypesAsync(token);

    public async Task<UploadFileRecord?> UploadImageAsync(
        Guid fileTypeId, Guid userId, string fileName, string contentType, byte[] data,
        CancellationToken token = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()), "uploadFileTypeId");
        form.Add(new StringContent(userId.ToString()), "appUserId");
        form.Add(new StringContent(""), "description");
        form.Add(new StringContent("true"), "isPublic");
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return await _api.UploadFileAsync(form, token);
    }

    public async Task<UploadFileRecord?> UploadUserFileAsync(
        Guid fileTypeId, Guid userId, string fileName, string contentType, byte[] data,
        string? description = null, bool isPublic = false, CancellationToken token = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()),             "uploadFileTypeId");
        form.Add(new StringContent(userId.ToString()),                 "appUserId");
        form.Add(new StringContent(description ?? string.Empty),       "description");
        form.Add(new StringContent(isPublic ? "true" : "false"),       "isPublic");
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return await _api.UploadFileAsync(form, token);
    }

    // ── Audio Config ─────────────────────────────────────────────────────

    public Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default)
        => _api.GetAudioConfigAsync(fileId, token);

    public Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default)
        => _api.UpsertAudioConfigAsync(fileId, request, token);

    public Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default)
        => _api.DeleteAudioConfigAsync(fileId, token);

    // ── Region Notes ──────────────────────────────────────────────────────────

    public Task<LoadResult<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default)
        => _api.GetRegionNotesAsync(fileId, token);

    public Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default)
        => _api.CreateRegionNoteAsync(fileId, request, token);

    public Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default)
        => _api.UpdateRegionNoteAsync(fileId, noteId, request, token);

    public Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default)
        => _api.DeleteRegionNoteAsync(fileId, noteId, token);

    // ── File Comments (item #6 phase 2) ───────────────────────────────────────

    public Task<LoadResult<UploadFileCommentRecord>> GetFileCommentsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetFileCommentsAsync(fileId, token);

    public Task<UploadFileCommentRecord?> CreateFileCommentAsync(Guid fileId, CreateFileCommentRequest request, CancellationToken token = default)
        => _api.CreateFileCommentAsync(fileId, request, token);

    public Task<UploadFileCommentRecord?> UpdateFileCommentAsync(Guid fileId, Guid commentId, UpdateFileCommentRequest request, CancellationToken token = default)
        => _api.UpdateFileCommentAsync(fileId, commentId, request, token);

    public Task<bool> DeleteFileCommentAsync(Guid fileId, Guid commentId, CancellationToken token = default)
        => _api.DeleteFileCommentAsync(fileId, commentId, token);

    public Task<FileCommentSettingsRecord?> GetFileCommentSettingsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetFileCommentSettingsAsync(fileId, token);

    public Task<FileCommentSettingsRecord?> UpdateFileCommentSettingsAsync(Guid fileId, FileCommentSettingsRecord request, CancellationToken token = default)
        => _api.UpdateFileCommentSettingsAsync(fileId, request, token);

    // ── Audio Markers (EVP) ──────────────────────────────────────────────────

    public Task<LoadResult<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default)
        => _api.GetAudioMarkersAsync(fileId, token);

    public Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default)
        => _api.CreateAudioMarkerAsync(fileId, request, token);

    public Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default)
        => _api.UpdateAudioMarkerAsync(fileId, markerId, request, token);

    public Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default)
        => _api.DeleteAudioMarkerAsync(fileId, markerId, token);

    public Task<IReadOnlyList<AudioMarkerRecord>?> ReplaceAudioCandidatesAsync(Guid fileId, BulkCreateAudioCandidatesRequest request, CancellationToken token = default)
        => _api.ReplaceAudioCandidatesAsync(fileId, request, token);

    public Task<AudioMarkerRecord?> ReviewAudioMarkerAsync(Guid fileId, Guid markerId, ReviewAudioMarkerRequest request, CancellationToken token = default)
        => _api.ReviewAudioMarkerAsync(fileId, markerId, request, token);

    public Task<IReadOnlyList<AudioMarkerRecord>?> ScanAudioForEvpAsync(Guid fileId, EvpSensitivity sensitivity, EvpDetectionOptions? options = null, CancellationToken token = default)
        => _api.ScanAudioForEvpAsync(fileId, sensitivity, options, token);

    // ── Audio Clip ────────────────────────────────────────────────────────────

    public Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default)
        => _api.ClipAudioAsync(fileId, request, token);

    public Task<LoadResult<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetChildClipsAsync(fileId, token);

    public Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default)
        => _api.EditAudioAsync(fileId, request, token);

    public Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default)
        => _api.GetClipPreviewAsync(fileId, start, end, token);

    // ── Video projects ────────────────────────────────────────────────────────
    public Task<LoadResult<VideoProjectRecord>> GetMyVideoProjectsAsync(Guid? caseId = null, CancellationToken token = default)
    {        var url = caseId.HasValue ? $"/api/video-projects?caseId={caseId}" : "/api/video-projects";
        return _api.GetListAsync<VideoProjectRecord>(url, token);
    }
    public Task<VideoProjectRecord?> GetMyVideoProjectAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<VideoProjectRecord>($"/api/video-projects/{id}", token);
    public Task<VideoProjectRecord?> SaveMyVideoProjectAsync(Ben.Video.Editor.Models.ProjectFile file, Guid? caseId = null, CancellationToken token = default)
    {
        var url = caseId.HasValue ? $"/api/video-projects?caseId={caseId}" : "/api/video-projects";
        return _api.PostAsync<Ben.Video.Editor.Models.ProjectFile, VideoProjectRecord>(url, file, token);
    }
    public Task<VideoProjectRecord?> UpdateMyVideoProjectAsync(Guid id, Ben.Video.Editor.Models.ProjectFile file, CancellationToken token = default)
        => _api.PutAsync<Ben.Video.Editor.Models.ProjectFile, VideoProjectRecord>($"/api/video-projects/{id}", file, token);
    public Task<VideoProjectRecord?> PublishVideoProjectAsync(Guid id, byte[] bytes, string fileName, string contentType, CancellationToken token = default)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", fileName);
        return _api.PostMultipartAsync<VideoProjectRecord>($"/api/video-projects/{id}/publish", form, token);
    }
    public Task<bool> DeleteMyVideoProjectAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/video-projects/{id}", token);

    // ── Image editor ────────────────────────────────────────────────────────
    public Task<UploadFileRecord?> SaveImageEditStateAsync(Guid fileId, string? editStateJson, CancellationToken token = default)
        => _api.PutAsync<object, UploadFileRecord>($"/api/upload-files/{fileId}/edit-state", new { EditStateJson = editStateJson }, token);
    public Task<UploadFileRecord?> SaveImageAsNewVersionAsync(Guid parentFileId, byte[] imageBytes, string format, CancellationToken token = default)
    {
        var mime = format == "jpeg" ? "image/jpeg" : "image/png";
        var ext  = format == "jpeg" ? ".jpg" : ".png";
        var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mime);
        var form = new MultipartFormDataContent();
        form.Add(content, "file", $"edited{ext}");
        return _api.PostMultipartAsync<UploadFileRecord>($"/api/upload-files/{parentFileId}/save-as-version", form, token);
    }
}
