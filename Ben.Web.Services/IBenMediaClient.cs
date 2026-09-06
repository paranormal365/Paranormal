using Ben.Web.Services.WebApi;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// The Media slice of <see cref="IBenAdminClient"/> — uploaded files and the media derived from them.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenMediaClient
{
    // ── Universal media library sharing (person / investigation team / org / public) ────────────

    /// <summary>Returns active shares for a file. Owner or SuperAdmin only.</summary>
    Task<LoadResult<UploadFileShareRecord>> GetSharesV2Async(Guid fileId, CancellationToken token = default);

    /// <summary>Grants one of the 4 share targets on a file the caller owns.</summary>
    Task<UploadFileShareRecord?> CreateShareAsync(Guid fileId, CreateShareRequest request, CancellationToken token = default);

    /// <summary>Revokes a share. Owner or SuperAdmin only.</summary>
    Task<bool> RemoveShareV2Async(Guid shareId, CancellationToken token = default);

    /// <summary>
    /// Returns files across every scope the universal media library aggregates (owned, shared,
    /// org, public, case-linked). Pass <paramref name="contentTypePrefixes"/> (e.g. "video/","image/")
    /// to narrow the result; omit for everything.
    /// </summary>
    Task<LoadResult<UploadFileRecord>> GetMediaLibraryFilesAsync(string[]? contentTypePrefixes = null, CancellationToken token = default);

    // ── File Types ────────────────────────────────────────────────────────────

    /// <summary>Returns all upload file types together with their allowed extension patterns.</summary>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    Task<LoadResult<AdminFileTypeWithExtensionsResponse>> GetFileTypesWithExtensionsAsync(CancellationToken token = default);

    /// <summary>Creates a new upload file type.</summary>
    /// <param name="request">Fields for the new file type including display metadata and the <c>AllowAllExtensions</c> flag.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The created <see cref="UploadFileTypeRecord"/>, or <c>null</c> if creation failed.</returns>
    Task<UploadFileTypeRecord?> CreateFileTypeAsync(AdminCreateFileTypeRequest request, CancellationToken token = default);

    /// <summary>Updates an existing upload file type.</summary>
    /// <param name="id">The primary key of the file type to update.</param>
    /// <param name="request">Replacement field values.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The updated record, or <c>null</c> if the update failed.</returns>
    Task<UploadFileTypeRecord?> UpdateFileTypeAsync(Guid id, AdminUpdateFileTypeRequest request, CancellationToken token = default);

    /// <summary>Deletes an upload file type and cascades to all of its extension patterns.</summary>
    /// <param name="id">The primary key of the file type to delete.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns><c>true</c> if deletion succeeded; <c>false</c> otherwise.</returns>
    Task<bool> DeleteFileTypeAsync(Guid id, CancellationToken token = default);

    // ── File Type Extensions ──────────────────────────────────────────────────

    /// <summary>Adds an extension pattern to an existing upload file type.</summary>
    /// <param name="request">The file type ID, pattern string (e.g. <c>.txt</c> or <c>.tx*</c>), and creator ID.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The created <see cref="UploadFileTypeExtensionRecord"/>, or <c>null</c> if creation failed.</returns>
    Task<UploadFileTypeExtensionRecord?> CreateFileTypeExtensionAsync(AdminCreateFileTypeExtensionRequest request, CancellationToken token = default);

    /// <summary>Replaces the pattern string of an existing extension record.</summary>
    /// <param name="id">The primary key of the extension to update.</param>
    /// <param name="pattern">The new pattern string (e.g. <c>.pdf</c> or <c>.doc*</c>).</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The updated <see cref="UploadFileTypeExtensionRecord"/>, or <c>null</c> if the update failed.</returns>
    Task<UploadFileTypeExtensionRecord?> UpdateFileTypeExtensionAsync(Guid id, string pattern, CancellationToken token = default);

    /// <summary>Removes a single extension pattern from its parent file type.</summary>
    /// <param name="id">The primary key of the extension to delete.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns><c>true</c> if deletion succeeded; <c>false</c> otherwise.</returns>
    Task<bool> DeleteFileTypeExtensionAsync(Guid id, CancellationToken token = default);

    // ── Clipart catalog (SuperAdmin) ─────────────────────────────────────────

    /// <summary>Every catalog asset, active and retired.</summary>
    Task<LoadResult<VideoAssetAdminRecord>> GetVideoAssetsAsync(CancellationToken token = default);

    /// <summary>Publishes an already-uploaded file into the shared catalog.</summary>
    Task<VideoAssetAdminRecord?> CreateVideoAssetAsync(
        CreateVideoAssetRequest request, CancellationToken token = default);

    /// <summary>Edits catalog metadata. Also used to restore a retired asset.</summary>
    Task<VideoAssetAdminRecord?> UpdateVideoAssetAsync(
        Guid id, UpdateVideoAssetRequest request, CancellationToken token = default);

    /// <summary>Retires an asset — out of the catalog, still downloadable by existing projects.</summary>
    Task<bool> RetireVideoAssetAsync(Guid id, CancellationToken token = default);

    // ── CMS File Library ──────────────────────────────────────────────────────

    /// <summary>Returns upload files shared with the given organization (for logo/gallery selection).</summary>
    Task<LoadResult<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Downloads raw file bytes + content-type for in-browser thumbnail rendering.</summary>
    Task<(byte[] Data, string ContentType)?> GetFileDataAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Returns all active upload file types (used to choose a type when uploading a logo).</summary>
    Task<LoadResult<UploadFileTypeRecord>> GetPublicFileTypesAsync(CancellationToken token = default);

    /// <summary>Uploads an image file and returns its record. Used to add a logo from device.</summary>
    Task<UploadFileRecord?> UploadImageAsync(Guid fileTypeId, Guid userId, string fileName, string contentType, byte[] data, CancellationToken token = default);

    /// <summary>
    /// Uploads any file (audio, document, image, etc.) for a specific user.
    /// Use when the caller controls the description and public-visibility flag.
    /// </summary>
    Task<UploadFileRecord?> UploadUserFileAsync(
        Guid fileTypeId, Guid userId,
        string fileName, string contentType, byte[] data,
        string? description = null, bool isPublic = false,
        CancellationToken token = default);

    // ── Audio Config ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the saved WaveSurfer config for an audio UploadFile,
    /// or <c>null</c> if none has been saved (component uses defaults).
    /// </summary>
    Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Creates or fully replaces the WaveSurfer config for an audio UploadFile.</summary>
    Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default);

    /// <summary>Removes the saved WaveSurfer config; the player will use theme-derived defaults on next render.</summary>
    Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default);

    // ── Region Notes ──────────────────────────────────────────────

    /// <summary>Returns all region notes for the given file, ordered by region start then time offset.</summary>
    Task<LoadResult<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Creates a new region note and returns the persisted record.</summary>
    Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default);

    /// <summary>Updates an existing region note (text, public flag, time offset).</summary>
    Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default);

    /// <summary>Permanently deletes a region note.</summary>
    Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default);

    // ── File Comments (item #6 phase 2) ────────────────────────────

    /// <summary>Returns the full comment thread for a file — visible to anyone who can see the file.</summary>
    Task<LoadResult<UploadFileCommentRecord>> GetFileCommentsAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Posts a new comment. Fails (null) unless the caller is the file's owner or matches an enabled audience.</summary>
    Task<UploadFileCommentRecord?> CreateFileCommentAsync(Guid fileId, CreateFileCommentRequest request, CancellationToken token = default);

    /// <summary>Edits the text of the caller's own comment.</summary>
    Task<UploadFileCommentRecord?> UpdateFileCommentAsync(Guid fileId, Guid commentId, UpdateFileCommentRequest request, CancellationToken token = default);

    /// <summary>Deletes a comment — allowed for its author or the file's owner.</summary>
    Task<bool> DeleteFileCommentAsync(Guid fileId, Guid commentId, CancellationToken token = default);

    /// <summary>Returns the file's current per-audience commenting toggles.</summary>
    Task<FileCommentSettingsRecord?> GetFileCommentSettingsAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Updates the file's per-audience commenting toggles. Owner-only.</summary>
    Task<FileCommentSettingsRecord?> UpdateFileCommentSettingsAsync(Guid fileId, FileCommentSettingsRecord request, CancellationToken token = default);

    // ── Audio Markers (EVP) ───────────────────────────────────────

    /// <summary>Returns all EVP markers for the given file, ordered by time.</summary>
    Task<LoadResult<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Creates a new EVP marker and returns the persisted record.</summary>
    Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default);

    /// <summary>Updates an existing EVP marker (time, label, confidence, note).</summary>
    Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default);

    /// <summary>Permanently deletes an EVP marker.</summary>
    Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default);

    /// <summary>
    /// Replaces this file's pending detector candidates with a fresh scan's results, leaving
    /// confirmed and dismissed markers alone. Returns the newly-created candidates.
    /// </summary>
    Task<IReadOnlyList<AudioMarkerRecord>?> ReplaceAudioCandidatesAsync(
        Guid fileId, BulkCreateAudioCandidatesRequest request, CancellationToken token = default);

    /// <summary>Records a reviewer's decision on a candidate — confirm (optionally relabelled and re-bounded) or dismiss.</summary>
    Task<AudioMarkerRecord?> ReviewAudioMarkerAsync(
        Guid fileId, Guid markerId, ReviewAudioMarkerRequest request, CancellationToken token = default);

    /// <summary>
    /// Runs EVP detection over the stored audio and replaces this file's pending candidates with
    /// the results, skipping anything overlapping a marker already confirmed or dismissed.
    /// </summary>
    /// <param name="options">
    /// Per-scan overrides. Null uses <paramref name="sensitivity"/>'s preset unchanged.
    /// </param>
    Task<IReadOnlyList<AudioMarkerRecord>?> ScanAudioForEvpAsync(
        Guid fileId, EvpSensitivity sensitivity, EvpDetectionOptions? options = null, CancellationToken token = default);

    // ── Audio Clip ─────────────────────────────────────────────────

    /// <summary>
    /// Clips the audio of <paramref name="fileId"/> to the specified time range and saves the
    /// result as a new UploadFile. Currently supports WAV and MP3 sources; output is WAV.
    /// </summary>
    Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default);

    /// <summary>
    /// Returns clipped audio bytes for the given time range WITHOUT saving a new file.
    /// Used by <c>WsRegionExplorer</c> to load only the region's audio.
    /// Returns null if the source format is unsupported (non-WAV / non-MP3).
    /// </summary>
    Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default);

    /// <summary>Returns all child clip files that were derived from <paramref name="fileId"/> via the region-clip workflow.</summary>
    Task<LoadResult<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default);

    // ── Audio Edit (destructive) ──────────────────────────────────

    /// <summary>
    /// Applies a destructive audio edit (cut, silence, normalize, gain, fade, reverse) to
    /// <paramref name="fileId"/> and saves the result as a new UploadFile. The source is never modified.
    /// </summary>
    Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default);

    // ── Video projects ────────────────────────────────────────────────────────
    Task<LoadResult<VideoProjectRecord>> GetMyVideoProjectsAsync(Guid? caseId = null, CancellationToken token = default);
    Task<VideoProjectRecord?> GetMyVideoProjectAsync(Guid id, CancellationToken token = default);
    Task<VideoProjectRecord?> SaveMyVideoProjectAsync(Ben.Video.Editor.Models.ProjectFile file, Guid? caseId = null, CancellationToken token = default);
    Task<VideoProjectRecord?> UpdateMyVideoProjectAsync(Guid id, Ben.Video.Editor.Models.ProjectFile file, CancellationToken token = default);
    Task<VideoProjectRecord?> PublishVideoProjectAsync(Guid id, byte[] bytes, string fileName, string contentType, CancellationToken token = default);
    Task<bool> DeleteMyVideoProjectAsync(Guid id, CancellationToken token = default);

    /// <summary>
    /// Asks the API for a one-minute, one-use code that signs this same person into the standalone
    /// editor (phase 12).
    /// </summary>
    /// <returns>The code, or null when the API refused — in which case the link is offered without one.</returns>
    Task<EditorHandoffCode?> GetEditorHandoffCodeAsync(CancellationToken token = default);

    // ── Image editor ────────────────────────────────────────────────────────
    Task<UploadFileRecord?> SaveImageEditStateAsync(Guid fileId, string? editStateJson, CancellationToken token = default);
    Task<UploadFileRecord?> SaveImageAsNewVersionAsync(Guid parentFileId, byte[] imageBytes, string format, CancellationToken token = default);
}
