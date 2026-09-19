namespace Ben.Web.Services.WebApi;

/// <summary>
/// Turns one sign-in attempt into the reason it failed. The ladder used to live inside
/// <c>WebApiAuthService.LoginAsync</c>, where nothing but the website could reach it.
/// </summary>
/// <remarks>
/// <para>Item 225 moved it here so every client answers a refusal the same way. Two front ends
/// that each decide for themselves what a 401 means will eventually disagree, and the way that
/// shows up is somebody being told their password is wrong when it never was.</para>
///
/// <para>The reason this is worth a named type rather than an inline conditional: Identity
/// answers <b>four</b> entirely different situations with the same 401, and the only thing that
/// separates them is a string in the problem-detail. Wait, enter your code, confirm your email,
/// or fix your password are four different instructions to a person, and three of them are
/// useless advice if the fourth is guessed.</para>
/// </remarks>
public static class LoginFailureMapping
{
    /// <summary>Why <paramref name="attempt"/> did not produce a token.</summary>
    /// <remarks>
    /// Call this only when the attempt carries no usable access token — a successful sign-in has
    /// no failure to name.
    /// </remarks>
    public static LoginFailure From(LoginAttempt attempt) =>
        attempt.WasUnreachable          ? LoginFailure.Unreachable
      : attempt.WasRateLimited          ? LoginFailure.RateLimited
      : attempt.RequiresTwoFactor       ? LoginFailure.RequiresTwoFactor
      : attempt.Detail == "NotAllowed"  ? LoginFailure.EmailNotConfirmed
      : attempt.Detail == "LockedOut"   ? LoginFailure.LockedOut
      // "Failed" is Identity's own word for a wrong email or password — SignInResult stringified.
      // Anything ELSE, including a detail that could not be read at all, means the reason is
      // genuinely unknown, and saying "invalid email or password" there is the mistake the cases
      // above exist to avoid. A full Playwright run caught it: an unconfirmed account with the
      // RIGHT password was told the password was wrong, because the 401's problem-detail did not
      // survive the read under load.
      : attempt.Detail == "Failed"      ? LoginFailure.InvalidCredentials
      :                                   LoginFailure.UnknownRefusal;
}
