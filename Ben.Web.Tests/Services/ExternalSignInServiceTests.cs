using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The one place every external door's decisions live, so this is the one matrix that has to be
/// right. Every case here was a real defect on at least one provider on 2026-09-10.
/// </summary>
public class ExternalSignInServiceTests
{
    private static readonly ExternalIdentity Apple =
        new("Apple", "001234.abcdef", "ada@example.test", EmailVerified: true);

    private static readonly ExternalIdentity AppleRelay =
        new("Apple", "001234.abcdef", "x7k2@privaterelay.appleid.com", EmailVerified: true, IsPrivateEmail: true);

    private static readonly ExternalIdentity AppleWithheld =
        new("Apple", "001234.abcdef", null, EmailVerified: false, IsPrivateEmail: true);

    /// <summary>What an Entra token gives us: an address we cannot call verified.</summary>
    private static readonly ExternalIdentity Entra =
        new("Microsoft", Guid.NewGuid().ToString(), "ben@corp.test", EmailVerified: false);

    private static Mock<UserManager<AppUser>> UserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        var mock = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        mock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        mock.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(m => m.UpdateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(m => m.DeleteAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<AppUser>())).ReturnsAsync(false);
        mock.Setup(m => m.IsLockedOutAsync(It.IsAny<AppUser>())).ReturnsAsync(false);
        return mock;
    }

    private static Mock<SignInManager<AppUser>> SignInManagerMock(Mock<UserManager<AppUser>> um)
    {
        var mock = new Mock<SignInManager<AppUser>>(
            um.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<AppUser>>().Object, null!, null!, null!, null!);
        mock.Setup(s => s.CanSignInAsync(It.IsAny<AppUser>())).ReturnsAsync(true);
        return mock;
    }

    private static ExternalSignInService Build(
        Mock<UserManager<AppUser>> um,
        Mock<SignInManager<AppUser>>? sim = null,
        Mock<IConfirmationSender>? confirmations = null)
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new ExternalSignInService(
            um.Object, (sim ?? SignInManagerMock(um)).Object, new UserHandleService(factory),
            (confirmations ?? new Mock<IConfirmationSender>()).Object);
    }

    private static AppUser Somebody(string email = "ben@ishaunted.com") =>
        new() { Id = Guid.NewGuid(), Email = email, EmailConfirmed = true };

    // ── Resolving ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AKnownSubjectIsFoundWhateverTheAddressSays()
    {
        var mine = Somebody();
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Apple.Subject)).ReturnsAsync(mine);

        // A relay address matching nothing here changes nothing: the subject already answered.
        var result = await Build(um).ResolveAsync(AppleRelay);

        var found = Assert.IsType<ResolveResult.Found>(result);
        Assert.Same(mine, found.User);
        um.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>An administrator's refusal holds on this door. SignInAsync would not have asked.</summary>
    [Theory]
    [InlineData(true,  false)]   // locked out
    [InlineData(false, true)]    // closed
    public async Task AKnownSubjectOnAnAccountThatMayNotSignInIsRefused(bool lockedOut, bool closed)
    {
        var mine = Somebody();
        if (closed) mine.DateClosed = DateTime.UtcNow;
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Apple", Apple.Subject)).ReturnsAsync(mine);
        um.Setup(m => m.IsLockedOutAsync(mine)).ReturnsAsync(lockedOut);

        Assert.IsType<ResolveResult.Refused>(await Build(um).ResolveAsync(Apple));
    }

    /// <summary>
    /// An account that has not confirmed its address may still sign in through its provider.
    /// </summary>
    /// <remarks>
    /// Confirmation proves the ADDRESS; the provider's token proves the PERSON, and the only way an
    /// account holding an external login is unconfirmed is that it was created from that very
    /// provider's unverified address claim (a website sign-up cannot link before confirming: the
    /// password check refuses it). Refusing here would strand the person the account was just
    /// made for. What confirmation still gates is the address: a password reset, and being
    /// written to. So this gate is closure and lockout, and deliberately not CanSignInAsync,
    /// which would fold the confirmed-account requirement in.
    /// </remarks>
    [Fact]
    public async Task AnUnconfirmedProviderBornAccountMayStillSignInThroughItsProvider()
    {
        var mine = new AppUser { Id = Guid.NewGuid(), Email = "ben@corp.test", EmailConfirmed = false };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Entra.Subject)).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CanSignInAsync(mine)).ReturnsAsync(false);   // what Identity would say

        Assert.IsType<ResolveResult.Found>(await Build(um, sim).ResolveAsync(Entra));
        sim.Verify(s => s.CanSignInAsync(It.IsAny<AppUser>()), Times.Never);
    }

    /// <summary>A VERIFIED address joins the account that holds it, and confirms it.</summary>
    [Fact]
    public async Task AVerifiedAddressJoinsTheAccountThatAlreadyHasIt()
    {
        var website = new AppUser { Id = Guid.NewGuid(), Email = "ada@example.test", EmailConfirmed = false };
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ada@example.test")).ReturnsAsync(website);

        var result = await Build(um).ResolveAsync(Apple);

        Assert.IsType<ResolveResult.Found>(result);
        um.Verify(m => m.AddLoginAsync(website, It.Is<UserLoginInfo>(l =>
            l.LoginProvider == "Apple" && l.ProviderKey == Apple.Subject)), Times.Once);
        Assert.True(website.EmailConfirmed);
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
    }

    /// <summary>
    /// An UNVERIFIED address never auto-links — not for Apple, and not for Microsoft, whose claim we
    /// cannot call verified. It routes to the link door instead.
    /// </summary>
    /// <remarks>
    /// This is the rule that used to be two rules. Apple's controller required <c>email_verified</c>;
    /// Entra's refused on an existing address. Both are this one sentence: an unverified claim on an
    /// address is not proof of holding it.
    /// </remarks>
    [Fact]
    public async Task AnUnverifiedAddressThatIsTakenRoutesToLinkingAndJoinsNothing()
    {
        var theirs = Somebody("ben@corp.test");
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ben@corp.test")).ReturnsAsync(theirs);

        var result = await Build(um).ResolveAsync(Entra);

        var unknown = Assert.IsType<ResolveResult.Unknown>(result);
        Assert.True(unknown.ShouldLinkInstead);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownSubjectWithAFreeAddressIsSimplyUnknown()
    {
        var unknown = Assert.IsType<ResolveResult.Unknown>(await Build(UserManagerMock()).ResolveAsync(Apple));
        Assert.False(unknown.ShouldLinkInstead);
    }

    // ── Claiming ──────────────────────────────────────────────────────────────

    private static (Mock<UserManager<AppUser>>, Mock<SignInManager<AppUser>>, AppUser) PasswordAccepted(bool twoFactor = false)
    {
        var mine = Somebody();
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(mine.Email!)).ReturnsAsync(mine);
        um.Setup(m => m.GetTwoFactorEnabledAsync(mine)).ReturnsAsync(twoFactor);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "right", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        return (um, sim, mine);
    }

    [Fact]
    public async Task ThePasswordLinksAnAccountWhoseAddressTheProviderNeverMentioned()
    {
        var (um, sim, mine) = PasswordAccepted();

        var result = await Build(um, sim).LinkAsync(AppleRelay, mine.Email!, "right", null, null);

        var linked = Assert.IsType<LinkResult.Linked>(result);
        Assert.Same(mine, linked.User);
        Assert.False(linked.AlreadyWas);
        um.Verify(m => m.AddLoginAsync(mine, It.Is<UserLoginInfo>(l => l.ProviderKey == AppleRelay.Subject)), Times.Once);
    }

    /// <summary>Unknown address and wrong password: same answer, same word.</summary>
    [Fact]
    public async Task AnUnknownAddressAnswersExactlyLikeAWrongPassword()
    {
        var (um, sim, mine) = PasswordAccepted();
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, "wrong", true))
           .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);
        var service = Build(um, sim);

        var unknown = Assert.IsType<LinkResult.Refused>(await service.LinkAsync(Apple, "nobody@nowhere.test", "x", null, null));
        var wrong   = Assert.IsType<LinkResult.Refused>(await service.LinkAsync(Apple, mine.Email!, "wrong", null, null));

        Assert.Equal(LoginRefusal.Failed, unknown.Why);
        Assert.Equal(unknown, wrong);
    }

    [Theory]
    [InlineData("LockedOut",  LoginRefusal.LockedOut)]
    [InlineData("NotAllowed", LoginRefusal.NotAllowed)]
    public async Task LockoutAndAnUnconfirmedAccountAreNamedForWhatTheyAre(string outcome, LoginRefusal expected)
    {
        var mine = Somebody();
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(mine.Email!)).ReturnsAsync(mine);
        var sim = SignInManagerMock(um);
        sim.Setup(s => s.CheckPasswordSignInAsync(mine, It.IsAny<string>(), true))
           .ReturnsAsync(outcome == "LockedOut"
               ? Microsoft.AspNetCore.Identity.SignInResult.LockedOut
               : Microsoft.AspNetCore.Identity.SignInResult.NotAllowed);

        var refused = Assert.IsType<LinkResult.Refused>(await Build(um, sim).LinkAsync(Apple, mine.Email!, "x", null, null));

        Assert.Equal(expected, refused.Why);
        // lockoutOnFailure: true is what makes a guess cost something.
        sim.Verify(s => s.CheckPasswordSignInAsync(mine, "x", true), Times.Once);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    /// <summary>
    /// A two-factor account is not claimable on a password alone.
    /// </summary>
    /// <remarks>
    /// Permanent, not momentary: afterwards the external identity signs in on its own and the code
    /// is never asked for again. CheckPasswordSignInAsync never reports this, which is how it was
    /// missed on both providers.
    /// </remarks>
    [Fact]
    public async Task ATwoFactorAccountIsRefusedWithoutACodeAndLinksNothing()
    {
        var (um, sim, mine) = PasswordAccepted(twoFactor: true);

        var refused = Assert.IsType<LinkResult.Refused>(await Build(um, sim).LinkAsync(Apple, mine.Email!, "right", null, null));

        Assert.Equal(LoginRefusal.RequiresTwoFactor, refused.Why);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task AValidCodeTypedWithSpacesLinks()
    {
        var (um, sim, mine) = PasswordAccepted(twoFactor: true);
        um.Setup(m => m.VerifyTwoFactorTokenAsync(mine, It.IsAny<string>(), "123456")).ReturnsAsync(true);

        Assert.IsType<LinkResult.Linked>(await Build(um, sim).LinkAsync(Apple, mine.Email!, "right", "123 456", null));
    }

    /// <summary>A recovery code is REDEEMED. One that survived being used would not be one.</summary>
    [Fact]
    public async Task ARecoveryCodeIsRedeemedNotChecked()
    {
        var (um, sim, mine) = PasswordAccepted(twoFactor: true);
        um.Setup(m => m.RedeemTwoFactorRecoveryCodeAsync(mine, "abcd1234")).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<LinkResult.Linked>(await Build(um, sim).LinkAsync(Apple, mine.Email!, "right", null, "abcd-1234"));

        um.Verify(m => m.RedeemTwoFactorRecoveryCodeAsync(mine, "abcd1234"), Times.Once);
        um.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnIdentityHeldByAnotherAccountCannotBeMovedByWhoeverAsksLast()
    {
        var (um, sim, mine) = PasswordAccepted();
        um.Setup(m => m.FindByLoginAsync("Apple", Apple.Subject)).ReturnsAsync(Somebody("someone@else.test"));

        Assert.IsType<LinkResult.HeldByAnotherAccount>(await Build(um, sim).LinkAsync(Apple, mine.Email!, "right", null, null));
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task LinkingWhatIsAlreadyLinkedToThisAccountIsNotAnError()
    {
        var (um, sim, mine) = PasswordAccepted();
        um.Setup(m => m.FindByLoginAsync("Apple", Apple.Subject)).ReturnsAsync(mine);

        var linked = Assert.IsType<LinkResult.Linked>(await Build(um, sim).LinkAsync(Apple, mine.Email!, "right", null, null));
        Assert.True(linked.AlreadyWas);
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    // ── Creating ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACreatedAccountRecordsWhatKindOfAddressTheProviderGave()
    {
        AppUser? created = null;
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);
        var service = Build(um);

        Assert.IsType<RegisterResult.Created>(await service.RegisterAsync(AppleRelay, "Ada Lovelace", "ada"));
        Assert.Equal(EmailAddressKind.AppleRelay, created!.EmailKind);

        Assert.IsType<RegisterResult.Created>(await service.RegisterAsync(AppleWithheld, "Ada Lovelace", "ada2"));
        Assert.Equal(EmailAddressKind.Unreachable, created!.EmailKind);
        Assert.EndsWith("@appleid.invalid", created.Email);

        Assert.IsType<RegisterResult.Created>(await service.RegisterAsync(Apple, "Ada Lovelace", "ada3"));
        Assert.Equal(EmailAddressKind.Ordinary, created!.EmailKind);
        Assert.True(created.EmailConfirmed);
    }

    /// <summary>
    /// An address the provider did NOT verify starts unconfirmed and is sent a confirmation, exactly
    /// as a website sign-up is.
    /// </summary>
    /// <remarks>
    /// Marking it confirmed let anybody create an account here under an address they do not own —
    /// not a takeover (a taken address is refused) but a squat: the real owner then finds their
    /// address held and cannot sign up with it. Decided with Ben 2026-09-10. The account is still
    /// usable through the provider that made it (see the gate test above); what it cannot do until
    /// confirmed is reset a password or be written to.
    /// </remarks>
    [Fact]
    public async Task AnUnverifiedAddressStartsUnconfirmedAndIsSentAConfirmation()
    {
        AppUser? created = null;
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);
        var confirmations = new Mock<IConfirmationSender>();
        confirmations.Setup(c => c.SendConfirmationAsync(It.IsAny<AppUser>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Build(um, confirmations: confirmations).RegisterAsync(Entra, "Ben Clark", handle: null);

        var made = Assert.IsType<RegisterResult.Created>(result);
        Assert.True(made.AwaitingConfirmation);
        Assert.False(created!.EmailConfirmed);
        Assert.Equal(EmailAddressKind.Ordinary, created.EmailKind);
        confirmations.Verify(c => c.SendConfirmationAsync(created, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A verified address, and a withheld one, have nothing to confirm and are sent nothing.
    /// </summary>
    /// <remarks>
    /// The placeholder behind a withheld address can never receive mail, and leaving that account
    /// unconfirmed would give it a state it could never leave. Apple authenticated the person; the
    /// account is complete.
    /// </remarks>
    [Theory]
    [InlineData("verified")]
    [InlineData("withheld")]
    public async Task AVerifiedOrWithheldAddressIsConfirmedAndSentNothing(string which)
    {
        AppUser? created = null;
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);
        var confirmations = new Mock<IConfirmationSender>();

        var result = await Build(um, confirmations: confirmations)
            .RegisterAsync(which == "verified" ? Apple : AppleWithheld, "Ada Lovelace", "ada");

        var made = Assert.IsType<RegisterResult.Created>(result);
        Assert.False(made.AwaitingConfirmation);
        Assert.True(created!.EmailConfirmed);
        confirmations.Verify(c => c.SendConfirmationAsync(It.IsAny<AppUser>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A confirmation that could not be sent does not fail the creation. The account exists and the
    /// person is signed in through their provider; the profile offers to resend.
    /// </summary>
    [Fact]
    public async Task AConfirmationThatCouldNotBeSentDoesNotFailTheCreation()
    {
        var um = UserManagerMock();
        var confirmations = new Mock<IConfirmationSender>();
        confirmations.Setup(c => c.SendConfirmationAsync(It.IsAny<AppUser>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var made = Assert.IsType<RegisterResult.Created>(
            await Build(um, confirmations: confirmations).RegisterAsync(Entra, "Ben Clark", handle: null));
        Assert.True(made.AwaitingConfirmation);
        um.Verify(m => m.DeleteAsync(It.IsAny<AppUser>()), Times.Never);
    }

    /// <summary>A handle is permanent, so a caller that can ask must ask; only one that cannot gets one allocated.</summary>
    [Fact]
    public async Task AHandleIsAskedForUnlessTheCallerCannotAsk()
    {
        var um = UserManagerMock();
        var service = Build(um);

        // Asked for and missing: not created, not invented.
        Assert.IsType<RegisterResult.NeedsProfile>(await service.RegisterAsync(Apple, "Ada Lovelace", ""));
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);

        // Null means "allocate" - the Microsoft flow has no handle field to ask with.
        AppUser? created = null;
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).Callback<AppUser>(u => created = u).ReturnsAsync(IdentityResult.Success);
        Assert.IsType<RegisterResult.Created>(await service.RegisterAsync(Entra, "Ben Clark", handle: null));
        Assert.False(string.IsNullOrWhiteSpace(created!.Handle));
    }

    [Fact]
    public async Task ATakenAddressIsRoutedToLinkingAndNothingIsCreated()
    {
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync("ada@example.test")).ReturnsAsync(Somebody("ada@example.test"));

        Assert.IsType<RegisterResult.AddressTaken>(await Build(um).RegisterAsync(Apple, "Ada Lovelace", "ada"));
        um.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
    }

    [Fact]
    public async Task LosingTheAddressToARaceStillRoutesToLinking()
    {
        var um = UserManagerMock();
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(
            IdentityResult.Failed(new IdentityError { Description = "Email 'ada@example.test' is already taken." }));

        Assert.IsType<RegisterResult.AddressTaken>(await Build(um).RegisterAsync(Apple, "Ada Lovelace", "ada"));
    }

    [Fact]
    public async Task AFailedLinkAfterCreationRollsTheAccountBack()
    {
        var um = UserManagerMock();
        um.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "boom" }));

        Assert.IsType<RegisterResult.Failed>(await Build(um).RegisterAsync(Apple, "Ada Lovelace", "ada"));
        um.Verify(m => m.DeleteAsync(It.IsAny<AppUser>()), Times.Once);
    }
}
