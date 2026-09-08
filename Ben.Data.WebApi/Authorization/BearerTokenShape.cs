namespace Ben.Data.WebApi.Authorization;

/// <summary>
/// Whether a bearer token could possibly be a JWT, decided by its shape alone.
/// </summary>
/// <remarks>
/// <para><b>W-A16 of the 2026-09-06 evaluation.</b> Every API request logged two warnings —
/// "Failed to validate the token" and "Entra was not authenticated. Failure message: IDX14100: JWT
/// is not well formed" — 188,000 lines in one afternoon. Nothing was wrong. The default
/// authorization policy accepts either the local Identity bearer scheme or the Entra JWT scheme,
/// and a policy listing two schemes runs BOTH: every ordinary sign-in's Identity token was handed
/// to the Entra handler, which correctly reported that it is not a JWT, at Warning, twice.</para>
///
/// <para>A log in that state cannot show a real fault, which is the same conclusion
/// <see cref="Logging.LogNoise"/> reached about a different flood. The difference is that this one
/// is answerable before it happens: an Identity bearer token is a single data-protection blob and
/// carries no dots at all, so the handler can decline the token instead of failing on it.</para>
///
/// <para><b>Deliberately shape-only.</b> It does not decode, verify or trust anything — a token
/// that passes here still goes through the full Entra validation unchanged. The only question is
/// whether it is worth handing over, and answering it wrongly in the permissive direction costs a
/// log line, while answering it wrongly in the strict direction would break Entra sign-in. Hence
/// both JWT serialisations are accepted, not just the common one.</para>
/// </remarks>
public static class BearerTokenShape
{
    /// <summary>A signed JWT (JWS) is header.payload.signature — two dots.</summary>
    private const int JwsDots = 2;

    /// <summary>
    /// An encrypted JWT (JWE) is header.key.iv.ciphertext.tag — four dots. Entra does not issue
    /// these for a custom API audience today, and accepting them costs nothing if it ever does.
    /// </summary>
    private const int JweDots = 4;

    /// <summary>
    /// Whether this token is worth handing to a JWT handler at all.
    /// </summary>
    /// <param name="token">The raw bearer token, without the "Bearer " prefix.</param>
    public static bool CouldBeAJwt(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var dots = token.Count(c => c == '.');
        if (dots != JwsDots && dots != JweDots) return false;

        // ".." would count as two dots and is not a JWT. Every segment has to hold something,
        // or the "well formed" complaint this exists to avoid is the correct answer.
        foreach (var segment in token.Split('.'))
            if (segment.Length == 0) return false;

        return true;
    }
}
