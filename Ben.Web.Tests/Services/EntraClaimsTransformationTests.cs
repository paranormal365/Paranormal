using System.Security.Claims;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The one place an Entra caller becomes somebody here.
/// </summary>
/// <remarks>
/// It matters more than an ordinary lookup because a Microsoft token IS the credential — there is
/// no sign-in step downstream to run the checks a password goes through. Whatever this decides is
/// the whole of the decision.
/// </remarks>
public class EntraClaimsTransformationTests
{
    private static readonly Guid Oid = Guid.NewGuid();

    private static Mock<UserManager<AppUser>> UserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        var mock = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.IsLockedOutAsync(It.IsAny<AppUser>())).ReturnsAsync(false);
        mock.Setup(m => m.GetRolesAsync(It.IsAny<AppUser>())).ReturnsAsync(new List<string>());
        return mock;
    }

    private static ClaimsPrincipal EntraPrincipal() =>
        new(new ClaimsIdentity([new Claim("oid", Oid.ToString())], authenticationType: "Entra"));

    private static EntraClaimsTransformation Build(Mock<UserManager<AppUser>> um)
    {
        // The gate is the shared service's, built from the SAME user-manager mock, so what these
        // tests say about lockout is what the decision sees.
        var sim = new Mock<SignInManager<AppUser>>(
            um.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<AppUser>>().Object, null!, null!, null!, null!);
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var external = new ExternalSignInService(
            um.Object, sim.Object, new UserHandleService(factory), new Mock<IConfirmationSender>().Object);
        return new EntraClaimsTransformation(um.Object, external, NullLogger<EntraClaimsTransformation>.Instance);
    }

    private static bool Resolved(ClaimsPrincipal principal) =>
        principal.HasClaim(c => c.Type == EntraClaimsTransformation.AppUserIdClaimType);

    [Fact]
    public async Task AnOrdinaryLinkedAccountResolves()
    {
        var user = new AppUser { Id = Guid.NewGuid(), Email = "ben@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Oid.ToString())).ReturnsAsync(user);

        var result = await Build(um).TransformAsync(EntraPrincipal());

        Assert.True(Resolved(result));
        Assert.Equal(user.Id.ToString(),
            result.FindFirstValue(EntraClaimsTransformation.AppUserIdClaimType));
    }

    /// <summary>
    /// A CLOSED account does not resolve, so a Microsoft identity cannot walk back into it.
    /// </summary>
    /// <remarks>
    /// Closure was enforced by RecordingSignInManager, which only ever runs on a password sign-in.
    /// A Microsoft token never touches it, so the closure held for one door and not the other.
    /// </remarks>
    [Fact]
    public async Task AClosedAccountDoesNotResolve()
    {
        var closed = new AppUser { Id = Guid.NewGuid(), Email = "closed@test.com", DateClosed = DateTime.UtcNow };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Oid.ToString())).ReturnsAsync(closed);

        var result = await Build(um).TransformAsync(EntraPrincipal());

        Assert.False(Resolved(result));
    }

    /// <summary>
    /// A LOCKED account does not resolve either.
    /// </summary>
    /// <remarks>
    /// Lockout is the only lever an administrator has short of closing an account. If a Microsoft
    /// identity ignores it, the lever does nothing to anybody who has ever linked one.
    /// </remarks>
    [Fact]
    public async Task ALockedAccountDoesNotResolve()
    {
        var locked = new AppUser { Id = Guid.NewGuid(), Email = "locked@test.com" };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Oid.ToString())).ReturnsAsync(locked);
        um.Setup(m => m.IsLockedOutAsync(locked)).ReturnsAsync(true);

        var result = await Build(um).TransformAsync(EntraPrincipal());

        Assert.False(Resolved(result));
    }

    /// <summary>
    /// A refused account does not get its ROLES either.
    /// </summary>
    /// <remarks>
    /// Worth its own assertion: leaking the roles of an account that may not be used would let a
    /// closed administrator keep administrative claims on a principal the site still honours.
    /// </remarks>
    [Fact]
    public async Task ARefusedAccountCarriesNoRoles()
    {
        var closed = new AppUser { Id = Guid.NewGuid(), DateClosed = DateTime.UtcNow };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Oid.ToString())).ReturnsAsync(closed);
        um.Setup(m => m.GetRolesAsync(closed)).ReturnsAsync(new List<string> { "SuperAdmin" });

        var result = await Build(um).TransformAsync(EntraPrincipal());

        Assert.False(result.IsInRole("SuperAdmin"));
    }

    /// <summary>
    /// An UNLINKED Microsoft identity does not become somebody here because its address matches.
    /// </summary>
    /// <remarks>
    /// This was the email fallback: an object id nobody had linked was looked up by its
    /// <c>email</c>, <c>preferred_username</c> or <c>upn</c> claim, and the account found was
    /// linked to it for good. Microsoft does not vouch for the <c>email</c> claim of a personal
    /// account, and this authority accepts personal accounts, so anybody could reach an account by
    /// putting its address on a Microsoft account of their own. Decided with Ben 2026-09-10:
    /// removed, not narrowed. A rotated object id uses the link door once, with a password.
    /// </remarks>
    [Theory]
    [InlineData("email")]
    [InlineData("preferred_username")]
    [InlineData("upn")]
    public async Task AnUnlinkedIdentityIsNotResolvedByItsAddressClaim(string claimType)
    {
        var theirs = new AppUser { Id = Guid.NewGuid(), Email = "victim@test.com", EmailConfirmed = true };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Oid.ToString())).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.FindByEmailAsync("victim@test.com")).ReturnsAsync(theirs);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", Oid.ToString()), new Claim(claimType, "victim@test.com")], authenticationType: "Entra"));

        var result = await Build(um).TransformAsync(principal);

        Assert.False(Resolved(result));
        um.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        um.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// An account that has not confirmed its address still resolves. Confirmation proves the
    /// address; the Microsoft token proves the person, and an account created from an unverified
    /// Microsoft address (see ExternalSignInServiceTests) is exactly one of these.
    /// </summary>
    [Fact]
    public async Task AnUnconfirmedLinkedAccountStillResolves()
    {
        var user = new AppUser { Id = Guid.NewGuid(), Email = "new@test.com", EmailConfirmed = false };
        var um = UserManagerMock();
        um.Setup(m => m.FindByLoginAsync("Microsoft", Oid.ToString())).ReturnsAsync(user);

        Assert.True(Resolved(await Build(um).TransformAsync(EntraPrincipal())));
    }

    /// <summary>A principal with no Microsoft identity at all is left alone.</summary>
    [Fact]
    public async Task APrincipalWithNoOidIsUntouched()
    {
        var um = UserManagerMock();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await Build(um).TransformAsync(principal);

        Assert.False(Resolved(result));
        um.Verify(m => m.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
