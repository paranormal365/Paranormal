using Ben.Web.Services.WebApi;

namespace Ben.Web.Website.Services;

/// <summary>
/// Who a media request is made for, and how to get them a token that still works.
/// </summary>
/// <remarks>
/// <para><b>Why (10/28/2026).</b> A ticket used to carry the access token as it was when the page
/// drew the link. The token lasts an hour and is renewed only when the page next talks to the API,
/// and a session player that is simply playing talks to nobody: so a recording could start
/// answering "not signed in" partway through a long night, while every other part of the page
/// still showed the person signed in.</para>
///
/// <para>Now a ticket keeps hold of the page's own token source. A request is sent with whatever
/// token the page holds by then, renewed first if it is about to run out, and a refusal is renewed
/// once and tried again — the same way the page's own API calls already behave
/// (<see cref="SessionRenewalHandler"/>). Only while the same person is signed in on that page:
/// after a sign-out or a switch to somebody else, the ticket falls back to the token it was made
/// with, and the API decides as it always did.</para>
/// </remarks>
public sealed class MediaCaller
{
    public MediaCaller(string token, IMediaTokenSource? source)
    {
        Token = token;
        Source = source;
    }

    /// <summary>The token to send.</summary>
    public string Token { get; }

    /// <summary>Where a fresher token comes from, when the ticket came from a signed-in page.</summary>
    public IMediaTokenSource? Source { get; }

    /// <summary>A bare token, from a caller with nothing to renew it with.</summary>
    public static implicit operator MediaCaller?(string? token)
        => string.IsNullOrWhiteSpace(token) ? null : new MediaCaller(token, null);
}

/// <summary>A page's token, current and renewable, for the media it links to.</summary>
public interface IMediaTokenSource
{
    /// <summary>The token to send now, renewed first if it is about to run out. Null: use the ticket's own.</summary>
    Task<string?> CurrentAsync(CancellationToken ct);

    /// <summary>The API refused <paramref name="refused"/>: a renewed token, or null when there is none.</summary>
    Task<string?> RenewedAfterRefusalAsync(string refused, CancellationToken ct);
}

/// <summary>
/// The signed-in page's tokens, by reference: what a ticket asks when its file is fetched.
/// </summary>
/// <remarks>
/// Holds the page's token store and renewer the way <see cref="SessionRenewalHandler"/> does for
/// API calls that outlive the moment they were made. Answers only for the person signed in when the
/// link was drawn.
/// </remarks>
public sealed class CircuitMediaTokenSource(IWebApiTokenStore store, SessionRenewer renewer) : IMediaTokenSource
{
    private readonly Guid? _for = store.UserId;

    private bool SamePerson => store.UserId == _for && !string.IsNullOrWhiteSpace(store.AccessToken);

    public async Task<string?> CurrentAsync(CancellationToken ct)
        => SamePerson ? await renewer.TokenToSendAsync(ct) : null;

    public async Task<string?> RenewedAfterRefusalAsync(string refused, CancellationToken ct)
    {
        if (!SamePerson) return null;
        var renewal = await renewer.RenewAfterRefusalAsync(refused, ct);
        return renewal.Token;
    }
}
