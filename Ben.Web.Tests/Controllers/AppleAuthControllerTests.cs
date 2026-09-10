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

    // ── Claiming an account whose address Apple never mentioned ───────────────

    /// <summary>
    /// The whole point of the link endpoint: an Apple identity joining an account it could never
    /// have matched by email.
    /// </summary>
    /// <remarks>
    /// A Hide My Email relay address will never equal anything already here, and plenty of people
    /// have an Apple ID at one address and an account at another. Without this door both end at
    /// "create an account", which produces a SECOND account holding none of their cases, groups or
    /// history — and nothing to merge it back.
    /// </remarks>
    [Fact]
    public async Task LinkJoinsAnAccountWhoseAddressAppleNeverMentioned()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        // Apple only ever offered a relay address, which matches nothing here.
        var validator = new FakeValidator(new AppleIdentity(Sub, "xyz@privaterelay.appleid.com", true, true));

        var result = await Build(um, validator, sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.AddLoginAsync(mine, It.Is<UserLoginInfo>(l =>
            l.LoginProvider == "Apple" && l.ProviderKey == Sub)), Times.Once);
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
        sim.Verify(s => s.SignInAsync(mine, false, null), Times.Once);
    }

    /// <summary>
    /// The password is what proves the account is theirs; the Apple token only proves the Apple
    /// identity.
    /// </summary>
    /// <remarks>
    /// Without this check, anybody holding any Apple ID could attach themselves to any address
    /// they could name, and then sign in as that person for good.
    /// </remarks>
    [Fact]
    public async Task LinkRefusesAWrongPasswordAndLinksNothing()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "wrong", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "wrong"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// An unknown address and a wrong password answer identically.
    /// </summary>
    /// <remarks>
    /// Two different answers would make this endpoint a way to ask whether any given address has
    /// an account here, which is not something a stranger is entitled to know.
    /// </remarks>
    [Fact]
    public async Task LinkAnswersTheSameForAnUnknownAddressAsForAWrongPassword()
    {
        var um = UserManagerMock();   // FindByEmailAsync answers null by default
        var sim = SignInManagerMock(um);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "nobody@nowhere.test", "whatever"), default);

        var refusal = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("That email address and password don't match an account.", refusal.Value);
    }

    /// <summary>Guesses must not be free on a door that takes a password from a stranger.</summary>
    [Fact]
    public async Task LinkHonoursLockout()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, It.IsAny<string>(), true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "guess"), default);

        var refusal = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains("locked", refusal.Value!.ToString(), StringComparison.OrdinalIgnoreCase);

        // lockoutOnFailure: true is what makes repeated guesses cost something. CheckPasswordAsync,
        // which the Entra link uses, does not count them at all.
        sim.Verify(s => s.CheckPasswordSignInAsync(mine, "guess", true), Times.Once);
    }

    /// <summary>An Apple identity cannot be moved to a second account by whoever asks last.</summary>
    [Fact]
    public async Task LinkRefusesAnAppleIdentityAlreadyHeldElsewhere()
    {
        var theirs = new AppUser { Id = Guid.NewGuid(), Email = "someone@else.test" };
        var mine   = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(theirs);
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        Assert.IsType<ConflictObjectResult>(result);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    /// <summary>Linking what is already linked signs them in rather than complaining.</summary>
    [Fact]
    public async Task LinkingAnIdentityAlreadyOnThisAccountJustSignsIn()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(mine);
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        sim.Verify(s => s.SignInAsync(mine, false, null), Times.Once);
    }

    /// <summary>A token Apple did not sign links nothing, whatever password came with it.</summary>
    [Fact]
    public async Task LinkRefusesAnUnverifiableAppleToken()
    {
        var um = UserManagerMock();
        var sim = SignInManagerMock(um);

        var result = await Build(um, new FakeValidator(null), sim)
            .Link(new AppleLinkRequest("forged", "ben@ishaunted.com", "right"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        um.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    /// <summary>A server with no Apple audience configured links nothing either.</summary>
    [Fact]
    public async Task LinkRefusesWhenAppleIsNotConfigured()
    {
        var um = UserManagerMock();
        var sim = SignInManagerMock(um);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim, clientIds: [])
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        var refusal = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refusal.StatusCode);
    }

    // ── The email identifies nobody ───────────────────────────────────────────

    /// <summary>
    /// A returning Apple identity signs into the same account even when the address has changed
    /// completely.
    /// </summary>
    /// <remarks>
    /// <para>This is what makes Hide My Email survivable. The identity is Apple's <c>sub</c>, a
    /// stable identifier for this person in this app group; the address is decoration. Somebody can
    /// rotate their relay, turn the relay off, or move to a different Apple ID address, and still be
    /// the same person here.</para>
    ///
    /// <para>Pinned because the tempting "improvement" is to look an Apple user up by email. That
    /// would sign a relay user into nothing, or worse, into whatever account happened to hold a
    /// matching address.</para>
    /// </remarks>
    [Fact]
    public async Task AReturningIdentityIsFoundBySubjectWhateverTheEmailSays()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);

        // A relay address that matches no account here, and never will.
        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "xyz789@privaterelay.appleid.com", true, true)), sim)
            .SignIn(Request(), default);

        Assert.IsType<EmptyResult>(result);
        sim.Verify(s => s.SignInAsync(mine, false, null), Times.Once);

        // The address was never even consulted: the subject already answered the question.
        um.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
    }

    /// <summary>
    /// A DIFFERENT Apple identity is a different person, even at the same address.
    /// </summary>
    /// <remarks>
    /// The mirror of the test above, and the reason the subject lookup comes first. Matching on
    /// address alone would let one Apple ID sign in as whoever else happened to use that address.
    /// </remarks>
    [Fact]
    public async Task AnUnknownSubjectIsNotTreatedAsAReturningUser()
    {
        var um = UserManagerMock();   // FindByLoginAsync answers null for every subject
        var sim = SignInManagerMock(um);

        var result = await Build(um, new FakeValidator(
            new AppleIdentity("a.completely.different.sub", null, false, true)), sim)
            .SignIn(Request(), default);

        // No account, no name, no handle: it asks rather than guessing.
        Assert.IsType<ConflictObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>A token carrying no subject is refused; there is nothing to identify.</summary>
    [Fact]
    public async Task ATokenWithNoSubjectIsRefused()
    {
        var um = UserManagerMock();
        var sim = SignInManagerMock(um);

        // The validator throws for a token it cannot read a subject from - see its own guard.
        var result = await Build(um, new FakeValidator(null), sim).SignIn(Request(), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
    }
}
