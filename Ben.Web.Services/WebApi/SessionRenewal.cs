using System.Net;
using System.Net.Http.Headers;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// Keeps a signed-in circuit's session alive, and ends it properly when it cannot be.
/// </summary>
/// <remarks>
/// <para><b>Why (10/14/2026).</b> Ben, coming back to the site after a while: it showed him signed
/// in, every part of the page said "You have been signed out", and he had to open the menu and
/// choose Sign out before it was true. The access token had expired and nothing ever renewed it:
/// <c>WebApiAuthService.RefreshIfNeededAsync</c> existed and had no caller. So the circuit kept the
/// dead token, the menu kept the person, and the API refused everything.</para>
///
/// <para><b>Now:</b> a token about to expire is renewed before the request goes (one renewal at a
/// time per circuit, behind <see cref="IWebApiTokenStore.RenewalGate"/>); a request the API refuses
/// is renewed once and sent again; and when renewal fails the store raises
/// <see cref="IWebApiTokenStore.SessionEnded"/>, which signs the person out and takes them to sign
/// in, with the page they were on to come back to.</para>
///
/// <para><b>Only the tokens change on a renewal.</b> The old renewal path re-read the person from
/// the new token, but these are opaque Identity tokens that carry no claims: it would have blanked
/// the user id and every role. Who the person is was established at sign-in and stays.</para>
/// </remarks>
public sealed class SessionRenewer(IWebApiTokenStore store, IWebApiIdentityClient identity)
{
    /// <summary>How close to expiry a token is renewed rather than sent.</summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    /// <summary>The token to send now: the current one, renewed first if it is about to expire.</summary>
    /// <returns>Null when nobody is signed in.</returns>
    public async Task<string?> TokenToSendAsync(CancellationToken ct)
    {
        var token = store.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return null;

        // Unknown expiry (an Entra session, or a session persisted before expiry was): send it and
        // let a refusal decide.
        if (store.AccessTokenExpiresAtUtc is not { } expires || expires > DateTimeOffset.UtcNow + Margin)
            return token;

        // A proactive renewal that does not work is not the end of the session: the old token goes,
        // and a refusal comes back through RenewAfterRefusalAsync.
        return (await RenewAsync(token, force: false, ct)).Token ?? token;
    }

    /// <summary>The API refused <paramref name="refused"/>: renew once.</summary>
    public Task<Renewal> RenewAfterRefusalAsync(string refused, CancellationToken ct)
        => RenewAsync(refused, force: true, ct);

    /// <summary>The session cannot be renewed: sign the person out (see <see cref="IWebApiTokenStore.SessionEnded"/>).</summary>
    public void End(string refused)
    {
        // Only the session that was refused. A request that left before a sign-in, an impersonation
        // or a renewal can come back refused with a token nobody holds any more; that says nothing
        // about the session there is now.
        if (store.AccessToken == refused) store.EndSession();
    }

    /// <summary>A renewal's answer: a token to use, or whether the session is over.</summary>
    /// <param name="Token">The token to send, or null.</param>
    /// <param name="SessionOver">The API refused the refresh token, or there is none: sign out.
    /// False when the API could not answer — the session is kept, and the outage reported as one.</param>
    public readonly record struct Renewal(string? Token, bool SessionOver);

    private async Task<Renewal> RenewAsync(string seen, bool force, CancellationToken ct)
    {
        await store.RenewalGate.WaitAsync(ct);
        try
        {
            // Another request renewed while this one waited: use what it got.
            if (!string.IsNullOrWhiteSpace(store.AccessToken) && store.AccessToken != seen)
                return new Renewal(store.AccessToken, false);
            if (!force && store.AccessTokenExpiresAtUtc is { } expires && expires > DateTimeOffset.UtcNow + Margin)
                return new Renewal(store.AccessToken, false);
            if (string.IsNullOrWhiteSpace(store.RefreshToken)) return new Renewal(null, SessionOver: true);

            var outcome = await identity.TryRefreshAsync(store.RefreshToken, ct);
            if (outcome.Tokens is not { } renewed) return new Renewal(null, SessionOver: outcome.Refused);

            store.AccessToken = renewed.AccessToken;
            if (!string.IsNullOrWhiteSpace(renewed.RefreshToken)) store.RefreshToken = renewed.RefreshToken;
            store.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, renewed.ExpiresIn - 30));
            store.NotifyStateChanged();   // MainLayout persists it, so a reload keeps the renewed session
            return new Renewal(renewed.AccessToken, false);
        }
        finally
        {
            store.RenewalGate.Release();
        }
    }
}

/// <summary>
/// The HTTP end of <see cref="SessionRenewer"/>: on the website's API client, every request that
/// carries a session goes through here.
/// </summary>
/// <remarks>
/// A message handler outlives the circuit that sent a request (IHttpClientFactory builds handlers in
/// its own scope), so the circuit's renewer rides on the request itself, under <see cref="Key"/>.
/// A request without one — anonymous, or from outside a circuit — passes straight through.
/// </remarks>
public sealed class SessionRenewalHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<SessionRenewer> Key = new("ben.session-renewer");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!request.Options.TryGetValue(Key, out var renewer))
            return await base.SendAsync(request, ct);

        var token = await renewer.TokenToSendAsync(ct);
        if (token is null) return await base.SendAsync(request, ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        var (renewed, sessionOver) = await renewer.RenewAfterRefusalAsync(token, ct);
        if (renewed is null)
        {
            // Refused, or no refresh token: sign out. Unanswered: keep the session; this request
            // reports the refusal it got, and the next one tries again.
            if (sessionOver) renewer.End(token);
            return response;
        }

        // Sent again only when that is safe: a request with no body can be repeated as it was. One
        // with a body may already have been read; it gets the refusal, and the renewed session is
        // there for the person's next try.
        if (request.Content is not null) return response;

        var again = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
            again.Headers.TryAddWithoutValidation(header.Key, header.Value);
        again.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        response.Dispose();

        var retried = await base.SendAsync(again, ct);
        if (retried.StatusCode == HttpStatusCode.Unauthorized) renewer.End(renewed);
        return retried;
    }
}
