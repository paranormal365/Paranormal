using System.Security.Claims;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Carrying a signed-in identity from the site to the standalone editor.
/// </summary>
/// <remarks>
/// The exchange is an anonymous endpoint that mints sessions, so what it refuses matters more than
/// what it allows. Every refusal below is a way in that this must not be (phase 12).
/// </remarks>
public sealed class EditorHandoffControllerTests
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Mock<UserManager<AppUser>> UserManagerMock(AppUser? found)
    {
        var store = new Mock<IUserStore<AppUser>>();
        var mock  = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        mock.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(found);
        return mock;
    }

    private static Mock<SignInManager<AppUser>> SignInManagerMock(
        Mock<UserManager<AppUser>> um, bool canSignIn = true)
    {
        var mock = new Mock<SignInManager<AppUser>>(
            um.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<AppUser>>().Object, null!, null!, null!, null!);

        mock.Setup(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.CanSignInAsync(It.IsAny<AppUser>())).ReturnsAsync(canSignIn);
        return mock;
    }

    private static EditorHandoffController Build(
        EditorHandoffCodeStore codes,
        Mock<UserManager<AppUser>> um,
        Mock<SignInManager<AppUser>> sim,
        Guid? signedInAs = null)
    {
        var controller = new EditorHandoffController(
            codes, um.Object, sim.Object, NullLogger<EditorHandoffController>.Instance);

        var http = new DefaultHttpContext();

        if (signedInAs is { } id)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    // ── Issuing ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_signed_in_caller_gets_a_code_that_stands_for_them()
    {
        var codes = new EditorHandoffCodeStore();
        var um    = UserManagerMock(null);

        var result = Build(codes, um, SignInManagerMock(um), signedInAs: UserId).Issue();

        var body = Assert.IsType<EditorHandoffCodeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(UserId, codes.Redeem(body.Code));
    }

    [Fact]
    public void The_code_says_how_long_it_lasts()
    {
        var codes = new EditorHandoffCodeStore();
        var um    = UserManagerMock(null);

        var result = Build(codes, um, SignInManagerMock(um), signedInAs: UserId).Issue();

        var body = Assert.IsType<EditorHandoffCodeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((int)EditorHandoffCodeStore.Lifetime.TotalSeconds, body.ExpiresInSeconds);
    }

    /// <summary>
    /// Belt and braces behind <c>[Authorize]</c>: an unidentifiable caller is issued nothing.
    /// </summary>
    [Fact]
    public void A_caller_with_no_identity_is_issued_nothing()
    {
        var codes = new EditorHandoffCodeStore();
        var um    = UserManagerMock(null);

        var result = Build(codes, um, SignInManagerMock(um)).Issue();

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, codes.Count);
    }

    // ── Exchanging ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_good_code_signs_that_account_in()
    {
        var codes = new EditorHandoffCodeStore();
        var code  = codes.Issue(UserId);
        var user  = new AppUser { Id = UserId, Email = "ben@test.com" };
        var um    = UserManagerMock(user);
        var sim   = SignInManagerMock(um);

        var result = await Build(codes, um, sim).ExchangeAsync(new EditorHandoffExchangeRequest(code));

        // The bearer handler wrote the body; anything else here would append to it.
        Assert.IsType<EmptyResult>(result);
        sim.Verify(s => s.SignInAsync(user, false, null), Times.Once);
    }

    /// <summary>
    /// The thing the whole design turns on: what crosses to the other origin is a code, and the
    /// tokens it becomes are minted here, freshly, by Identity itself.
    /// </summary>
    [Fact]
    public async Task The_exchange_mints_its_own_session_under_the_bearer_scheme()
    {
        var codes = new EditorHandoffCodeStore();
        var code  = codes.Issue(UserId);
        var um    = UserManagerMock(new AppUser { Id = UserId });
        var sim   = SignInManagerMock(um);

        var controller = Build(codes, um, sim);
        await controller.ExchangeAsync(new EditorHandoffExchangeRequest(code));

        Assert.Equal(IdentityConstants.BearerScheme, sim.Object.AuthenticationScheme);
    }

    [Fact]
    public async Task A_code_cannot_be_exchanged_twice()
    {
        var codes = new EditorHandoffCodeStore();
        var code  = codes.Issue(UserId);
        var um    = UserManagerMock(new AppUser { Id = UserId });
        var sim   = SignInManagerMock(um);
        var controller = Build(codes, um, sim);

        await controller.ExchangeAsync(new EditorHandoffExchangeRequest(code));
        var second = await controller.ExchangeAsync(new EditorHandoffExchangeRequest(code));

        Assert.IsType<UnauthorizedObjectResult>(second);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("guessed-it")]
    public async Task A_code_nobody_issued_signs_nobody_in(string code)
    {
        var codes = new EditorHandoffCodeStore();
        var um    = UserManagerMock(new AppUser { Id = UserId });
        var sim   = SignInManagerMock(um);

        var result = await Build(codes, um, sim).ExchangeAsync(new EditorHandoffExchangeRequest(code));

        Assert.IsType<UnauthorizedObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// A missing body is a malformed request, not a session.
    /// </summary>
    [Fact]
    public async Task An_exchange_with_no_body_signs_nobody_in()
    {
        var codes = new EditorHandoffCodeStore();
        var um    = UserManagerMock(new AppUser { Id = UserId });
        var sim   = SignInManagerMock(um);

        var result = await Build(codes, um, sim).ExchangeAsync(null!);

        Assert.IsType<UnauthorizedObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// An account that cannot sign in on the password path cannot sign in on this one. Closed
    /// accounts and lockouts both answer here.
    /// </summary>
    [Fact]
    public async Task An_account_that_cannot_sign_in_cannot_come_in_this_way_either()
    {
        var codes = new EditorHandoffCodeStore();
        var code  = codes.Issue(UserId);
        var um    = UserManagerMock(new AppUser { Id = UserId });
        var sim   = SignInManagerMock(um, canSignIn: false);

        var result = await Build(codes, um, sim).ExchangeAsync(new EditorHandoffExchangeRequest(code));

        Assert.IsType<UnauthorizedObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task A_code_for_an_account_that_no_longer_exists_signs_nobody_in()
    {
        var codes = new EditorHandoffCodeStore();
        var code  = codes.Issue(UserId);
        var um    = UserManagerMock(null);
        var sim   = SignInManagerMock(um);

        var result = await Build(codes, um, sim).ExchangeAsync(new EditorHandoffExchangeRequest(code));

        Assert.IsType<UnauthorizedObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// Unknown, used and expired all answer the same thing. Telling them apart tells a guesser
    /// which guesses are closer.
    /// </summary>
    [Fact]
    public async Task Every_refusal_says_the_same_thing()
    {
        var codes = new EditorHandoffCodeStore();
        var used  = codes.Issue(UserId);
        var um    = UserManagerMock(new AppUser { Id = UserId });
        var controller = Build(codes, um, SignInManagerMock(um));

        await controller.ExchangeAsync(new EditorHandoffExchangeRequest(used));

        var spent   = await controller.ExchangeAsync(new EditorHandoffExchangeRequest(used));
        var unknown = await controller.ExchangeAsync(new EditorHandoffExchangeRequest("never-issued"));

        Assert.Equal(
            Assert.IsType<UnauthorizedObjectResult>(spent).Value,
            Assert.IsType<UnauthorizedObjectResult>(unknown).Value);
    }
}
