namespace Ben.Web.Services;

/// <summary>
/// Mints the short-lived handle that lets the browser fetch one media file for itself.
/// </summary>
/// <remarks>
/// <para>An interface because the minting lives in the host application, alongside the endpoint
/// that redeems it, while the media provider that needs one lives here. A host without one is not
/// broken — the provider falls back to fetching the file into server memory, which is what it did
/// before (2026-09-05 audit, site-2).</para>
///
/// <para>The ticket carries who is asking, and is bound to the one file it unlocks. The access
/// token itself never reaches the page.</para>
/// </remarks>
public interface IMediaTicketMinter
{
    /// <summary>A URL the browser can fetch <paramref name="fileId"/> from, or null.</summary>
    /// <param name="kind">"download" for the file itself, "thumbnail" for its preview image.</param>
    string? Mint(Guid fileId, string kind);
}
