using Ben.Data.Common.Interfaces;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Writes uploaded files straight through to storage.
/// </summary>
/// <remarks>
/// <para>The obvious shape — copy the <see cref="IFormFile"/> into a <see cref="MemoryStream"/>,
/// call <c>ToArray()</c>, hand the array to storage — keeps <i>two</i> full copies of the upload
/// resident for the life of the request. Uploads here have no size limit by design (the cap
/// belongs at app-settings/person/org/case scope, not baked into an endpoint), so that cost is
/// unbounded: one large video is gigabytes of server memory, and a few concurrent uploads is a
/// self-inflicted outage.</para>
///
/// <para><see cref="IFormFile"/> and <see cref="IFileStorageService"/> are both stream-based
/// already, so nothing is gained by materialising the bytes in between.</para>
///
/// <para>Callers that must inspect or rewrite the whole file before it is stored — SVG
/// sanitisation is the only one — still have to buffer, and should say why where they do it.</para>
/// </remarks>
public static class FormFileStorageExtensions
{
    /// <summary>
    /// Streams <paramref name="file"/> to <paramref name="relativePath"/> without buffering it.
    /// </summary>
    public static async Task WriteFormFileAsync(
        this IFileStorageService storage,
        string relativePath,
        IFormFile file,
        CancellationToken ct = default)
    {
        await using var source = file.OpenReadStream();
        await storage.WriteAsync(relativePath, source, ct);
    }

    /// <summary>
    /// Streams <paramref name="bytes"/> to <paramref name="relativePath"/>. For content the caller
    /// genuinely has in memory already (sanitised SVG, transcoded audio) — not a way to keep
    /// buffering uploads.
    /// </summary>
    public static async Task WriteBytesAsync(
        this IFileStorageService storage,
        string relativePath,
        byte[] bytes,
        CancellationToken ct = default)
    {
        using var source = new MemoryStream(bytes, writable: false);
        await storage.WriteAsync(relativePath, source, ct);
    }
}
