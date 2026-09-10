using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Ben.Web.Website.Services;

/// <summary>
/// Carries an Apple identity token the few seconds between the endpoint Apple posts to and the
/// Blazor page that finishes the sign-in.
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> Apple answers with a form post to a plain HTTP endpoint,
/// and a plain endpoint has no Blazor circuit — so it cannot put a session anywhere a page can see.
/// The page has to do the API call itself, exactly as the password form does, which means the token
/// has to cross from one to the other. It crosses as an opaque code, and the token itself never
/// appears in a URL, a log, or a browser history entry.</para>
///
/// <para><b>Single use and short lived</b>, for the same reason the API's editor handoff is: a
/// credential that can be redeemed twice is a credential that can be redeemed by somebody else.</para>
///
/// <para>In memory, so a restart loses any in-flight sign-in. That is the correct trade: the cure
/// is pressing the button again, and persisting Apple identity tokens to buy a smoother failure
/// would be storing bearer credentials for no good reason.</para>
/// </remarks>
public sealed class AppleSignInHandoff
{
    /// <summary>
    /// Long enough to survive a redirect and a page render, short enough that a leaked code is
    /// already dead.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    public AppleSignInHandoff(Func<DateTimeOffset>? now = null)
        => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <param name="DisplayName">Apple's one-shot name, when this was a first authorization.</param>
    public sealed record Pending(string IdentityToken, string? DisplayName);

    private sealed record Entry(Pending Value, DateTimeOffset ExpiresAt);

    /// <summary>Stashes a token and returns the code that redeems it.</summary>
    public string Stash(string identityToken, string? displayName)
    {
        Sweep();

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _entries[code] = new Entry(new Pending(identityToken, displayName), _now().Add(Lifetime));
        return code;
    }

    /// <summary>
    /// Redeems a code, once. Answers null for a code that is unknown, spent or expired.
    /// </summary>
    /// <remarks>
    /// All three are one answer on purpose. Telling them apart would say whether a code ever
    /// existed, and the page has the same thing to do either way: send them back to start again.
    /// </remarks>
    public Pending? Redeem(string? code)
    {
        Sweep();

        if (string.IsNullOrWhiteSpace(code)) return null;
        if (!_entries.TryRemove(code, out var entry)) return null;

        return entry.ExpiresAt <= _now() ? null : entry.Value;
    }

    /// <summary>Drops anything expired, so an abandoned sign-in does not sit in memory holding a token.</summary>
    private void Sweep()
    {
        var now = _now();
        foreach (var pair in _entries)
            if (pair.Value.ExpiresAt <= now)
                _entries.TryRemove(pair.Key, out _);
    }

    /// <summary>How many codes are outstanding. For tests.</summary>
    internal int Count => _entries.Count;
}
