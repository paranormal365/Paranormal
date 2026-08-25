using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Sign in with Apple: the three outcomes, and the refusals that keep it from being a way in.
/// </summary>
/// <remarks>
/// The token validator is faked here on purpose — validating Apple's signature is Microsoft's
/// code and Apple's keys, neither of which is this site's to test. What IS this site's is what it
/// DOES with a validated identity, and every one of those decisions is exercised below.
/// </remarks>
public class AppleAuthControllerTests
{
    private const string Sub = "001234.abcdef.5678";

    private sealed class FakeValidator : IAppleIdentityTokenValidator
    {
        private readonly AppleIdentity? _identity;
        public FakeValidator(AppleIdentity? identity) => _identity = identity;
        public IReadOnlyList<string>? SawAudiences { get; private set; }

        public Task<AppleIdentity> ValidateAsync(
            string identityToken, IReadOnlyList<string> audiences, CancellationToken ct)
        {
            SawAudiences = audiences;
            return _identity is null
                ? throw new SecurityTokenException("bad token")
                : Task.FromResult(_identity);
        }
    }

    private static Mock<UserManager<AppUser>> UserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        var mock = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((AppUser?)null);
        mock.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((AppUser?)null);
        mock.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);
        mock.Setup(m => m.UpdateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        return mock;
    }

    private static Mock<SignInManager<AppUser>> SignInManagerMock(Mock<UserManager<AppUser>> um)
    {
        var context = new Mock<IHttpContextAccessor>();
        var claims  = new Mock<IUserClaimsPrincipalFactory<AppUser>>();
        var mock = new Mock<SignInManager<AppUser>>(
            um.Object, context.Object, claims.Object, null!, null!, null!, null!);
        mock.Setup(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static IDbContextFactory<BenDataContext> Factory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static AppleAuthController Build(
        Mock<UserManager<AppUser>> um,
        IAppleIdentityTokenValidator validator,
        Mock<SignInManager<AppUser>>? sim = null,
        string[]? clientIds = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            (clientIds ?? ["com.ishaunted.ios"])
                .Select((id, i) => new KeyValuePair<string, string?>($"Apple:ClientIds:{i}", id))
                .ToArray()).Build();

        var controller = new AppleAuthController(
            um.Object, (sim ?? SignInManagerMock(um)).Object,
            new UserHandleService(Factory()), validator, config,
            NullLogger<AppleAuthController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static AppleSignInRequest Request(string? name = null, string? handle = null) =>
        new("a.signed.token", name, handle);

    // ── The three outcomes ────────────────────────────────────────────────────

    [Fact]
    public async Task AKnownAppleIdentitySignsStraightIn()
    {
        var existing = new AppUser { Id = Guid.NewGuid(), Email = "known@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(existing);
        var sim = SignInManagerMock(um);

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "known@test.com", true, false)), sim).SignIn(Request(), default);

        Assert.IsType<EmptyResult>(result);   // the bearer handler wrote the body
        sim.Verify(s => s.SignInAsync(existing, false, null), Times.Once);
        // No second account, and no re-linking of a link that already exists.
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task AVerifiedEmailLinksToTheAccountThatAlreadyHasIt()
    {
        // Somebody who signed up on the website, now arriving on their phone. One account.
        var website = new AppUser { Id = Guid.NewGuid(), Email = "ben@test.com", EmailConfirmed = true };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@test.com")).ReturnsAsync(website);
        var sim = SignInManagerMock(um);

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "ben@test.com", true, false)), sim).SignIn(Request(), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.AddLoginAsync(website,
            It.Is<UserLoginInfo>(l => l.LoginProvider == "Apple" && l.ProviderKey == Sub)), Times.Once);
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
        sim.Verify(s => s.SignInAsync(website, false, null), Times.Once);
    }

    [Fact]
    public async Task ANewIdentityWithANameAndHandleGetsAnAccount()
    {
        var um = UserManagerMock();
        AppUser? created = null;
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
          .Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "new@test.com", true, false)))
            .SignIn(Request("New Person", "NewPerson"), default);

        Assert.IsType<EmptyResult>(result);
        Assert.NotNull(created);
        Assert.Equal("new@test.com", created!.Email);
        Assert.Equal("newperson", created.Handle);
        // Apple verified the address, so there is no confirmation left to wait for.
        Assert.True(created.EmailConfirmed);
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [Fact]
    public async Task ANewIdentityWithoutAHandleIsAskedForOneRatherThanGivenAnInventedOne()
    {
        // A handle is permanent. Generating one from an Apple sub would hand somebody a name
        // they never chose and cannot change.
        var result = await Build(UserManagerMock(), new FakeValidator(
            new AppleIdentity(Sub, "new@test.com", true, false))).SignIn(Request(), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var payload = Assert.IsType<AppleNeedsProfileResponse>(conflict.Value);
        Assert.True(payload.NeedsProfile);
        Assert.Equal("new@test.com", payload.Email);
    }

    [Fact]
    public async Task AnUnverifiedEmailNeverLinksToAnExistingAccount()
    {
        // The whole safety of outcome 2 is that Apple vouched for the address. Without that
        // claim, matching on the string would be account takeover by typing an email.
        var victim = new AppUser { Id = Guid.NewGuid(), Email = "victim@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("victim@test.com")).ReturnsAsync(victim);

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "victim@test.com", EmailVerified: false, IsPrivateEmail: false)))
            .SignIn(Request(), default);

        Assert.IsType<ConflictObjectResult>(result);   // treated as a stranger, not as the victim
        um.Verify(m => m.AddLoginAsync(victim, It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task AnUnverifiableTokenIsRefused()
    {
        var result = await Build(UserManagerMock(), new FakeValidator(null)).SignIn(Request(), default);
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task AnEmptyTokenIsRefusedBeforeAnythingElse()
    {
        var result = await Build(UserManagerMock(), new FakeValidator(
            new AppleIdentity(Sub, "x@test.com", true, false)))
            .SignIn(new AppleSignInRequest("  ", null, null), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AnUnconfiguredServerRefusesRatherThanTrustingWhateverArrives()
    {
        // With no configured audience there is nothing to check the token against, and
        // "validate against anything" is how a signed token for a DIFFERENT app gets in.
        var validator = new FakeValidator(new AppleIdentity(Sub, "x@test.com", true, false));
        var result = await Build(UserManagerMock(), validator, clientIds: []).SignIn(Request(), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        Assert.Null(validator.SawAudiences);   // never even asked
    }

    [Fact]
    public async Task TheConfiguredClientIdsAreWhatTheTokenIsCheckedAgainst()
    {
        var validator = new FakeValidator(new AppleIdentity(Sub, "x@test.com", true, false));
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub))
          .ReturnsAsync(new AppUser { Id = Guid.NewGuid() });

        await Build(um, validator, clientIds: ["com.ishaunted.ios", "com.ishaunted.web"])
            .SignIn(Request(), default);

        Assert.Equal(["com.ishaunted.ios", "com.ishaunted.web"], validator.SawAudiences);
    }

    [Fact]
    public async Task AWithheldEmailStillGetsAnAccountButNoPretendAddress()
    {
        // "Hide My Email" and a user who shares nothing both land here. An account is still
        // possible; what must not happen is a plausible-looking address nobody can receive.
        var um = UserManagerMock();
        AppUser? created = null;
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
          .Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, Email: null, EmailVerified: false, IsPrivateEmail: true)))
            .SignIn(Request("Quiet Person", "QuietPerson"), default);

        Assert.IsType<EmptyResult>(result);
        Assert.NotNull(created);
        Assert.EndsWith("@appleid.invalid", created!.Email);   // reserved TLD: never deliverable
    }

    [Fact]
    public async Task AFailedLinkRollsBackTheAccountRatherThanLeavingOneNothingCanSignInto()
    {
        var um = UserManagerMock();
        um.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "no" }));

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "new@test.com", true, false)))
            .SignIn(Request("New Person", "NewPerson2"), default);

        Assert.IsType<BadRequestObjectResult>(result);
        um.Verify(m => m.DeleteAsync(It.IsAny<AppUser>()), Times.Once);
    }

    // ── The real validator: telling "bad token" apart from "Apple is down" ────

    [Theory]
    [InlineData("not.a.real.token")]        // four segments
    [InlineData("garbage")]                 // no segments at all
    [InlineData("")]
    public async Task AMalformedTokenIsRejectedLocallyAndNeverBlamedOnApple(string token)
    {
        // This runs the REAL validator with an HttpClient pointed nowhere: a malformed token must
        // be refused before any network call, so the test proves both that it is rejected and
        // that Apple was never asked. IdentityModel 8 files SecurityTokenMalformedException under
        // ArgumentException, which once made this arrive as "we couldn't reach Apple" — a refusal
        // reported to the user as somebody else's outage.
        var http = new HttpClient(new ThrowingHandler());
        var validator = new AppleIdentityTokenValidator(http);

        await Assert.ThrowsAsync<SecurityTokenException>(
            () => validator.ValidateAsync(token, ["com.ishaunted.ios"], default));
    }

    /// <summary>Fails any request, so a test that touches the network fails loudly instead of
    /// quietly depending on Apple being up.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("the network should not have been touched");
    }

    // ── The wire shape the iPhone app decodes ─────────────────────────────────

    [Fact]
    public void TheNeedsProfileBodyIsExactlyWhatTheAppDecodes()
    {
        // Ben.iOS decodes this by hand, in another language, in another repo folder. Nothing
        // else connects the two, so this literal is the contract: change the record and this
        // test fails HERE, next to the change, instead of the app silently reading nothing.
        // The matching Swift test (BenKitTests/AppleSignInTests) uses this same string.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new AppleNeedsProfileResponse(true, "New Person", "new@test.com", false, null),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        Assert.Equal(
            "{\"needsProfile\":true,\"suggestedDisplayName\":\"New Person\"," +
            "\"email\":\"new@test.com\",\"isPrivateEmail\":false,\"handleProblem\":null}",
            json);
    }
}
