using System.Security.Claims;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Unit tests for EntraAuthController:
///   - POST /api/auth/entra/register — creates local AppUser and links Entra OID
///   - POST /api/auth/entra/link    — links Entra OID to existing authenticated user
/// </summary>
public class EntraAuthControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly string ValidOid   = Guid.NewGuid().ToString();
    private static readonly Guid   UserId     = Guid.NewGuid();

    private static Mock<UserManager<AppUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static EntraAuthController BuildController(
        Mock<UserManager<AppUser>> umMock,
        ClaimsPrincipal? principal = null)
    {
        var controller = new EntraAuthController(umMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal ?? new ClaimsPrincipal()
            }
        };
        return controller;
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], authenticationType: "Bearer"));

    // ── Register — happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task Register_NewUser_CreatesAccountAndReturnsUserId()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("new@test.com"))
              .ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid))
              .ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
              .ReturnsAsync(IdentityResult.Success);
        umMock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock);
        var request = new EntraRegisterRequest(ValidOid, "new@test.com", "New User");

        var result = await controller.Register(request, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var reg = Assert.IsType<EntraRegisterResult>(ok.Value);
        Assert.Equal("new@test.com", reg.Email);
        Assert.NotEqual(Guid.Empty, reg.UserId);
    }

    [Fact]
    public async Task Register_NewUser_SetsEmailConfirmedTrue()
    {
        // Entra has already verified the email — EmailConfirmed should be true on creation.
        var umMock = CreateUserManagerMock();
        AppUser? capturedUser = null;
        umMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
              .Callback<AppUser>(u => capturedUser = u)
              .ReturnsAsync(IdentityResult.Success);
        umMock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock);
        await controller.Register(new EntraRegisterRequest(ValidOid, "e@t.com", "Name"), default);

        Assert.NotNull(capturedUser);
        Assert.True(capturedUser!.EmailConfirmed);
    }

    [Fact]
    public async Task Register_LinksCorrectOidAndProvider()
    {
        var umMock = CreateUserManagerMock();
        UserLoginInfo? capturedLogin = null;
        umMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        umMock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
              .Callback<AppUser, UserLoginInfo>((_, l) => capturedLogin = l)
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock);
        await controller.Register(new EntraRegisterRequest(ValidOid, "e@t.com", "Name", "e@t.com"), default);

        Assert.NotNull(capturedLogin);
        Assert.Equal("Microsoft",  capturedLogin!.LoginProvider);
        Assert.Equal(ValidOid,     capturedLogin.ProviderKey);
        Assert.Equal("e@t.com",    capturedLogin.ProviderDisplayName);
    }

    // ── Register — validation guards ─────────────────────────────────────────

    [Fact]
    public async Task Register_EmptyEmail_ReturnsBadRequest()
    {
        var controller = BuildController(CreateUserManagerMock());
        var result = await controller.Register(
            new EntraRegisterRequest(ValidOid, "", "Name"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_InvalidOid_ReturnsBadRequest()
    {
        var controller = BuildController(CreateUserManagerMock());
        var result = await controller.Register(
            new EntraRegisterRequest("not-a-guid", "e@t.com", "Name"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_EmailAlreadyExists_ReturnsConflict()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("existing@test.com"))
              .ReturnsAsync(new AppUser { Id = UserId, Email = "existing@test.com" });

        var controller = BuildController(umMock);
        var result = await controller.Register(
            new EntraRegisterRequest(ValidOid, "existing@test.com", "Name"), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_OidAlreadyLinked_ReturnsExistingUser()
    {
        // Idempotent: if the OID is already linked, return that user's info without creating a duplicate.
        var existingUser = new AppUser { Id = UserId, Email = "linked@test.com" };
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync(existingUser);

        var controller = BuildController(umMock);
        var result = await controller.Register(
            new EntraRegisterRequest(ValidOid, "e@t.com", "Name"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var reg = Assert.IsType<EntraRegisterResult>(ok.Value);
        Assert.Equal(UserId, reg.UserId);
        Assert.Equal("linked@test.com", reg.Email);
        umMock.Verify(m => m.CreateAsync(It.IsAny<AppUser>()), Times.Never);
    }

    [Fact]
    public async Task Register_CreateFails_ReturnsBadRequest()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "DB error" }));

        var controller = BuildController(umMock);
        var result = await controller.Register(
            new EntraRegisterRequest(ValidOid, "e@t.com", "Name"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_LinkFails_DeletesUserAndReturnsBadRequest()
    {
        // If AddLoginAsync fails after CreateAsync succeeds, the user is rolled back.
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.CreateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        umMock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Link error" }));
        umMock.Setup(m => m.DeleteAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock);
        var result = await controller.Register(
            new EntraRegisterRequest(ValidOid, "e@t.com", "Name"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        umMock.Verify(m => m.DeleteAsync(It.IsAny<AppUser>()), Times.Once);
    }

    // ── Link — happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Link_NewOid_LinksSuccessfully_ReturnsOk()
    {
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        var umMock    = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(localUser);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.AddLoginAsync(localUser, It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock, AuthenticatedPrincipal(UserId));
        var result = await controller.Link(
            new EntraLinkRequest(ValidOid, "entra@test.com"), default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Link_NewOid_LinksWithCorrectProvider()
    {
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        UserLoginInfo? captured = null;
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(localUser);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.AddLoginAsync(localUser, It.IsAny<UserLoginInfo>()))
              .Callback<AppUser, UserLoginInfo>((_, l) => captured = l)
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock, AuthenticatedPrincipal(UserId));
        await controller.Link(new EntraLinkRequest(ValidOid, "entra@test.com", "entra@test.com"), default);

        Assert.NotNull(captured);
        Assert.Equal("Microsoft",       captured!.LoginProvider);
        Assert.Equal(ValidOid,          captured.ProviderKey);
        Assert.Equal("entra@test.com",  captured.ProviderDisplayName);
    }

    // ── Link — guard cases ────────────────────────────────────────────────────

    [Fact]
    public async Task Link_InvalidOid_ReturnsBadRequest()
    {
        var controller = BuildController(CreateUserManagerMock());
        var result = await controller.Link(
            new EntraLinkRequest("not-a-guid", "e@t.com"), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Link_NoLocalUser_ReturnsUnauthorized()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((AppUser?)null);

        var controller = BuildController(umMock, AuthenticatedPrincipal(UserId));
        var result = await controller.Link(
            new EntraLinkRequest(ValidOid, "e@t.com"), default);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Link_OidAlreadyLinkedToSameUser_ReturnsOk()
    {
        // Idempotent: linking the same OID twice to the same account is harmless.
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        var umMock    = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(localUser);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync(localUser);

        var controller = BuildController(umMock, AuthenticatedPrincipal(UserId));
        var result = await controller.Link(
            new EntraLinkRequest(ValidOid, "e@t.com"), default);

        Assert.IsType<OkObjectResult>(result);
        umMock.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task Link_OidAlreadyLinkedToDifferentUser_ReturnsConflict()
    {
        var localUser   = new AppUser { Id = UserId, Email = "local@test.com" };
        var differentId = Guid.NewGuid();
        var otherUser   = new AppUser { Id = differentId, Email = "other@test.com" };

        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(localUser);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync(otherUser);

        var controller = BuildController(umMock, AuthenticatedPrincipal(UserId));
        var result = await controller.Link(
            new EntraLinkRequest(ValidOid, "e@t.com"), default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Link_AddLoginFails_ReturnsBadRequest()
    {
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        var umMock    = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(localUser);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.AddLoginAsync(localUser, It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Store error" }));

        var controller = BuildController(umMock, AuthenticatedPrincipal(UserId));
        var result = await controller.Link(
            new EntraLinkRequest(ValidOid, "e@t.com"), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
