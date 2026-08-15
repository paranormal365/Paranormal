using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The anti-spam checks for the public contact form.
/// </summary>
/// <remarks>
/// <para>Three cheap layers before anything external. In order of what they cost the sender:
/// a honeypot they never see, a form token proving the page was open long enough for a person to
/// type, and rate limits by address and by IP.</para>
///
/// <para>No CAPTCHA. It would be the public site's first third-party runtime dependency, and these
/// three catch the traffic a site this size attracts. If they stop being enough, a CAPTCHA belongs
/// on top of them rather than instead of them.</para>
/// </remarks>
public sealed class SupportFormGuard
{
    /// <summary>A form filled faster than this was not filled by a person reading it.</summary>
    public static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(3);

    /// <summary>How long an issued form token stays usable — a page left open all day still works.</summary>
    public static readonly TimeSpan FormTokenLifetime = TimeSpan.FromHours(6);

    public const int MaxPerEmailPerDay = 5;
    public const int MaxPerIpPerHour = 3;

    private readonly IDataProtector _protector;
    private readonly byte[] _ipHashKey;

    public SupportFormGuard(IDataProtectionProvider provider, IConfiguration configuration)
    {
        // Purpose-scoped: a token minted here cannot be replayed against any other protected
        // payload in the app, and vice versa.
        _protector = provider.CreateProtector("Ben.Support.ContactForm.v1");

        // Deliberately NOT derived from IDataProtector: Protect() is non-deterministic, so hashing
        // its output would produce a different value for the same address every single call and
        // the rate limit would never match anything. A keyed HMAC is the deterministic primitive
        // this needs. Changing the key only resets rate-limit history, which is harmless.
        var configured = configuration["Support:IpHashKey"];
        _ipHashKey = Encoding.UTF8.GetBytes(
            string.IsNullOrWhiteSpace(configured) ? "ben-support-ip-hash-v1" : configured);
    }

    /// <summary>Mints the token handed to a freshly rendered form.</summary>
    public string IssueFormToken(DateTimeOffset now)
        => _protector.Protect(now.ToUnixTimeMilliseconds().ToString());

    /// <summary>
    /// Checks a submitted token: ours, not expired, and old enough that a person could have typed
    /// in the meantime.
    /// </summary>
    /// <remarks>
    /// Signed rather than a plain timestamp, so the clock cannot be moved by the client. Any
    /// failure to unprotect — tampering, a rotated key, gibberish — is treated as a failed check
    /// rather than an exception, because from here they are the same thing.
    /// </remarks>
    public FormTokenResult ValidateFormToken(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token)) return FormTokenResult.Missing;

        string raw;
        try { raw = _protector.Unprotect(token); }
        catch { return FormTokenResult.Invalid; }

        if (!long.TryParse(raw, out var issuedMs)) return FormTokenResult.Invalid;

        var issued = DateTimeOffset.FromUnixTimeMilliseconds(issuedMs);
        var age = now - issued;

        if (age < TimeSpan.Zero) return FormTokenResult.Invalid;   // issued in the future
        if (age > FormTokenLifetime) return FormTokenResult.Expired;
        if (age < MinimumFillTime) return FormTokenResult.TooFast;

        return FormTokenResult.Valid;
    }

    /// <summary>
    /// Salted hash of a caller's IP, for rate limiting.
    /// </summary>
    /// <remarks>
    /// Keyed, not a bare SHA-256: the IPv4 space is small enough to enumerate completely, so an
    /// unsalted hash of an address is trivially invertible and "we only store a hash" would be an
    /// empty claim. With a secret key it is not.
    /// </remarks>
    public string? HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var hash = HMACSHA256.HashData(_ipHashKey, Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(hash)[..32];
    }

    /// <summary>True when the hidden field a person never sees came back filled in.</summary>
    public static bool IsHoneypotTripped(string? honeypotValue)
        => !string.IsNullOrWhiteSpace(honeypotValue);
}

/// <summary>Outcome of checking a submitted form token.</summary>
public enum FormTokenResult
{
    Valid,
    Missing,
    Invalid,
    Expired,

    /// <summary>Submitted sooner after rendering than a person could have typed it.</summary>
    TooFast,
}
