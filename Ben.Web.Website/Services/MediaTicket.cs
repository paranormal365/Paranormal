namespace Ben.Web.Website.Services;

/// <summary>
/// Lets the browser fetch a file it is entitled to, without the server holding the file.
/// </summary>
/// <remarks>
/// <para><b>The problem this solves.</b> Previews used to be fetched by the SERVER and handed to
/// the page as base64 data URLs — a copy of every file in the website's memory, per card, per
/// render. A media library of recordings took the process to sixteen gigabytes and the host was
/// killed. The obvious fix, pointing an <c>&lt;img&gt;</c> straight at the API, works only for
/// PUBLIC files: the browser has no bearer token, the API answers 401, and the page then reports
/// a perfectly healthy file as broken.</para>
///
/// <para><b>Why a ticket.</b> The access token lives in localStorage and is loaded into the
/// Blazor circuit — a plain image request carries none of it. So the circuit, which does know who
/// the viewer is, mints a ticket the browser can put in a URL. The website's media endpoint
/// unprotects it, calls the API with that viewer's own token, and streams the reply straight
/// through.</para>
///
/// <para><b>What keeps it safe.</b> The ticket is an opaque handle — it carries no payload at
/// all, so there is nothing in the URL to read, log or replay without the server that issued it.
/// It is bound to ONE file id, so a ticket lifted from one image cannot fetch another. It
/// expires. And the API is still the authority: redeeming a ticket yields the caller's identity,
/// it never asserts what they may see.</para>
///
/// <para><b>It used to be the token itself, encrypted.</b> That was unreadable but vast — 2504
/// characters, past the 2048 IIS allows in a query string, so IIS answered 404.15 before the
/// application saw the request and profile photos silently vanished on the deployed site while
/// working on localhost. See <see cref="BrowserTicketStore"/> for why a handle replaced it and
/// what that costs (item 201).</para>
/// </remarks>
public sealed class MediaTicketService
{
    private readonly BrowserTicketStore _store;

    /// <summary>Keeps media handles from ever being redeemable as upload handles.</summary>
    private const string Scope = "media";

    /// <summary>
    /// Long enough to view a page and scroll it; short enough that a leaked URL is worthless.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public MediaTicketService(BrowserTicketStore store) => _store = store;

    /// <summary>Mints a ticket for one file, for the caller holding this token.</summary>
    /// <remarks>
    /// The store derives a stable handle for the same viewer, file and hour, so the URL does not
    /// change between renders and the browser can cache the image — the same property the
    /// rounded-down expiry gave the encrypted version, for the same reason.
    /// </remarks>
    public string Protect(Guid fileId, string accessToken)
        => _store.Issue(Scope, fileId, accessToken, Lifetime);

    /// <summary>
    /// Reads a ticket back, returning the access token when it is valid for this file.
    /// </summary>
    /// <returns>Null when the ticket is unknown, expired, or was minted for another file.</returns>
    public string? Unprotect(Guid fileId, string ticket)
        => _store.Redeem(Scope, fileId, ticket);
}
