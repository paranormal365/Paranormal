using Ben.Video.Editor.Models;

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
    /// <param name="scope">
    /// Which slice to list. Null means everything the person may see, which is what this did before
    /// scoping existed. An implementation that cannot narrow should return the full list rather
    /// than an empty one — showing too much is a nuisance, showing nothing looks like a broken tab.
    /// </param>
    Task<IReadOnlyList<MediaLibraryFile>> GetFilesAsync(
        MediaLibraryScope? scope = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the raw bytes of a single library file so the editor can
    /// write it to ffmpeg MEMFS.
    /// </summary>
    /// <param name="progress">Optional 0.0-1.0 download progress reporter, for callers that show
    /// per-file progress UI (e.g. the Server tab's own download-then-cache flow). Implementations
    /// that can't report real progress (unknown content length, etc.) may simply not call it —
    /// callers must treat a caller-side progress bar as best-effort, not a guarantee.</param>
    Task<byte[]> DownloadFileAsync(Guid fileId, CancellationToken cancellationToken = default, IProgress<double>? progress = null);

    /// <summary>
    /// A URL the browser itself can fetch this file from, or null when the host has none.
    /// </summary>
    /// <remarks>
    /// <para>The way to avoid <see cref="DownloadFileAsync"/> entirely. Under Blazor Server that
    /// method runs on the server: the file is pulled into the server's memory, copied again into a
    /// byte array, and then shipped to the browser over the circuit — three copies of a file that
    /// was already reachable from the browser, with a 2 GB ceiling from an int cast along the way
    /// (2026-09-05 audit, site-2 and media-6).</para>
    ///
    /// <para>A host that returns a URL here lets the browser stream the file straight into its own
    /// storage. Returning null is not a failure: the standalone host already fetches from the
    /// browser, so it has nothing to gain, and callers fall back to the byte path.</para>
    /// </remarks>
    Task<string?> GetDownloadUrlAsync(Guid fileId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

/// <summary>
/// Supplies the groups the media library can be scoped by — on this site, cases and their visits.
/// </summary>
/// <remarks>
/// <para>Separate from <see cref="IMediaLibraryProvider"/> on purpose. A host may well be able to
/// list files and have no notion of grouping them; making this a second, optional interface means
/// such a host registers nothing and the editor simply offers All and Personal. Requiring it on
/// the file provider would force every implementation to write a method returning an empty list.
/// </para>
///
/// <para>The editor treats what comes back as opaque labels and ids.</para>
/// </remarks>
public interface IMediaLibraryScopeSource
{
    Task<IReadOnlyList<MediaLibraryScopeGroup>> GetScopeGroupsAsync(CancellationToken cancellationToken = default);
}
