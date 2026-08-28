namespace Ben.Data.Common.Constants;

/// <summary>
/// What a closed account looks like, in one place, so every side agrees.
/// </summary>
/// <remarks>
/// <para>Closing an account anonymises it rather than deleting the row. The person's identity,
/// credentials and contact details go; the cases, evidence, reports and messages they authored
/// stay where their group left them, now attributed to a name that is nobody. Deleting the row
/// would take a group's case history with it, which is a worse answer to "remove my data" than
/// the one that removes the data and keeps the work.</para>
///
/// <para>The website, the API and the iOS app all need to recognise the same shape, so the two
/// values that make it recognisable live here rather than being typed out three times.</para>
/// </remarks>
public static class AccountClosure
{
    /// <summary>
    /// The display name a closed account carries.
    /// </summary>
    /// <remarks>
    /// Written into <c>AppUser.DisplayName</c> at closure rather than substituted at render time,
    /// deliberately: dozens of surfaces already read <c>DisplayName ?? Email</c>, and every one of
    /// them shows this correctly without being found and changed. A render-time substitution would
    /// have to be added to each of them, and the one that got missed would show a real name.
    /// </remarks>
    public const string FormerMemberName = "A former member";

    /// <summary>
    /// The domain the anonymised email address is parked on.
    /// </summary>
    /// <remarks>
    /// <para><c>.invalid</c> is reserved by RFC 2606 and can never resolve, so nothing can post to
    /// it by accident. Identity requires a unique, non-null <c>UserName</c>, so the row needs
    /// <i>some</i> address; this one carries no information about the person and is safe to
    /// display if a surface somewhere shows an email without falling back to the display name.</para>
    /// </remarks>
    public const string ClosedEmailDomain = "removed.invalid";

    /// <summary>The anonymised address for a given account id.</summary>
    public static string ClosedEmailFor(Guid userId) => $"closed-{userId:N}@{ClosedEmailDomain}";

    /// <summary>Whether an email address is one of ours for a closed account.</summary>
    public static bool IsClosedEmail(string? email) =>
        email is not null && email.EndsWith('@' + ClosedEmailDomain, StringComparison.OrdinalIgnoreCase);
}
