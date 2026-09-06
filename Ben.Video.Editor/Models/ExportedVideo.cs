namespace Ben.Video.Editor.Models;

/// <summary>
/// A finished render the user has chosen to send to the host application rather than save to their
/// own machine — phase 176.
///
/// <para><b>The bytes are not in here.</b> <see cref="ReadBytesAsync"/> materializes them on
/// demand, once. A rendered video is the largest artifact this app produces, and the whole export
/// pipeline is built to keep it off the .NET heap (item #38 phase D); handing a host a struct that
/// already contains a <c>byte[]</c> would undo that for every host, including ones that would
/// rather stream. Hosts that need bytes (anything posting to an API that takes a <c>byte[]</c>)
/// call the delegate; the rest can use <see cref="SizeBytes"/> and <see cref="FileName"/> to decide
/// what to do first — refuse an over-quota upload, say — without ever reading the body.</para>
///
/// <para>Valid only for the duration of the callback that receives it: the editor deletes its OPFS
/// copy as soon as the callback returns, so a host that stashes this and reads it later gets
/// <c>null</c>.</para>
/// </summary>
/// <param name="FileName">The user's chosen output filename including extension, e.g. <c>my-video.mp4</c>.</param>
/// <param name="ContentType">MIME type matching the chosen output format, e.g. <c>video/mp4</c>.</param>
/// <param name="SizeBytes">Size of the rendered output, known without reading it.</param>
/// <param name="DurationSeconds">Duration of the rendered output, from the pipeline's own final probe.</param>
/// <param name="ReadBytesAsync">Materializes the body. Returns <c>null</c> if the export is already gone.</param>
/// <param name="BlobUrl">
/// Where the render sits in the browser, so a host can hand it straight to a server without it
/// passing through .NET at all.
/// </param>
/// <remarks>
/// <para><see cref="BlobUrl"/> is the one a Blazor Server host wants. Reading the bytes there means
/// returning them over the circuit as a JS-interop value, which Blazor caps at 32 KB by default —
/// so publishing a real render from the site could not work, at any size worth calling a video
/// (2026-09-05 audit, site-1). With the URL, the browser posts the file itself and the server
/// process never sees it.</para>
///
/// <para>Valid for exactly as long as the rest of this record: the editor revokes it as soon as
/// the callback returns.</para>
/// </remarks>
public sealed record ExportedVideo(
    string FileName,
    string ContentType,
    long SizeBytes,
    double DurationSeconds,
    Func<Task<byte[]?>> ReadBytesAsync,
    string? BlobUrl = null);
