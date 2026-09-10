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
        // Most accounts have no second factor; the ones that do say so explicitly per test.
        mock.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<AppUser>())).ReturnsAsync(false);
        // And most are not locked out. The tests about an administrator's refusal say otherwise.
        mock.Setup(m => m.IsLockedOutAsync(It.IsAny<AppUser>())).ReturnsAsync(false);
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
        // An ordinary account may sign in. A closed one may not, and those tests say so.
        mock.Setup(s => s.CanSignInAsync(It.IsAny<AppUser>())).ReturnsAsync(true);
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

        // The service is built from the SAME mocks as the controller, so every expectation these
        // tests set on the managers is seen by the decisions, wherever they now live.
        var signIn = (sim ?? SignInManagerMock(um)).Object;
        var external = new ExternalSignInService(um.Object, signIn, new UserHandleService(Factory()));
        var controller = new AppleAuthController(
            um.Object, signIn, external, validator, config,
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
        //
        // shouldLinkInstead and emailProblem were added when a taken address stopped being
        // reported under the @name field. APPENDED, and both optional: Swift's Codable ignores
        // keys it does not know, so the app keeps decoding this unchanged and simply does not act
        // on the new routing until item 226 teaches it to.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new AppleNeedsProfileResponse(true, "New Person", "new@test.com", false, null),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        Assert.Equal(
            "{\"needsProfile\":true,\"suggestedDisplayName\":\"New Person\"," +
            "\"email\":\"new@test.com\",\"isPrivateEmail\":false,\"handleProblem\":null," +
            "\"shouldLinkInstead\":false,\"emailProblem\":null}",
            json);

        // The keys the app actually reads are untouched and still in place, which is the part
        // that decides whether a shipped build keeps working.
        foreach (var key in new[] { "needsProfile", "suggestedDisplayName", "email", "isPrivateEmail", "handleProblem" })
            Assert.Contains($"\"{key}\":", json);
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

        AssertRefusal(result, "Failed");
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

        // Byte for byte what a wrong password answers. Same status, same shape, same word — anything
        // less makes this a way to ask whether an address has an account here.
        AssertRefusal(result, "Failed");
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

        AssertRefusal(result, "LockedOut");

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

    // ── Linking must not walk around a second factor ──────────────────────────

    /// <summary>
    /// An account with two-step verification is NOT joinable with a password alone.
    /// </summary>
    /// <remarks>
    /// <para>This is the hole this test exists for. <c>CheckPasswordSignInAsync</c> verifies the
    /// password and the lockout and nothing else — it never returns <c>RequiresTwoFactor</c>,
    /// because that is <c>PasswordSignInAsync</c>'s job, and this endpoint cannot use that one
    /// without creating a cookie sign-in it has no business creating.</para>
    ///
    /// <para>So a first draft of this endpoint would link a two-factor account on a password, which
    /// is a way around the exact protection its owner turned on. The second factor is checked
    /// explicitly instead.</para>
    /// </remarks>
    [Fact]
    public async Task LinkRefusesATwoFactorAccountWithNoCode()
    {
        var (um, sim, mine) = TwoFactorAccount();

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        AssertRefusal(result, "RequiresTwoFactor");
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>A wrong code is refused as firmly as no code.</summary>
    [Fact]
    public async Task LinkRefusesATwoFactorAccountWithAWrongCode()
    {
        var (um, sim, mine) = TwoFactorAccount();
        um.Setup(m => m.VerifyTwoFactorTokenAsync(mine, It.IsAny<string>(), "000000")).ReturnsAsync(false);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right", TwoFactorCode: "000000"), default);

        AssertRefusal(result, "RequiresTwoFactor");
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    /// <summary>The right code links and signs in.</summary>
    [Fact]
    public async Task LinkAcceptsAValidAuthenticatorCode()
    {
        var (um, sim, mine) = TwoFactorAccount();
        um.Setup(m => m.VerifyTwoFactorTokenAsync(mine, It.IsAny<string>(), "123456")).ReturnsAsync(true);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right", TwoFactorCode: "123456"), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.AddLoginAsync(mine, It.IsAny<UserLoginInfo>()), Times.Once);
    }

    /// <summary>
    /// A code typed the way it is displayed still works.
    /// </summary>
    /// <remarks>
    /// People read these aloud in threes and authenticator apps print them spaced. Refusing a
    /// correctly typed code would be a failure we caused.
    /// </remarks>
    [Fact]
    public async Task LinkIgnoresSpacingInACode()
    {
        var (um, sim, mine) = TwoFactorAccount();
        um.Setup(m => m.VerifyTwoFactorTokenAsync(mine, It.IsAny<string>(), "123456")).ReturnsAsync(true);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right", TwoFactorCode: "123 456"), default);

        Assert.IsType<EmptyResult>(result);
    }

    /// <summary>
    /// A recovery code is REDEEMED, not merely checked.
    /// </summary>
    /// <remarks>
    /// One that survived being used would not be a recovery code. Verified through the redeeming
    /// call rather than the checking one, because the difference is the whole point.
    /// </remarks>
    [Fact]
    public async Task LinkRedeemsARecoveryCode()
    {
        var (um, sim, mine) = TwoFactorAccount();
        um.Setup(m => m.RedeemTwoFactorRecoveryCodeAsync(mine, "abcd1234")).ReturnsAsync(IdentityResult.Success);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right",
                TwoFactorRecoveryCode: "abcd-1234"), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.RedeemTwoFactorRecoveryCodeAsync(mine, "abcd1234"), Times.Once);

        // A recovery code is not an app code and must not be checked as one.
        um.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>An account with no second factor is never asked for one.</summary>
    [Fact]
    public async Task LinkDoesNotAskForACodeWhenThereIsNoSecondFactor()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// An unconfirmed address is named as such, not reported as a wrong password.
    /// </summary>
    /// <remarks>
    /// The mistake this avoids has its own comments elsewhere in this codebase: telling somebody
    /// their password is wrong sends them to reset one that was always right.
    /// </remarks>
    [Fact]
    public async Task LinkTellsAnUnconfirmedAccountWhatIsActuallyWrong()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.NotAllowed);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        AssertRefusal(result, "NotAllowed");
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    private static (Mock<UserManager<AppUser>> Um, Mock<SignInManager<AppUser>> Sim, AppUser User) TwoFactorAccount()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        um.Setup(m => m.GetTwoFactorEnabledAsync(mine)).ReturnsAsync(true);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        return (um, sim, mine);
    }

    /// <summary>A refusal carries Identity's own word, in the shape /login uses.</summary>
    private static void AssertRefusal(IActionResult result, string expectedDetail)
    {
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
        var details = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(problem.Value);
        Assert.Equal(expectedDetail, details.Detail);
    }

    // ── Recording what kind of address Apple actually gave ────────────────────

    /// <summary>
    /// Apple says whether the address is a relay exactly once, and it is recorded.
    /// </summary>
    /// <remarks>
    /// It used to be read and thrown away. Nothing afterwards could tell a relay from a real
    /// address, so the site printed a machine-generated string as somebody's own email and would
    /// have claimed to write to one Apple silently drops.
    /// </remarks>
    [Fact]
    public async Task ARelayAddressIsRecordedAsARelay()
    {
        AppUser? created = null;
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
          .Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);

        await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "x7k2@privaterelay.appleid.com", true, IsPrivateEmail: true)),
            SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        Assert.NotNull(created);
        Assert.Equal(Ben.Data.Common.Enums.EmailAddressKind.AppleRelay, created!.EmailKind);
        Assert.Equal("x7k2@privaterelay.appleid.com", created.Email);
    }

    /// <summary>
    /// A withheld address is recorded as unreachable, which is stronger than "a relay".
    /// </summary>
    /// <remarks>
    /// The placeholder ends in <c>.invalid</c>, reserved so it can never resolve. Nothing will ever
    /// be delivered to it — not a password reset, not a way back in — so the difference from a
    /// relay is worth keeping: one is a configuration problem, the other is permanent.
    /// </remarks>
    [Fact]
    public async Task AWithheldAddressIsRecordedAsUnreachable()
    {
        AppUser? created = null;
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
          .Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);

        await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, IsPrivateEmail: true)),
            SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        Assert.NotNull(created);
        Assert.Equal(Ben.Data.Common.Enums.EmailAddressKind.Unreachable, created!.EmailKind);
        Assert.EndsWith("@appleid.invalid", created.Email);
    }

    /// <summary>A real address is recorded as one, so nothing is warned about that need not be.</summary>
    [Fact]
    public async Task ARealAddressIsRecordedAsOrdinary()
    {
        AppUser? created = null;
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
          .Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);

        await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "ada@example.test", true, IsPrivateEmail: false)),
            SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        Assert.NotNull(created);
        Assert.Equal(Ben.Data.Common.Enums.EmailAddressKind.Ordinary, created!.EmailKind);
    }

    /// <summary>
    /// Linking to an existing account does NOT restamp its address.
    /// </summary>
    /// <remarks>
    /// The account keeps its own real address; only the Apple identity is attached. Marking it as a
    /// relay because Apple's was one would warn somebody about an address that is perfectly fine.
    /// </remarks>
    [Fact]
    public async Task LinkingDoesNotRestampAnExistingAccountsAddress()
    {
        var mine = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "ben@ishaunted.com",
            EmailKind = Ben.Data.Common.Enums.EmailAddressKind.Ordinary,
        };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "x7k2@privaterelay.appleid.com", true, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "ben@ishaunted.com", "right"), default);

        Assert.Equal(Ben.Data.Common.Enums.EmailAddressKind.Ordinary, mine.EmailKind);
        Assert.Equal("ben@ishaunted.com", mine.Email);
    }

    // ── A SuperAdmin's refusal has to hold on this door too ───────────────────

    /// <summary>
    /// A locked-out account cannot sign in with Apple.
    /// </summary>
    /// <remarks>
    /// Lockout is the only lever an administrator has over an account short of closing it, and it
    /// is set through the admin profile endpoint. If Apple ignores it, the lever does nothing to
    /// anybody who has ever linked an Apple ID — which is the entire point of locking them.
    /// </remarks>
    [Fact]
    public async Task AKnownIdentityOnALockedAccountIsRefused()
    {
        var locked = new AppUser { Id = Guid.NewGuid(), Email = "locked@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(locked);
        um.Setup(m => m.IsLockedOutAsync(locked)).ReturnsAsync(true);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CanSignInAsync(locked)).ReturnsAsync(true);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .SignIn(Request(), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// A CLOSED account cannot sign in with Apple either.
    /// </summary>
    /// <remarks>
    /// RecordingSignInManager refuses a closed account through CanSignInAsync, and its own comment
    /// says Identity calls that before every sign-in "including external providers". It does not:
    /// SignInAsync mints a session unconditionally, and this endpoint called it directly. So the
    /// closure held for passwords and not for Apple.
    /// </remarks>
    [Fact]
    public async Task AKnownIdentityOnAClosedAccountIsRefused()
    {
        var closed = new AppUser { Id = Guid.NewGuid(), Email = "closed@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(closed);
        um.Setup(m => m.IsLockedOutAsync(closed)).ReturnsAsync(false);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CanSignInAsync(closed)).ReturnsAsync(false);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .SignIn(Request(), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        sim.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// The refusal says nothing about why.
    /// </summary>
    /// <remarks>
    /// Naming a closed or locked account tells a stranger the address existed here, which is the
    /// same reason every other refusal on this site is deliberately vague.
    /// </remarks>
    [Fact]
    public async Task ARefusedAccountIsNotNamedAsClosedOrLocked()
    {
        var closed = new AppUser { Id = Guid.NewGuid(), Email = "closed@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Sub)).ReturnsAsync(closed);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CanSignInAsync(closed)).ReturnsAsync(false);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .SignIn(Request(), default);

        var refusal = Assert.IsType<UnauthorizedObjectResult>(result);
        var text = refusal.Value!.ToString()!;
        Assert.DoesNotContain("closed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Linking is a sign-in too, so a refused account cannot be linked into either.</summary>
    [Fact]
    public async Task LinkingIntoARefusedAccountIsAlsoRefused()
    {
        var closed = new AppUser { Id = Guid.NewGuid(), Email = "closed@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("closed@test.com")).ReturnsAsync(closed);
        var sim = SignInManagerMock(um);
        // A closed account fails the password check as NotAllowed, which this already refuses -
        // but assert it explicitly, because the closure must hold on every door.
        sim.Setup(s => s.CheckPasswordSignInAsync(closed, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.NotAllowed);

        var result = await Build(um, new FakeValidator(new AppleIdentity(Sub, null, false, true)), sim)
            .Link(new AppleLinkRequest("a.signed.token", "closed@test.com", "right"), default);

        AssertRefusal(result, "NotAllowed");
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    // ── An address that already belongs to somebody ───────────────────────────

    /// <summary>
    /// A taken address routes to the link door, and complains about the ADDRESS.
    /// </summary>
    /// <remarks>
    /// <para>Only reachable with an address Apple did NOT verify — a verified one links further up
    /// — which is exactly when joining the two automatically would be wrong. An unverified claim
    /// on an address is not proof of holding it, so the password is what settles it.</para>
    ///
    /// <para>What this replaces was worse than either outcome: CreateAsync was allowed to fail and
    /// Identity's "Email is already taken" was printed under the @name field, where it made no
    /// sense and offered nothing to do about it.</para>
    /// </remarks>
    [Fact]
    public async Task ATakenAddressRoutesToLinkingAndNamesTheRightField()
    {
        var theirs = new AppUser { Id = Guid.NewGuid(), Email = "ben@ishaunted.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@ishaunted.com")).ReturnsAsync(theirs);

        var result = await Build(um, new FakeValidator(
            // Unverified, so it did not auto-link above.
            new AppleIdentity(Sub, "ben@ishaunted.com", EmailVerified: false, false)), SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<AppleNeedsProfileResponse>(conflict.Value);

        Assert.True(body.ShouldLinkInstead);
        Assert.Contains("already has an account", body.EmailProblem);

        // The @name was never the problem, so nothing is said about it.
        Assert.Null(body.HandleProblem);

        // And emphatically no second account.
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
    }

    /// <summary>A free address still creates the account, so nothing is blocked that need not be.</summary>
    [Fact]
    public async Task AFreeAddressStillCreatesTheAccount()
    {
        var um = UserManagerMock();   // FindByEmailAsync answers null by default

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "nobody@example.test", true, false)), SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        Assert.IsType<EmptyResult>(result);
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Once);
    }

    /// <summary>
    /// Losing the address to a race between the check and the insert answers the same way.
    /// </summary>
    /// <remarks>
    /// Two sign-ups a moment apart is the ordinary case for this, and the person should be routed
    /// rather than shown Identity's wording under whichever field it lands on.
    /// </remarks>
    [Fact]
    public async Task LosingTheAddressToARaceStillRoutesToLinking()
    {
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(
            IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateEmail",
                Description = "Email 'ben@ishaunted.com' is already taken.",
            }));

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "ben@ishaunted.com", false, false)), SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<AppleNeedsProfileResponse>(conflict.Value);

        Assert.True(body.ShouldLinkInstead);
        Assert.Null(body.HandleProblem);
        Assert.DoesNotContain("already taken", body.EmailProblem ?? string.Empty);
    }

    /// <summary>A taken HANDLE is still reported as a handle problem, under its own field.</summary>
    [Fact]
    public async Task ATakenHandleIsStillAHandleProblem()
    {
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(
            IdentityResult.Failed(new IdentityError { Description = "Handle 'ada' is already taken." }));

        var result = await Build(um, new FakeValidator(
            new AppleIdentity(Sub, "free@example.test", true, false)), SignInManagerMock(um))
            .SignIn(Request("Ada Lovelace", "ada"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<AppleNeedsProfileResponse>(conflict.Value);

        Assert.False(body.ShouldLinkInstead);
        Assert.Contains("Try another", body.HandleProblem);
    }
}
