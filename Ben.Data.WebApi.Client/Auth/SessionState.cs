using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>Where a client is in the business of signing somebody in.</summary>
public enum SessionPhase
{
    /// <summary>Nobody is signed in. Anonymous surfaces still work.</summary>
    SignedOut,

    /// <summary>A sign-in is in flight.</summary>
    Authenticating,

    /// <summary>
    /// The password was accepted and a second factor is needed. Not a failure, and must not be
    /// shown as one.
    /// </summary>
    TwoFactorChallenge,

    /// <summary>Tokens are in hand; asking the server who they belong to.</summary>
    FetchingIdentity,

    /// <summary>Signed in, with roles resolved.</summary>
    SignedIn,

    /// <summary>
    /// Microsoft accepted them, but no account here is linked to that identity yet, so they must
    /// either create one or attach it to an account they already have.
    /// </summary>
    /// <remarks>
    /// The server signals this by answering <c>GET api/me</c> with a UserId of
    /// <see cref="System.Guid.Empty"/> rather than with a refusal — the token is perfectly valid,
    /// there is simply nobody here it belongs to. Treating that as signed in would put somebody in
    /// front of an app with no account behind it, and every page would refuse them for reasons
    /// none of them could explain.
    /// </remarks>
    NeedsLocalAccount,
}

/// <summary>
/// The whole of what a screen needs to know about the session, as one value.
/// </summary>
/// <param name="Failure">
/// Why the last attempt did not succeed, or null. Present alongside <see cref="SessionPhase.SignedOut"/>
/// and <see cref="SessionPhase.TwoFactorChallenge"/> both — the second factor being required is
/// carried here as well so a screen has one place to look.
/// </param>
/// <param name="RetryAfter">
/// How long the server asked the caller to wait, when it answered 429. A countdown is the honest
/// thing to show; inviting an immediate retry is not.
/// </param>
/// <param name="SessionEndedUnexpectedly">
/// True when the session ended without the person asking. Deserves a banner. A deliberate sign-out
/// never sets it.
/// </param>
public sealed record SessionState(
    SessionPhase Phase,
    MeResponse? Me = null,
    LoginFailure? Failure = null,
    TimeSpan? RetryAfter = null,
    bool SessionEndedUnexpectedly = false)
{
    public static readonly SessionState SignedOut = new(SessionPhase.SignedOut);

    public bool IsSignedIn => Phase == SessionPhase.SignedIn;

    /// <summary>Whether an external sign-in is waiting for an account to be created or linked.</summary>
    public bool NeedsLocalAccount => Phase == SessionPhase.NeedsLocalAccount;

    /// <summary>
    /// The address the external provider gave, when one is waiting for an account. Carried on
    /// <see cref="Me"/>, whose UserId is empty in that state.
    /// </summary>
    public string? ExternalEmail => NeedsLocalAccount ? Me?.Email : null;

    /// <summary>Whether a screen should be showing the two-factor prompt.</summary>
    public bool NeedsTwoFactor => Phase == SessionPhase.TwoFactorChallenge;

    /// <summary>Whether something is in flight and the sign-in form should be disabled.</summary>
    public bool IsBusy => Phase is SessionPhase.Authenticating or SessionPhase.FetchingIdentity;
}
