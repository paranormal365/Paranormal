using Ben.Data.Common.Enums;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// What <c>GET api/me</c> answers: who the bearer token belongs to and what site-wide roles they
/// hold.
/// </summary>
/// <remarks>
/// <para>This is the only way a client learns its own roles. Identity's bearer tokens are opaque
/// and data-protected, not JWTs, so there is nothing in the token to decode — a client that tries
/// gets nothing and silently believes the person is an ordinary member.</para>
///
/// <para>Ask again after every token acquisition, including a refresh that followed an
/// impersonation change. Matches <c>MeController.MeResponse</c> on the server.</para>
/// </remarks>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    bool IsSuperAdmin,
    bool IsAdmin,
    bool IsModerator = false,
    EmailAddressKind EmailKind = EmailAddressKind.Ordinary)
{
    /// <summary>Whether this address is one the person chose and reads as their own.</summary>
    /// <remarks>
    /// False for an Apple relay and for the placeholder a withheld address leaves behind. Showing
    /// either as "your email" is telling somebody something untrue about themselves.
    /// </remarks>
    public bool EmailIsTheirOwn => EmailKind == EmailAddressKind.Ordinary;

    /// <summary>Whether anything sent to this address can arrive at all.</summary>
    /// <remarks>
    /// A relay is deliverable only from a sender domain registered with Apple, and until that is
    /// done Apple drops the message without a bounce. A placeholder can never be delivered to. So
    /// this answers "may we honestly say we have emailed them", and the answer for a relay depends
    /// on configuration this client cannot see — which is why a relay is treated as unreachable
    /// here rather than optimistically.
    /// </remarks>
    public bool CanBeEmailed => EmailKind == EmailAddressKind.Ordinary;
}
