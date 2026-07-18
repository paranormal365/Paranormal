using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Authorization;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for SuperAdminHandler — the DB-backed authorization handler that checks
/// the SuperAdmin role for both local Identity and Entra JWT principals.
/// </summary>
public class SuperAdminHandlerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<UserManager<AppUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static SuperAdminHandler BuildHandler(Mock<UserManager<AppUser>> um)
        => new SuperAdminHandler(um.Object);

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user)
        => new AuthorizationHandlerContext(
            [new SuperAdminRequirement()],
            user,
            resource: null);

    private static ClaimsPrincipal LocalSuperAdminPrincipal(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)
        ], "Bearer"));

    private static ClaimsPrincipal LocalNonAdminPrincipal(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Bearer"));

    private static ClaimsPrincipal EntraPrincipalWithAppUserId(Guid appUserId) =>
        new(new ClaimsIdentity(
        [
            new Claim(EntraClaimsTransformation.AppUserIdClaimType, appUserId.ToString()),
            new Claim("oid", Guid.NewGuid().ToString())
        ], "Entra"));

    private static ClaimsPrincipal EntraPrincipalWithOidOnly(string oid) =>
        new(new ClaimsIdentity(
        [
            new Claim("oid", oid)
        ], "Entra"));

    private static AppUser MakeUser(Guid id) => new AppUser { Id = id, Email = "test@test.com" };

    // ── Path 1: Local Identity role claim ────────────────────────────────────

    [Fact]
    public async Task LocalToken_WithSuperAdminRoleClaim_Succeeds_WithoutDbLookup()
    {
        var um = CreateUserManagerMock();
        var handler = BuildHandler(um);
        var context = BuildContext(LocalSuperAdminPrincipal(Guid.NewGuid()));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        // UserManager should NOT be called — role claim short-circuits DB lookup
        um.Verify(m => m.FindByIdAsync(It.IsAny<string>()), Times.Never);
        um.Verify(m => m.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LocalToken_WithoutSuperAdminRoleClaim_DoesNotSucceedViaClaim()
    {
        var um = CreateUserManagerMock();
        // No DB match either
        um.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AppUser?)null);

        var handler = BuildHandler(um);
        var context = BuildContext(LocalNonAdminPrincipal(Guid.NewGuid()));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    // ── Path 2: Entra token — app_user_id claim → UserManager.FindByIdAsync ──

    [Fact]
    public async Task EntraToken_AppUserIdClaim_SuperAdminUser_Succeeds()
    {
        var userId = Guid.NewGuid();
        var user   = MakeUser(userId);

        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        um.Setup(m => m.IsInRoleAsync(user, RoleNames.SuperAdmin)).ReturnsAsync(true);

        var handler = BuildHandler(um);
        var context = BuildContext(EntraPrincipalWithAppUserId(userId));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task EntraToken_AppUserIdClaim_NonSuperAdminUser_Fails()
    {
        var userId = Guid.NewGuid();
        var user   = MakeUser(userId);

        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        um.Setup(m => m.IsInRoleAsync(user, RoleNames.SuperAdmin)).ReturnsAsync(false);

        var handler = BuildHandler(um);
        var context = BuildContext(EntraPrincipalWithAppUserId(userId));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    // ── Path 3: Entra token — OID claim → UserManager.FindByLoginAsync ────────

    [Fact]
    public async Task EntraToken_OidClaim_SuperAdminUser_Succeeds()
    {
        var oid  = Guid.NewGuid().ToString();
        var user = MakeUser(Guid.NewGuid());

        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.FindByLoginAsync("Microsoft", oid)).ReturnsAsync(user);
        um.Setup(m => m.IsInRoleAsync(user, RoleNames.SuperAdmin)).ReturnsAsync(true);

        var handler = BuildHandler(um);
        var context = BuildContext(EntraPrincipalWithOidOnly(oid));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task EntraToken_OidNotLinked_Fails()
    {
        var oid = Guid.NewGuid().ToString();

        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.FindByLoginAsync("Microsoft", oid)).ReturnsAsync((AppUser?)null);

        var handler = BuildHandler(um);
        var context = BuildContext(EntraPrincipalWithOidOnly(oid));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task EntraToken_OidFound_ButNotSuperAdmin_Fails()
    {
        var oid  = Guid.NewGuid().ToString();
        var user = MakeUser(Guid.NewGuid());

        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.FindByLoginAsync("Microsoft", oid)).ReturnsAsync(user);
        um.Setup(m => m.IsInRoleAsync(user, RoleNames.SuperAdmin)).ReturnsAsync(false);

        var handler = BuildHandler(um);
        var context = BuildContext(EntraPrincipalWithOidOnly(oid));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AnonymousPrincipal_Fails()
    {
        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AppUser?)null);

        var handler = BuildHandler(um);
        var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity()));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
