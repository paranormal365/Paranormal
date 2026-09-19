using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Web.Website.Services;

/// <summary>
/// Hands the browser a short opaque handle for something only the server should hold.
/// </summary>
/// <remarks>
/// <para><b>What this replaces, and why.</b> Media and upload tickets used to be the viewer's API
/// access token encrypted into the URL. Encryption made it unreadable, and it was bound to one id
/// and expired — but it was also <i>enormous</i>. A real media ticket measured 2504 characters,
/// and IIS refuses a query string over 2048 with 404.15 <b>before the request reaches the
/// application</b>. Nothing of ours logged it, an IIS error page came back, and it read as a
/// broken image rather than an oversized URL. Ben's profile photos were invisible on
/// ishaunted.com while working perfectly on localhost, because Kestrel has no such limit (item
/// 201, 2026-08-27).</para>
///
/// <para><b>Raising the limit was not the fix.</b> It unblocked it, and a raised limit is still a
/// limit — crossed by something that grows on its own. One more claim in a JWT and some viewers
/// go back over, silently, in the same invisible way. A URL is also the most-copied, most-logged
/// string in a system: it reaches proxy logs, browser history and <c>Referer</c> headers, and an
/// encrypted token nobody can read is still one anybody can replay until it expires.</para>
///
/// <para><b>So the token stops travelling.</b> The handle is 43 characters against 2504, carries
/// no payload at all, and is worthless to anyone without the server that issued it.</para>
///
/// <para><b>Why the handle is derived rather than random.</b> The same viewer asking for the same
/// file must get the same URL on every render, or the browser treats each render as a new image
/// and can never cache the bytes — which was the entire reason for getting them out of the server
/// in the first place. So the handle is a hash of what it stands for, including the hour it was
/// issued in, exactly as the encrypted version rounded its expiry down to the hour for the same
/// reason.</para>
///
/// <para><b>Why in-memory is enough here.</b> A restart empties this and every outstanding handle
/// stops resolving — which sounds worse than it is: this is a Blazor Server app, and a restart has
/// already destroyed every circuit, so the page holding those URLs is gone regardless and its
/// viewer has to reload. The store loses nothing that was not already lost. The trade it buys is
/// that a token never appears in a URL.</para>
/// </remarks>
public sealed class BrowserTicketStore
{
    private readonly IMemoryCache _cache;

    public BrowserTicketStore(IMemoryCache cache) => _cache = cache;

    /// <summary>What a handle stands for. Kept server-side; never serialised to the browser.</summary>
    private sealed record Entry(Guid Id, string AccessToken);

    /// <summary>
    /// Issues a handle for one id and one viewer, valid for <paramref name="lifetime"/>.
    /// </summary>
    /// <param name="scope">
    /// Keeps media and upload handles apart, so one can never be redeemed as the other.
    /// </param>
    public string Issue(string scope, Guid id, string accessToken, TimeSpan lifetime)
    {
        // Rounded down to the hour: the same viewer, the same file, the same hour gives the same
        // handle, so the URL is stable across renders and the browser can cache the bytes.
        var slot = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 * 3600;
        var handle = Derive(scope, id, accessToken, slot);

        // Absolute, not sliding: a handle that renewed itself by being used would outlive the
        // session it belongs to, which is the opposite of what an expiry is for. The extra hour
        // matches the rounding above, so a handle minted at 10:59 still gets its full lifetime.
        _cache.Set(CacheKey(scope, handle), new Entry(id, accessToken),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime + TimeSpan.FromHours(1) });

        return handle;
    }

    /// <summary>
    /// Reads a handle back, returning the access token when it is valid for this id.
    /// </summary>
    /// <returns>
    /// Null when the handle is unknown, expired, or was issued for something else — the same
    /// answer the encrypted version gave, so every caller's handling is unchanged.
    /// </returns>
    public string? Redeem(string scope, Guid id, string handle)
    {
        if (string.IsNullOrWhiteSpace(handle)) return null;
        if (!_cache.TryGetValue(CacheKey(scope, handle), out Entry? entry) || entry is null) return null;

        // Bound to ONE id, exactly as before: a handle lifted from one image cannot fetch another.
        // Checked even though the handle was derived from the id, because the check is what states
        // the rule — deriving it is an optimisation, not the guarantee.
        return entry.Id != id ? null : entry.AccessToken;
    }

    private static string CacheKey(string scope, string handle) => $"ticket:{scope}:{handle}";

    /// <summary>
    /// The handle: a hash of what it stands for, in URL-safe characters.
    /// </summary>
    /// <remarks>
    /// Unguessable because the access token is part of what is hashed — 256 bits of digest over a
    /// secret, so a handle cannot be constructed by anybody who does not already hold the token
    /// it would stand for. Base64url, and the padding is stripped: a handle travels in a query
    /// string, and '=' would only be escaped again by whoever writes it there.
    /// </remarks>
    private static string Derive(string scope, Guid id, string accessToken, long slot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{id:N}|{slot}|{accessToken}"));
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
