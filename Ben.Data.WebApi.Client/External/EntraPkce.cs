using System.Security.Cryptography;
using System.Text;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// One authorization-code exchange's proof: a secret this client keeps, and the digest of it that
/// travels in the browser.
/// </summary>
/// <remarks>
/// <para>Proof Key for Code Exchange (RFC 7636) is what makes an authorization code safe to hand
/// to a desktop app. Without it, anything that can intercept the redirect — another app claiming
/// the same URL scheme, a browser extension, a log — can redeem the code for a token. With it, the
/// code is worthless to anyone who does not hold the verifier, which never leaves the process.</para>
///
/// <para>A public client cannot use a client secret instead. There is nowhere on a machine the
/// user controls to keep one, and a secret shipped inside an app is not a secret.</para>
/// </remarks>
public sealed record EntraPkce(string CodeVerifier, string CodeChallenge)
{
    /// <summary>The only challenge method worth using; the spec's "plain" is no proof at all.</summary>
    public const string Method = "S256";

    /// <summary>Generates a fresh verifier and its challenge.</summary>
    /// <remarks>
    /// 32 random bytes, base64url-encoded, lands at 43 characters — the shortest length RFC 7636
    /// allows. The randomness must come from a cryptographic source: a verifier an attacker can
    /// predict is a verifier they can use.
    /// </remarks>
    public static EntraPkce Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new EntraPkce(verifier, challenge);
    }

    /// <summary>
    /// Base64 with the URL-safe alphabet and no padding.
    /// </summary>
    /// <remarks>
    /// Ordinary base64 uses '+' and '/', which change meaning in a query string, and '=' padding,
    /// which RFC 7636 forbids here. Encoding this wrong produces a challenge the server computes
    /// differently, and the only symptom is every sign-in being refused as an invalid grant.
    /// </remarks>
    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
