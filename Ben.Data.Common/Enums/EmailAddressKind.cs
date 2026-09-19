namespace Ben.Data.Common.Enums;

/// <summary>
/// Whether an account's address is one the person actually chose and can read.
/// </summary>
/// <remarks>
/// <para>Sign in with Apple can produce two addresses that look ordinary and are not. Without this,
/// nothing downstream can tell, so the site presents a machine-generated address as though somebody
/// picked it, and claims to have emailed them when it has not.</para>
///
/// <para>Apple states which case it is in the identity token, once, at the moment the account is
/// created. That answer was previously thrown away.</para>
/// </remarks>
public enum EmailAddressKind
{
    /// <summary>A real address the person gave. Show it, email it.</summary>
    Ordinary = 0,

    /// <summary>
    /// One of Apple's Hide My Email relay addresses.
    /// </summary>
    /// <remarks>
    /// Genuinely deliverable, but ONLY from a sender domain registered with Apple under "Configure
    /// Sign in with Apple for Email Communication". Until that is done, Apple drops the message and
    /// says nothing. It is also not an address they read as theirs, so showing it as their email is
    /// misleading.
    /// </remarks>
    AppleRelay = 1,

    /// <summary>
    /// A placeholder standing in for an address that was never supplied.
    /// </summary>
    /// <remarks>
    /// Apple lets somebody withhold their address entirely, and Identity still needs a unique one,
    /// so the account carries <c>{subject}@appleid.invalid</c>. Nothing can ever be delivered to it
    /// — <c>.invalid</c> is reserved by RFC 2606 precisely so it cannot resolve — and it must never
    /// be shown to anybody as their address.
    /// </remarks>
    Unreachable = 2,
}
