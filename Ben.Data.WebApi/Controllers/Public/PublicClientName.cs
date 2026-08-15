using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The one place that decides what name a case's client is given on public surfaces.
/// </summary>
/// <remarks>
/// <para>Two levers exist and they are not equal. <see cref="Case.ClientDisplayAlias"/> is the
/// client's own stated preference; <see cref="Case.PublicPseudonym"/> is one the organization
/// chose on their behalf. The client's wins, because an org picking a name for someone who has
/// already picked their own is overriding a decision that was never theirs to make.</para>
///
/// <para>Falling back to null rather than the real name is deliberate and pre-existing: a case
/// with no pseudonym at all publishes anonymously instead of quietly exposing whoever reported
/// it. This helper preserves that — every branch returns either a chosen name or nothing, and a
/// real name is never one of the outcomes.</para>
///
/// <para>It lives on its own so the case page and the discovery list cannot drift apart. They are
/// two endpoints showing the same person, and a client who anonymised themselves on one and not
/// the other has not been anonymised at all.</para>
/// </remarks>
internal static class PublicClientName
{
    /// <summary>The name to publish for this case's client, or null to show none.</summary>
    internal static string? For(Case c)
        => Clean(c.ClientDisplayAlias) ?? Clean(c.PublicPseudonym);

    /// <summary>Flag-only overload for callers projecting the two columns out of a query.</summary>
    internal static string? For(string? clientDisplayAlias, string? publicPseudonym)
        => Clean(clientDisplayAlias) ?? Clean(publicPseudonym);

    /// <summary>Whitespace is not a choice — treat it as unset rather than publishing blanks.</summary>
    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
