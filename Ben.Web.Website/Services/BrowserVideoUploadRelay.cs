using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Microsoft.JSInterop;

namespace Ben.Web.Website.Services;

/// <summary>
/// Posts a finished render from the browser straight to the API, through this site's upload relay.
/// </summary>
/// <remarks>
/// <para>The same trust shape the other upload relays use: the circuit mints a short-lived ticket
/// bound to this one project, the browser posts the file to an endpoint that takes the ticket, and
/// the endpoint streams the body to the API. The access token is never handed to the page, and the
/// file never enters this process's memory (2026-09-05 audit, site-1).</para>
///
/// <para>The ticket is bound to the project id rather than a fresh nonce, so it cannot be replayed
/// against a different project — the endpoint it unlocks names that project in its path.</para>
/// </remarks>
public sealed class BrowserVideoUploadRelay(
    UploadTicketService tickets,
    IWebApiTokenStore tokenStore,
    IJSRuntime js) : IVideoUploadRelay
{
    private const string ModulePath = "/_content/Ben.Video.Editor/js/domInterop.js";

    public async Task<string?> PublishAsync(
        Guid projectId, string blobUrl, string fileName, string contentType,
        CancellationToken ct = default)
    {
        var accessToken = tokenStore.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            return "You are not signed in any more, so the video could not be uploaded. "
                 + "Sign in again and try the upload once more — the render is still here.";

        var ticket   = tickets.Protect(projectId, accessToken);
        var endpoint = $"/uploads/video-project/{projectId}?t={Uri.EscapeDataString(ticket)}";

        await using var module = await js.InvokeAsync<IJSObjectReference>("import", ct, ModulePath);

        var result = await module.InvokeAsync<PostResult>(
            "postBlobUrlTo", ct, blobUrl, endpoint, fileName, contentType);

        if (result.Ok) return null;

        return result.Status switch
        {
            401 => "Your sign-in has expired, so the video could not be uploaded. Sign in again "
                 + "and try the upload once more — the render is still here.",
            413 => "The server refused the file for being too large.",
            0   => "The upload could not reach the server. Check your connection and try again — "
                 + "the render is still here.",
            _   => $"The server would not accept the video ({result.Status})."
                   + (string.IsNullOrWhiteSpace(result.Body) ? "" : $" {result.Body}"),
        };
    }

    /// <summary>What the browser's own fetch reported. Shaped to match postBlobUrlTo's return.</summary>
    private sealed record PostResult(bool Ok, int Status, string? Body);
}
