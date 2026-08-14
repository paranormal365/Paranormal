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
///   - POST /api/auth/entra/register — creates a local AppUser from the caller's validated
///     Entra identity (OID/email read from claims, never the request body) and links it
///   - POST /api/auth/entra/link    — links the caller's validated Entra identity to an
///     existing local account, identified + proven by email/password in the body
///
/// Both actions require [Authorize(Policy = AuthPolicyNames.EntraOnly)] in production — that
/// attribute isn't exercised by these controller-level unit tests (no auth pipeline runs), so
/// the tests instead assert the action body's own claim-reading and validation guards, which is
/// where the actual security fix lives: neither action ever trusts a client-supplied OID/email.
/// </summary>
public class EntraAuthControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly string ValidOid = Guid.NewGuid().ToString();
    private static readonly Guid   UserId   = Guid.NewGuid();

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

    /// <summary>Simulates the principal the "Entra" JWT bearer scheme would produce — carries the
    /// validated "oid" and "preferred_username" claims that <c>GetValidatedEntraIdentity</c> reads.</summary>
    private static ClaimsPrincipal EntraPrincipal(string oid, string email) =>
        new(new ClaimsIdentity(
        [
            new Claim("oid", oid),
            new Claim("preferred_username", email)
        ], authenticationType: "Entra"));

    /// <summary>A principal with no "oid" claim at all — e.g. what a local Identity bearer token
    /// (never carrying Entra claims) would produce if it somehow reached these actions.</summary>
    private static readonly ClaimsPrincipal NoOidPrincipal =
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], authenticationType: "Bearer"));

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

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "new@test.com"));
        var request = new EntraRegisterRequest("New User");

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

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "e@t.com"));
        await controller.Register(new EntraRegisterRequest("Name"), default);

        Assert.NotNull(capturedUser);
        Assert.True(capturedUser!.EmailConfirmed);
    }

    [Fact]
    public async Task Register_UsesOidAndEmailFromValidatedClaims_NotFromBody()
    {
        // The core of the fix: even though the request body carries no identity fields at all,
        // the created login is tied to whatever OID/email the (simulated) validated token carried.
        var umMock = CreateUserManagerMock();
        UserLoginInfo? capturedLogin = null;
        AppUser? capturedUser = null;
        umMock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.CreateAsync(It.IsAny<AppUser>()))
              .Callback<AppUser>(u => capturedUser = u)
              .ReturnsAsync(IdentityResult.Success);
        umMock.Setup(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()))
              .Callback<AppUser, UserLoginInfo>((_, l) => capturedLogin = l)
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "claims-email@test.com"));
        await controller.Register(new EntraRegisterRequest("Name"), default);

        Assert.NotNull(capturedLogin);
        Assert.Equal("Microsoft", capturedLogin!.LoginProvider);
        Assert.Equal(ValidOid, capturedLogin.ProviderKey);
        Assert.Equal("claims-email@test.com", capturedUser!.Email);
    }

    // ── Register — validation guards ─────────────────────────────────────────

    [Fact]
    public async Task Register_NoOidClaim_ReturnsBadRequest()
    {
        // Simulates a caller whose token isn't actually a validated Entra JWT (no "oid" claim) —
        // in production the EntraOnly policy would already have rejected this, but the action
        // itself must not proceed as if it had a valid identity either.
        var controller = BuildController(CreateUserManagerMock(), NoOidPrincipal);
        var result = await controller.Register(new EntraRegisterRequest("Name"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_EmptyDisplayName_ReturnsBadRequest()
    {
        var controller = BuildController(CreateUserManagerMock(), EntraPrincipal(ValidOid, "e@t.com"));
        var result = await controller.Register(new EntraRegisterRequest(""), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_EmailAlreadyExists_ReturnsConflict()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("existing@test.com"))
              .ReturnsAsync(new AppUser { Id = UserId, Email = "existing@test.com" });

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "existing@test.com"));
        var result = await controller.Register(new EntraRegisterRequest("Name"), default);

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

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "e@t.com"));
        var result = await controller.Register(new EntraRegisterRequest("Name"), default);

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

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "e@t.com"));
        var result = await controller.Register(new EntraRegisterRequest("Name"), default);

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

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "e@t.com"));
        var result = await controller.Register(new EntraRegisterRequest("Name"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        umMock.Verify(m => m.DeleteAsync(It.IsAny<AppUser>()), Times.Once);
    }

    // ── Link — happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Link_ValidCredentials_LinksSuccessfully_ReturnsOk()
    {
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        var umMock    = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("local@test.com")).ReturnsAsync(localUser);
        umMock.Setup(m => m.CheckPasswordAsync(localUser, "correct-password")).ReturnsAsync(true);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.AddLoginAsync(localUser, It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "entra@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("local@test.com", "correct-password"), default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Link_UsesOidFromValidatedClaims_NotFromBody()
    {
        // The body carries only email/password now — the OID being linked comes entirely from
        // the (simulated) validated Entra token, which is the actual fix under test.
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        UserLoginInfo? captured = null;
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("local@test.com")).ReturnsAsync(localUser);
        umMock.Setup(m => m.CheckPasswordAsync(localUser, "correct-password")).ReturnsAsync(true);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.AddLoginAsync(localUser, It.IsAny<UserLoginInfo>()))
              .Callback<AppUser, UserLoginInfo>((_, l) => captured = l)
              .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "entra@test.com"));
        await controller.Link(new EntraLinkRequest("local@test.com", "correct-password"), default);

        Assert.NotNull(captured);
        Assert.Equal("Microsoft", captured!.LoginProvider);
        Assert.Equal(ValidOid, captured.ProviderKey);
    }

    // ── Link — guard cases ────────────────────────────────────────────────────

    [Fact]
    public async Task Link_NoOidClaim_ReturnsBadRequest()
    {
        var controller = BuildController(CreateUserManagerMock(), NoOidPrincipal);
        var result = await controller.Link(
            new EntraLinkRequest("e@t.com", "password"), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Link_EmptyPassword_ReturnsBadRequest()
    {
        var controller = BuildController(CreateUserManagerMock(), EntraPrincipal(ValidOid, "entra@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("e@t.com", ""), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Link_NoAccountForEmail_ReturnsUnauthorized()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("nobody@test.com")).ReturnsAsync((AppUser?)null);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "entra@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("nobody@test.com", "password"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Link_WrongPassword_ReturnsUnauthorized()
    {
        // This is the actual attack this endpoint used to be vulnerable to: without a password
        // check, presenting any target email would be enough to hijack that account's future
        // Entra sign-ins. Confirms the fix rejects it.
        var localUser = new AppUser { Id = UserId, Email = "victim@test.com" };
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("victim@test.com")).ReturnsAsync(localUser);
        umMock.Setup(m => m.CheckPasswordAsync(localUser, "wrong-password")).ReturnsAsync(false);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "attacker@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("victim@test.com", "wrong-password"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        umMock.Verify(m => m.AddLoginAsync(It.IsAny<AppUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task Link_OidAlreadyLinkedToSameUser_ReturnsOk()
    {
        // Idempotent: linking the same OID twice to the same account is harmless.
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        var umMock    = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("local@test.com")).ReturnsAsync(localUser);
        umMock.Setup(m => m.CheckPasswordAsync(localUser, "correct-password")).ReturnsAsync(true);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync(localUser);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "entra@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("local@test.com", "correct-password"), default);

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
        umMock.Setup(m => m.FindByEmailAsync("local@test.com")).ReturnsAsync(localUser);
        umMock.Setup(m => m.CheckPasswordAsync(localUser, "correct-password")).ReturnsAsync(true);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync(otherUser);

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "entra@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("local@test.com", "correct-password"), default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Link_AddLoginFails_ReturnsBadRequest()
    {
        var localUser = new AppUser { Id = UserId, Email = "local@test.com" };
        var umMock    = CreateUserManagerMock();
        umMock.Setup(m => m.FindByEmailAsync("local@test.com")).ReturnsAsync(localUser);
        umMock.Setup(m => m.CheckPasswordAsync(localUser, "correct-password")).ReturnsAsync(true);
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", ValidOid)).ReturnsAsync((AppUser?)null);
        umMock.Setup(m => m.AddLoginAsync(localUser, It.IsAny<UserLoginInfo>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Store error" }));

        var controller = BuildController(umMock, EntraPrincipal(ValidOid, "entra@test.com"));
        var result = await controller.Link(
            new EntraLinkRequest("local@test.com", "correct-password"), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
