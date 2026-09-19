using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// One-minute, one-use codes that carry a signed-in identity from the site to the standalone
/// editor.
/// </summary>
/// <remarks>
/// <para><b>Why a code at all.</b> The site holds the person's tokens in the Blazor circuit, on
/// the server, where the browser cannot read them — that is the point of holding them there. The
/// standalone editor is a different origin running in the browser, and it needs tokens of its own.
/// Handing the site's tokens across would defeat the arrangement and put a refresh token into page
/// script; so instead the site asks for a code that proves nothing on its own, and the editor
/// exchanges it for tokens minted freshly for it.</para>
///
/// <para><b>Why in memory.</b> The window is sixty seconds. A row in the database would outlive
/// its usefulness by orders of magnitude, need a migration against a live database, and need
/// sweeping. An API restart drops every outstanding code, which costs somebody one extra click on
/// a link that has been alive for under a minute.</para>
///
/// <para><b>What is stored is a hash</b>, not the code. The code exists in the response to the
/// issuing call and in the link the person clicks; nothing that can be redeemed is kept here, so
/// a dump of this process's memory yields nothing that opens a session.</para>
/// </remarks>
public sealed class EditorHandoffCodeStore
{
    /// <summary>How long a code is worth anything.</summary>
    /// <remarks>
    /// Long enough to survive a slow page load and a cold WebAssembly start, short enough that a
    /// link left in somebody's clipboard or a chat window is already dead.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Bytes of randomness in a code. 32 bytes is 256 bits — unguessable inside a minute by any
    /// margin worth arguing about.
    /// </summary>
    private const int CodeBytes = 32;

    private readonly ConcurrentDictionary<string, Entry> _codes = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    public EditorHandoffCodeStore(Func<DateTimeOffset>? now = null)
        => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Number of codes outstanding. For tests and diagnostics.</summary>
    public int Count => _codes.Count;

    /// <summary>
    /// Issues a code standing for <paramref name="appUserId"/>.
    /// </summary>
    /// <returns>The code itself, which is not stored and cannot be recovered from this store.</returns>
    public string Issue(Guid appUserId)
    {
        // Cheap opportunistic sweep. Codes are only created by signed-in callers on a rate-limited
        // endpoint, but nothing else ever removes an unredeemed one, and a dictionary that only
        // grows is a slow leak in a process that runs for months.
        PruneExpired();

        var code = Base64Url(RandomNumberGenerator.GetBytes(CodeBytes));

        _codes[Hash(code)] = new Entry(appUserId, _now().Add(Lifetime));

        return code;
    }

    /// <summary>
    /// Redeems a code, returning the account it stands for.
    /// </summary>
    /// <returns>
    /// Null when the code is unknown, already used, or expired. The three are deliberately
    /// indistinguishable to the caller: telling them apart tells a guesser which guesses are
    /// closer.
    /// </returns>
    public Guid? Redeem(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Removed whether or not it turns out to be expired: one attempt is all a code gets, so a
        // replay of a valid code finds nothing the second time.
        if (!_codes.TryRemove(Hash(code), out var entry)) return null;

        return _now() < entry.ExpiresAt ? entry.AppUserId : null;
    }

    private void PruneExpired()
    {
        var now = _now();

        foreach (var (key, entry) in _codes)
            if (now >= entry.ExpiresAt)
                _codes.TryRemove(key, out _);
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));

    /// <summary>Base64 without the three characters that need escaping in a URL fragment.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private readonly record struct Entry(Guid AppUserId, DateTimeOffset ExpiresAt);
}
