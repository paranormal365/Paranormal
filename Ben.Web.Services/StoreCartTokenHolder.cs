namespace Ben.Web.Services;

/// <summary>
/// This visitor's cart token (storefront S3.4): the value of the HttpOnly <c>ben.cart</c> cookie,
/// which <see cref="WebApi.WebApiClient"/> sends to the API as <c>X-Ben-Cart</c> on store calls.
/// </summary>
/// <remarks>
/// <para>Scoped. On the first, server-rendered request the cookie middleware fills it; the
/// <c>StoreCartTokenPersister</c> writes it into the page and <c>MainLayout</c> reads it back into
/// the circuit's own instance — the same two steps as the Microsoft sign-in token, because a
/// circuit has no <c>HttpContext</c> to read a cookie from.</para>
///
/// <para>The token never reaches JavaScript: the cookie is HttpOnly and the circuit holds the
/// value on the server.</para>
/// </remarks>
public sealed class StoreCartTokenHolder
{
    public string? Token { get; set; }
}

/// <summary>
/// The visitor's own address (storefront S3.4), sent to the API as <c>X-Forwarded-For</c> so its
/// rate limits count each visitor rather than the website as one.
/// </summary>
/// <remarks>
/// Filled the same way as <see cref="StoreCartTokenHolder"/>. The website's own forwarded-headers
/// step trusts only the local tunnel, so this is the address the person actually came from, not
/// one they typed into a header.
/// </remarks>
public sealed class VisitorAddressHolder
{
    public string? Address { get; set; }
}

/// <summary>What the persister writes into the page for the circuit to pick up.</summary>
public sealed record SerializedStoreVisitor(string? CartToken, string? Address);
