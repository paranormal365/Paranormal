using Ben.Web.Services;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Website.Services;

/// <summary>
/// Lets the browser fetch one media file for itself, through this site's media relay.
/// </summary>
/// <remarks>
/// The relay endpoint and its ticket already existed for the site's own media; this points the
/// video editor's Server tab at them. Without it the editor pulled every clip into the server's
/// memory and shipped it over the circuit — three copies of a file the browser could have fetched
/// directly, with a 2 GB ceiling on the way (2026-09-05 audit, site-2 and media-6).
/// </remarks>
public sealed class SiteMediaTicketMinter(
    MediaTicketService tickets,
    IWebApiTokenStore tokenStore) : IMediaTicketMinter
{
    public string? Mint(Guid fileId, string kind)
    {
        var accessToken = tokenStore.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var ticket = tickets.Protect(fileId, accessToken);
        return $"/media/{fileId}/{kind}?t={Uri.EscapeDataString(ticket)}";
    }
}
