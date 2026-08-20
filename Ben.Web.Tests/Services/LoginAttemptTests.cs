using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Reading a refused sign-in correctly.
/// </summary>
/// <remarks>
/// <para>Identity answers four quite different situations with the same 401, distinguished only by
/// a string in the problem-detail body. Getting that wrong is not a cosmetic failure: for most of
/// this product's life every one of them was reported as "Invalid email or password", which sends
/// somebody whose password was <b>right</b> off to reset it — and the reset does not help, because
/// the password was never the problem.</para>
///
/// <para>What each one actually needs the person to do is different in every case: enter a code,
/// confirm an email, wait for a lockout to clear, wait for a rate limit to clear, or genuinely
/// retype a password. This is the code that tells them apart.</para>
/// </remarks>
public sealed class LoginAttemptTests
{
    [Fact]
    public void A_two_factor_challenge_is_not_a_failure()
    {
        // The password was correct. Reporting this as bad credentials is the worst of the four,
        // because the account is working perfectly and the person is told it is not.
        var attempt = new LoginAttempt(null, 401, "RequiresTwoFactor");

        Assert.True(attempt.RequiresTwoFactor);
        Assert.False(attempt.WasRateLimited);
    }

    [Fact]
    public void A_two_factor_challenge_is_recognised_only_on_a_401()
    {
        // Defensive: the detail string is Identity's, and a 500 that happened to carry it would
        // not mean the password was accepted.
        Assert.False(new LoginAttempt(null, 500, "RequiresTwoFactor").RequiresTwoFactor);
    }

    [Theory]
    [InlineData("NotAllowed")]        // unconfirmed email
    [InlineData("LockedOut")]
    [InlineData("Failed")]
    [InlineData(null)]
    public void Other_refusals_are_not_read_as_a_two_factor_challenge(string? detail)
    {
        Assert.False(new LoginAttempt(null, 401, detail).RequiresTwoFactor);
    }

    [Fact]
    public void The_detail_is_matched_exactly()
    {
        // Ordinal comparison, deliberately: this is a protocol string from Identity, not prose,
        // and a culture-sensitive or case-insensitive match here would be matching something the
        // server never said.
        Assert.False(new LoginAttempt(null, 401, "requirestwofactor").RequiresTwoFactor);
        Assert.False(new LoginAttempt(null, 401, "RequiresTwoFactor ").RequiresTwoFactor);
    }

    [Fact]
    public void A_rate_limited_attempt_is_the_server_refusing_the_request_not_the_account()
    {
        // 429 is about this client sending too many requests; it says nothing about the
        // credentials, and the cure is to wait rather than to retype anything.
        var attempt = new LoginAttempt(null, 429);

        Assert.True(attempt.WasRateLimited);
        Assert.False(attempt.RequiresTwoFactor);
    }

    [Fact]
    public void A_successful_attempt_carries_the_token_and_claims_nothing_else()
    {
        var attempt = new LoginAttempt(new WebApiTokenResponse { AccessToken = "abc" }, 200);

        Assert.False(attempt.WasRateLimited);
        Assert.False(attempt.RequiresTwoFactor);
        Assert.Equal("abc", attempt.Token!.AccessToken);
    }
}
