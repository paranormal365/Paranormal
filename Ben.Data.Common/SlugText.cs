namespace Ben.Data.Common;

/// <summary>
/// The one definition of how a URL slug is written down and compared.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Organizations were created through two paths that disagreed: the
/// admin endpoint lowercased the URL name, registration only trimmed it. Readers then variously
/// lowercased the incoming segment or did not. On SQL Server's default case-insensitive collation
/// that mostly works, which is the dangerous part — the behaviour was correct by accident of
/// database configuration rather than by anything in the code, and it changes under a
/// case-sensitive collation or the InMemory provider the tests use.</para>
///
/// <para><b>Normalize on write and on read.</b> Stored slugs are lowercase, and every lookup
/// lowercases what it was given. The comparison is then plain equality that means the same thing
/// everywhere, rather than a question about collation.</para>
///
/// <para>Lives in <c>Ben.Data.Common</c> because both the WebApi and the repository layer write
/// slugs, and a rule only one of them knows is a rule the other will break.</para>
/// </remarks>
public static class SlugText
{
    /// <summary>
    /// A slug as it is stored and compared: trimmed, lowercased, invariant.
    /// </summary>
    /// <remarks>
    /// Invariant rather than current-culture, because the Turkish dotless ı famously makes
    /// <c>"I".ToLower()</c> culture-dependent — and a URL that resolved differently depending on the
    /// server's locale would be a genuinely miserable thing to diagnose.
    /// </remarks>
    public static string? Normalize(string? slug)
        => string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant();

    /// <summary>Normalizes, falling back to empty so a lookup misses rather than matching everything.</summary>
    public static string NormalizeOrEmpty(string? slug) => Normalize(slug) ?? string.Empty;
}
