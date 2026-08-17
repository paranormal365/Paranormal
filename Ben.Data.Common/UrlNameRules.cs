using System.Text.RegularExpressions;

namespace Ben.Data.Common;

/// <summary>
/// What an organization is allowed to choose as its address.
/// </summary>
/// <remarks>
/// <para><b>Typed, not generated.</b> Unlike a case or an investigation slug, which is derived from
/// a title the organization already made public, <c>UrlName</c> is free text somebody enters — and
/// it was going straight into the database with nothing but a trim and a lowercase. Any character
/// at all was accepted, so <c>ghost squad</c>, <c>a/b</c> and <c>..</c> were all storable, and each
/// produces an address that is broken, ambiguous or both.</para>
///
/// <para><b>The shape is deliberately narrow</b>: lowercase letters, digits and single hyphens,
/// starting and ending with a letter or digit. That is the intersection of what reads well, what
/// survives being pasted into a message, and what needs no escaping anywhere — no percent-encoding
/// in the address bar, no ambiguity in a path segment, nothing that changes meaning when a mail
/// client linkifies it.</para>
///
/// <para>Lives in <c>Ben.Data.Common</c> so the Blazor form can say no before the round trip and the
/// API can say no regardless — the browser check is a courtesy, and the server one is the rule.</para>
/// </remarks>
public static class UrlNameRules
{
    /// <summary>Shortest and longest an address may be.</summary>
    public const int MinLength = 2;
    public const int MaxLength = 100;

    private static readonly Regex Shape = new(
        "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// Words an organization cannot take as its address, because the site routes them itself.
    /// </summary>
    /// <remarks>
    /// Organization pages live under <c>/o/</c>, so these do not collide with top-level routes
    /// today. They are refused anyway: <c>/o/api</c> or <c>/o/admin</c> reads as ours rather than
    /// theirs, and an address that looks official is worth denying whether or not it currently
    /// resolves. Cheap now; taking one back after a group has printed it is not.
    /// </remarks>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "api", "app", "assets", "auth", "billing", "blog", "content",
        "dashboard", "help", "images", "login", "logout", "media", "new", "null", "o", "profile",
        "public", "register", "root", "search", "settings", "signin", "signup", "static",
        "superadmin", "support", "system", "undefined", "user", "users", "www",
    };

    /// <summary>
    /// The reason this address cannot be used, or null when it is fine.
    /// </summary>
    /// <remarks>
    /// Returns prose rather than a boolean because every one of these is a thing a person typed and
    /// can fix, and "invalid" tells them only that they were wrong. Uniqueness is not checked here —
    /// that needs the database, and it belongs with the write.
    /// </remarks>
    public static string? RefusalFor(string? urlName)
    {
        var slug = SlugText.Normalize(urlName);

        if (slug is null)
            return "A web address is required.";

        if (slug.Length < MinLength)
            return $"A web address needs at least {MinLength} characters.";

        if (slug.Length > MaxLength)
            return $"A web address can be at most {MaxLength} characters.";

        if (!Shape.IsMatch(slug))
            return "A web address can use lowercase letters, numbers and hyphens only — for example "
                 + "\"ghost-squad\". It cannot start or end with a hyphen, or contain spaces, "
                 + "slashes or punctuation.";

        if (Reserved.Contains(slug))
            return $"\"{slug}\" is reserved for the site itself. Try something like \"{slug}-team\".";

        return null;
    }

    /// <summary>Whether this address is one an organization may take.</summary>
    public static bool IsAllowed(string? urlName) => RefusalFor(urlName) is null;
}
