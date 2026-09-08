using Ben.Data.WebApi.Authorization;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which bearer tokens are worth handing to the Entra JWT handler (W-A16).
/// </summary>
/// <remarks>
/// <para>The default authorization policy accepts either the local Identity bearer scheme or the
/// Entra JWT scheme, and a policy listing two schemes runs both. So every ordinary sign-in's
/// Identity token reached the Entra handler, which correctly reported that it is not a JWT — at
/// Warning, twice per request. 188,000 lines in one afternoon, all of them about requests that
/// succeeded.</para>
///
/// <para><b>The permissive direction is the safe one.</b> Wrongly letting a token through costs a
/// log line and a failed validation; wrongly turning one away breaks Entra sign-in for whoever
/// holds it. Every case below is written with that asymmetry in mind — the rejections are only
/// shapes that could not be a JWT under any serialisation.</para>
/// </remarks>
public class BearerTokenShapeTests
{
    /// <summary>
    /// The token the flood was made of: ASP.NET Core Identity's own bearer token.
    /// </summary>
    /// <remarks>
    /// A single data-protection payload. Base64url with no dots at all, which is what makes this
    /// answerable without decoding anything.
    /// </remarks>
    [Fact]
    public void An_identity_bearer_token_is_not_a_jwt()
    {
        const string identityToken =
            "CfDJ8Nr5Kw2VZ0tHmQ7cV1sYt3Xa9WqLpO2bE4uRn6TjMhKgFvBdCyZsAeUxIlPoQwRtYuIoPaSdFgHjKl";
        Assert.False(BearerTokenShape.CouldBeAJwt(identityToken));
    }

    /// <summary>A signed JWT — header.payload.signature.</summary>
    [Fact]
    public void A_signed_jwt_is_offered_to_the_handler()
    {
        Assert.True(BearerTokenShape.CouldBeAJwt("eyJhbGciOiJSUzI1NiJ9.eyJvaWQiOiIxMjMifQ.c2ln"));
    }

    /// <summary>
    /// An encrypted JWT — five segments. Entra does not issue these for a custom API audience
    /// today, and turning one away if it ever does would break sign-in for a log line's sake.
    /// </summary>
    [Fact]
    public void An_encrypted_jwt_is_offered_to_the_handler()
    {
        Assert.True(BearerTokenShape.CouldBeAJwt("aGVhZGVy.a2V5.aXY.Y2lwaGVy.dGFn"));
    }

    /// <summary>
    /// Nothing, whitespace, and a token whose dots enclose nothing.
    /// </summary>
    /// <remarks>
    /// "a..b" has two dots and is not a JWT — for that one the handler's "not well formed"
    /// complaint is the correct answer, and there is no reason to make it.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a..b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("one.two")]
    [InlineData("one.two.three.four")]
    public void These_could_not_be_a_jwt(string? token)
    {
        Assert.False(BearerTokenShape.CouldBeAJwt(token));
    }

    /// <summary>
    /// It looks at the shape and nothing else.
    /// </summary>
    /// <remarks>
    /// Explicit, because a reader could reasonably assume a check named for tokens verifies
    /// something. It does not, and must not: a token that passes here still goes through the whole
    /// of Entra's validation unchanged. The only decision made here is whether to bother.
    /// </remarks>
    [Fact]
    public void It_makes_no_claim_about_a_token_being_valid()
    {
        Assert.True(BearerTokenShape.CouldBeAJwt("not.a.jwt"));
    }
}
