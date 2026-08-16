namespace Ben.Video.Editor.Services;

/// <summary>
/// Abstraction over the remote media library data source.
/// Register a custom implementation to point the editor at any HTTP API.
/// The default registration is <see cref="HttpMediaLibraryProvider"/>.
/// </summary>
public interface IMediaLibraryProvider
{
    /// <summary>
    /// Fetches the list of media files available in the library.
    /// Only video/* and audio/* content types should be returned.
    /// </summary>
    Task<IReadOnlyList<Models.MediaLibraryFile>> GetFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the raw bytes of a single library file so the editor can
    /// write it to ffmpeg MEMFS.
    /// </summary>
    /// <param name="progress">Optional 0.0-1.0 download progress reporter, for callers that show
    /// per-file progress UI (e.g. the Server tab's own download-then-cache flow). Implementations
    /// that can't report real progress (unknown content length, etc.) may simply not call it —
    /// callers must treat a caller-side progress bar as best-effort, not a guarantee.</param>
    Task<byte[]> DownloadFileAsync(Guid fileId, CancellationToken cancellationToken = default, IProgress<double>? progress = null);
}
