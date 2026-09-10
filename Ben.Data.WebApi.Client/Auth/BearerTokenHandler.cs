using System.Net;
using System.Net.Http.Headers;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>
/// Attaches the signed-in person's access token to every request, and notices when the server
/// stops accepting it.
/// </summary>
/// <remarks>
/// <para>Ben.Web.Services had a handler like this and could not use it: <c>IHttpClientFactory</c>
/// resolves handlers from the root scope, so the token store injected here was never the Blazor
/// circuit's own and was always empty. That objection is specific to Blazor Server. A desktop or
/// mobile client has one container and one signed-in person, so this is simply the right place for
/// the concern — and it means no call site can forget the header.</para>
///
/// <para>A request that already carries an <c>Authorization</c> header is left alone. That is how
/// the Entra endpoints are reached: <c>api/auth/entra/register</c> and <c>/link</c> are authorised
/// by a Microsoft-issued JWT, not by an Identity session, and overwriting it would make them
/// answer 401 for a reason nothing on screen could explain.</para>
/// </remarks>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly TokenSession _session;

    public BearerTokenHandler(TokenSession session) => _session = session;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var carriedOurToken = false;

        if (request.Headers.Authorization is null)
        {
            var accessToken = await _session.GetValidAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                carriedOurToken = true;
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Only when WE attached the token. An anonymous request that is refused says nothing about
        // the session, and ending a good session because a public endpoint answered 401 would sign
        // somebody out for reading a page they were never signed in to read.
        if (carriedOurToken && response.StatusCode == HttpStatusCode.Unauthorized)
            await _session.HandleUnauthorizedAsync();

        return response;
    }
}
