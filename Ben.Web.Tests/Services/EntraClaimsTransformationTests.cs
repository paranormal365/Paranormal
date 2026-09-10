using System.Security.Claims;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Identity;
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

    private static EntraClaimsTransformation Build(Mock<UserManager<AppUser>> um) =>
        new(um.Object, NullLogger<EntraClaimsTransformation>.Instance);

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
