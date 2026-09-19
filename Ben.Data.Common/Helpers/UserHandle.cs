using System.Text;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// The rules for an <c>@name</c> — the unique name a person picks when their account is made.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-08-20: <i>"Lets let people choose a unique name to use for the @name when they
/// create their account. We verify it is not already taken."</i> and <i>"For now, we will not let
/// them change their @name."</i></para>
///
/// <para><b>Why a handle rather than matching display names.</b> Display names are not unique here
/// and contain spaces, so <c>@sarahmitchell</c> could only ever be matched by stripping punctuation
/// and hoping exactly one account came back. Two people called Sarah Mitchell would then either
/// both be notified or neither — and the answer would change as accounts are added, so a post's
/// meaning would depend on who else had signed up since. A handle makes a mention resolve to
/// exactly one account, permanently, and survives a display-name change because the mention is
/// stored against the account's id.</para>
///
/// <para>Lives in Common because the same rules are needed in three places: the WebApi validating
/// what somebody typed, the website checking availability as they type, and the feed parser
/// deciding what counts as a mention token.</para>
/// </remarks>
public static class UserHandle
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    /// <summary>
    /// Names nobody may take, because a URL or a mention using them would be ambiguous or
    /// misleading.
    /// </summary>
    /// <remarks>
    /// Two kinds. Route words — an account called <c>admin</c> would sit at a profile URL that
    /// reads like a section of the site. And impersonation risks: <c>support</c> or <c>ishaunted</c>
    /// in a mention is the sort of thing somebody trusts.
    /// </remarks>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "superadmin", "root", "system", "support", "help", "staff",
        "moderator", "mod", "official", "ishaunted", "ishauntedcom", "team", "security",
        "api", "www", "app", "login", "logout", "register", "signup", "signin", "account",
        "settings", "profile", "feed", "tags", "search", "about", "contact", "terms", "privacy",
        "me", "you", "everyone", "here", "all", "null", "undefined", "anonymous", "deleted",
    };

    /// <summary>
    /// Reduces a candidate to the canonical form actually stored: trimmed, lower-cased, with a
    /// leading <c>@</c> removed.
    /// </summary>
    /// <remarks>
    /// Storing the canonical form rather than comparing case-insensitively keeps the unique index
    /// meaningful and lookups seekable. Somebody typing <c>@SarahM</c> gets <c>sarahm</c>, and
    /// nobody else can then take <c>SARAHM</c>.
    /// </remarks>
    public static string Normalize(string? candidate)
        => (candidate ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    /// <summary>
    /// Whether a candidate is a legal handle, and why not when it is not.
    /// </summary>
    /// <param name="candidate">What the person typed. Normalised before checking.</param>
    /// <param name="error">A sentence to show them. Null when the handle is fine.</param>
    /// <remarks>
    /// Availability is <b>not</b> checked here — that needs the database. This answers "could this
    /// ever be a handle", which is the half a browser can answer as somebody types.
    /// </remarks>
    public static bool IsValid(string? candidate, out string? error)
    {
        var handle = Normalize(candidate);

        if (handle.Length == 0)
        {
            error = "Choose a name.";
            return false;
        }

        if (handle.Length < MinLength)
        {
            error = $"Names are at least {MinLength} characters.";
            return false;
        }

        if (handle.Length > MaxLength)
        {
            error = $"Names are at most {MaxLength} characters.";
            return false;
        }

        if (!char.IsLetter(handle[0]))
        {
            // A leading digit makes a handle look like an id, and a leading underscore is the
            // sort of thing used to sit at the top of an alphabetical list.
            error = "Names start with a letter.";
            return false;
        }

        foreach (var c in handle)
        {
            if (!IsAllowed(c))
            {
                error = "Names use letters, numbers and underscores only.";
                return false;
            }
        }

        if (Reserved.Contains(handle))
        {
            error = "That name is reserved.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Letters, digits and underscore — the characters a mention can carry unambiguously.</summary>
    /// <remarks>
    /// No dots or hyphens, deliberately, even though the mention parser tolerates them in a token.
    /// A trailing dot is indistinguishable from a full stop, so <c>@sarah.</c> at the end of a
    /// sentence would be a different handle depending on the punctuation around it.
    /// </remarks>
    private static bool IsAllowed(char c)
        => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';

    /// <summary>
    /// A legal starting point derived from whatever the account already has — a display name, or
    /// the local part of an email.
    /// </summary>
    /// <remarks>
    /// <para>Used where no human is present to choose: an Entra sign-in creating a linked account,
    /// an event magic link, the seeders. The result is a suggestion, not a reservation — the caller
    /// still has to make it unique against the database.</para>
    ///
    /// <para>Falls back to <c>user</c> when there is nothing usable, which is legal and will be
    /// suffixed into <c>user2</c> and so on by the caller. An account with no handle at all is the
    /// one outcome to avoid, because a person who cannot be mentioned is invisible to the feed.
    /// </para>
    /// </remarks>
    public static string Suggest(string? displayName, string? email)
    {
        var seed = FromText(displayName);
        if (seed.Length < MinLength) seed = FromText(EmailLocalPart(email));
        if (seed.Length < MinLength) seed = "user";

        if (seed.Length > MaxLength) seed = seed[..MaxLength];

        // Suggest never returns a reserved word; the caller's uniquifying suffix would otherwise
        // be the only thing standing between "support" and somebody's profile.
        return Reserved.Contains(seed) ? seed + "1" : seed;
    }

    private static string FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var lower = char.ToLowerInvariant(c);
            if (IsAllowed(lower)) builder.Append(lower);
        }

        // Must start with a letter, so drop anything before the first one.
        var result = builder.ToString();
        var firstLetter = result.AsSpan().IndexOfAnyInRange('a', 'z');
        return firstLetter <= 0 ? (firstLetter == 0 ? result : string.Empty) : result[firstLetter..];
    }

    private static string? EmailLocalPart(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}
