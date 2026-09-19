namespace Ben.Web.Services;

/// <summary>
/// Hands a finished render from the browser to the API without it passing through this process.
/// </summary>
/// <remarks>
/// <para>Publishing from the site used to read the whole render back into the circuit as one
/// JS-interop <c>byte[]</c> return. Blazor Server caps a JS-interop return value at 32 KB by
/// default and nothing raises it here, so a real render — megabytes at the very least — could not
/// be published from the site at all (2026-09-05 audit, site-1).</para>
///
/// <para>An interface rather than a direct dependency because the ticket minting and the JS call
/// both live in the host application, while the publisher that needs them lives here. The
/// publisher works without one, falling back to the byte path, so a host that has not registered a
/// relay is degraded rather than broken.</para>
/// </remarks>
public interface IVideoUploadRelay
{
    /// <summary>
    /// Posts the render at <paramref name="blobUrl"/> to the project's publish endpoint.
    /// </summary>
    /// <returns>Null when it worked, otherwise something to show the person.</returns>
    Task<string?> PublishAsync(
        Guid projectId, string blobUrl, string fileName, string contentType,
        CancellationToken ct = default);
}
