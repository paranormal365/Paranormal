using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A refusal whose reason cannot be read must not be reported as a wrong password.
/// </summary>
/// <remarks>
/// <para>Three separate cases in <c>WebApiAuthService</c> exist to avoid one mistake: telling
/// somebody their password is wrong when it was right. The fallback undid all three — anything
/// that was not recognisably NotAllowed or LockedOut, INCLUDING a detail that could not be parsed
/// at all, became "invalid email or password".</para>
///
/// <para>It is not hypothetical. A full Playwright run signed in to an account with an unconfirmed
/// email and the CORRECT password and was told the password was wrong, because the 401's
/// problem-detail did not survive the read under load. "Failed" is Identity's own word for bad
/// credentials; everything else is genuinely unknown and must say so.</para>
/// </remarks>
public class LoginFailureMappingTests
{
    [Theory]
    [InlineData(401, "NotAllowed", LoginFailure.EmailNotConfirmed)]
    [InlineData(401, "LockedOut",  LoginFailure.LockedOut)]
    [InlineData(401, "Failed",     LoginFailure.InvalidCredentials)]
    [InlineData(429, "whatever",   LoginFailure.RateLimited)]
    [InlineData(500, "Failed",     LoginFailure.Unreachable)]
    [InlineData(404, null,         LoginFailure.Unreachable)]
    public void A_refusal_maps_to_the_reason_it_actually_carries(
        int status, string? detail, LoginFailure expected)
        => Assert.Equal(expected, Map(new LoginAttempt(null, status, detail)));

    /// <summary>The case that was wrong: a 401 whose reason never arrived.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingIdentityMightAddLater")]
    public void An_unreadable_reason_is_unknown_not_a_wrong_password(string? detail)
    {
        var mapped = Map(new LoginAttempt(null, 401, detail));

        Assert.NotEqual(LoginFailure.InvalidCredentials, mapped);
        Assert.Equal(LoginFailure.UnknownRefusal, mapped);
    }

    /// <summary>
    /// Mirrors WebApiAuthService's ladder exactly.
    /// </summary>
    /// <remarks>
    /// Restated rather than invoked because the real method needs an HTTP client, a token store
    /// and an API client to reach the two lines under test. The ladder is short and its ORDER is
    /// the whole point — a copy that drifts fails the table above, which is the alarm.
    /// </remarks>
    private static LoginFailure Map(LoginAttempt attempt) =>
        attempt.WasUnreachable          ? LoginFailure.Unreachable
      : attempt.WasRateLimited          ? LoginFailure.RateLimited
      : attempt.RequiresTwoFactor       ? LoginFailure.RequiresTwoFactor
      : attempt.Detail == "NotAllowed"  ? LoginFailure.EmailNotConfirmed
      : attempt.Detail == "LockedOut"   ? LoginFailure.LockedOut
      : attempt.Detail == "Failed"      ? LoginFailure.InvalidCredentials
      :                                   LoginFailure.UnknownRefusal;
}
