namespace Ben.Web.Services;

/// <summary>
/// Where the browser should fetch a file's picture or bytes from.
/// </summary>
/// <remarks>
/// Components in the shared library need these URLs but must not know how the host authenticates
/// them, so the interface lives here and the implementation sits in the website.
///
/// <para><b>Use this instead of fetching file bytes in a component</b> whenever more than one file
/// can appear at once. Fetching puts a copy of each file in this process's memory and another,
/// larger, base64 copy in the page.</para>
/// </remarks>
public interface IMediaUrlBuilder
{
    /// <summary>A shrunken picture of the file — for tiles, avatars and grids.</summary>
    string Thumbnail(Guid fileId);

    /// <summary>The file itself, streamed — for audio, video and downloads.</summary>
    string Download(Guid fileId);

    /// <summary>
    /// A recording belonging to an uploaded field session.
    /// </summary>
    /// <remarks>
    /// Its own route because a session's files are served by the session endpoint, which checks
    /// access against the investigation rather than the file's own audience.
    /// </remarks>
    string FieldSessionFile(Guid sessionId, Guid fileId);
}
