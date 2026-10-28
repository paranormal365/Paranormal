using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Ben.Data.WebApi.Services.Files;

/// <summary>
/// The site's seal on a certificate of authenticity (10/28/2026), made with the API's own
/// data-protection keys — the ring already kept on disk so sign-ins survive a restart — so there is
/// no separate secret to configure. A row changed in the database after it was issued no longer checks.
/// </summary>
public sealed class ProvenanceSigner(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Ben.FileProvenance.v1");

    public enum Check { Valid, Altered, Unsigned, Unreadable }

    public string Sign(string canonical) => _protector.Protect(Digest(canonical));

    public Check Verify(string canonical, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return Check.Unsigned;
        try
        {
            return _protector.Unprotect(signature) == Digest(canonical) ? Check.Valid : Check.Altered;
        }
        catch (CryptographicException)
        {
            // Not this site's seal, or one made with a key the ring no longer holds.
            return Check.Unreadable;
        }
    }

    private static string Digest(string canonical)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
