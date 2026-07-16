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
/// Unit tests for MeController.Get():
///   - Local Identity user path (AppUser found via UserManager)
///   - Microsoft Entra user path (no local AppUser — falls back to token claims)
/// </summary>
public class MeControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly Guid LocalUserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EntraOid    = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>Creates a minimal mock of UserManager (many constructor args, all optional).</summary>
    private static Mock<UserManager<AppUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(
            store.Object,
            /* IOptions<IdentityOptions>               */ null!,
            /* IPasswordHasher<AppUser>                */ null!,
            /* IEnumerable<IUserValidator<AppUser>>    */ null!,
            /* IEnumerable<IPasswordValidator<AppUser>>*/ null!,
            /* ILookupNormalizer                       */ null!,
            /* IdentityErrorDescriber                  */ null!,
            /* IServiceProvider                        */ null!,
            /* ILogger<UserManager<AppUser>>           */ null!);
    }

    /// <summary>Builds a controller with a given ClaimsPrincipal as the authenticated user.</summary>
    private static MeController BuildController(
        Mock<UserManager<AppUser>> userManagerMock,
        ClaimsPrincipal principal)
    {
        var controller = new MeController(userManagerMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return controller;
    }

    /// <summary>Creates a principal representing a local Identity user.</summary>
    private static ClaimsPrincipal LocalUserPrincipal(Guid userId, string email = "local@test.com") =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email)
        ], authenticationType: "Bearer"));

    /// <summary>Creates a principal representing a Microsoft Entra OIDC user.</summary>
    private static ClaimsPrincipal EntraPrincipal(
        Guid? oid = null,
        string? preferredUsername = null,
        string? email = null) =>
        new(new ClaimsIdentity(
        [
            new Claim("oid", (oid ?? EntraOid).ToString(),
                ClaimTypes.NameIdentifier,
                "https://login.microsoftonline.com/b9f905ce/v2.0"),
            .. preferredUsername is not null
               ? new[] { new Claim("preferred_username", preferredUsername) }
               : Array.Empty<Claim>(),
            .. email is not null
               ? new[] { new Claim(ClaimTypes.Email, email) }
               : Array.Empty<Claim>()
        ], authenticationType: "Entra"));

    // ── Local Identity user ───────────────────────────────────────────────────

    [Fact]
    public async Task Get_LocalUser_ReturnsLocalUserIdAndEmail()
    {
        var umMock = CreateUserManagerMock();
        var localUser = new AppUser { Id = LocalUserId, Email = "local@test.com" };

        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync(localUser);
        umMock.Setup(m => m.IsInRoleAsync(localUser, "SuperAdmin"))
              .ReturnsAsync(false);

        var controller = BuildController(umMock, LocalUserPrincipal(LocalUserId));

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal(LocalUserId, me.UserId);
        Assert.Equal("local@test.com", me.Email);
        Assert.False(me.IsSuperAdmin);
    }

    [Fact]
    public async Task Get_LocalSuperAdminUser_ReturnsSuperAdminTrue()
    {
        var umMock = CreateUserManagerMock();
        var adminUser = new AppUser { Id = LocalUserId, Email = "admin@test.com" };

        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync(adminUser);
        umMock.Setup(m => m.IsInRoleAsync(adminUser, "SuperAdmin"))
              .ReturnsAsync(true);

        var controller = BuildController(umMock, LocalUserPrincipal(LocalUserId, "admin@test.com"));

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.True(me.IsSuperAdmin);
    }

    [Fact]
    public async Task Get_LocalNonAdminUser_ReturnsSuperAdminFalse()
    {
        var umMock = CreateUserManagerMock();
        var user = new AppUser { Id = LocalUserId, Email = "member@test.com" };

        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync(user);
        umMock.Setup(m => m.IsInRoleAsync(user, "SuperAdmin"))
              .ReturnsAsync(false);

        var controller = BuildController(umMock, LocalUserPrincipal(LocalUserId, "member@test.com"));

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.False(me.IsSuperAdmin);
    }

    [Fact]
    public async Task Get_EntraUser_WithLinkedAccount_ReturnsLocalUserData()
    {
        // When an Entra OID is linked via AspNetUserLogins, the local user's data is returned —
        // same experience as local login regardless of which Entra identity was used to sign in.
        var umMock = CreateUserManagerMock();
        var linkedUser = new AppUser { Id = LocalUserId, Email = "haveben@msn.com" };

        umMock.Setup(m => m.FindByLoginAsync("Microsoft", EntraOid.ToString()))
              .ReturnsAsync(linkedUser);
        umMock.Setup(m => m.IsInRoleAsync(linkedUser, "SuperAdmin"))
              .ReturnsAsync(true); // haveben@msn.com is SuperAdmin

        var principal = EntraPrincipal(oid: EntraOid, preferredUsername: "ben.clark@vanderbilt.edu");
        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        // Returns LOCAL user data — not the Entra email
        Assert.Equal(LocalUserId, me.UserId);
        Assert.Equal("haveben@msn.com", me.Email);
        Assert.True(me.IsSuperAdmin);
    }

    // ── Entra user (no local AppUser) ─────────────────────────────────────────

    [Fact]
    public async Task Get_EntraUser_NoLocalAccount_ReturnsGuidEmpty()
    {
        // MeController returns Guid.Empty when no linked AppUser is found.
        // This signals the WebApp to redirect to /entra/complete-profile for account setup.
        var umMock = CreateUserManagerMock();

        // No linked account for this Entra OID
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", EntraOid.ToString()))
              .ReturnsAsync((AppUser?)null);

        // No local AppUser by NameIdentifier claim either
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync((AppUser?)null);

        var principal = EntraPrincipal(oid: EntraOid, preferredUsername: "ben@example.com");
        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal(Guid.Empty, me.UserId); // Guid.Empty = "needs account setup"
    }

    [Fact]
    public async Task Get_EntraUser_PreferredUsername_UsedAsEmail()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync((AppUser?)null);

        var principal = EntraPrincipal(preferredUsername: "haveben@msn.com");
        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal("haveben@msn.com", me.Email);
    }

    [Fact]
    public async Task Get_EntraUser_FallsBackToEmailClaim_WhenNoPreferredUsername()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync((AppUser?)null);

        var principal = EntraPrincipal(email: "fallback@msn.com"); // no preferred_username
        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal("fallback@msn.com", me.Email);
    }

    [Fact]
    public async Task Get_EntraUser_IsSuperAdminAlwaysFalse()
    {
        // Entra users have no local Identity role — SuperAdmin is never granted via Entra claims.
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync((AppUser?)null);

        var principal = EntraPrincipal(preferredUsername: "admin@entra.com");
        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.False(me.IsSuperAdmin);
    }

    [Fact]
    public async Task Get_EntraUser_NoOidClaim_UsesGuidEmpty()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync((AppUser?)null);

        // Principal with no OID claim
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("preferred_username", "nooid@test.com")
        ], authenticationType: "Entra"));

        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal(Guid.Empty, me.UserId);
    }

    [Fact]
    public async Task Get_EntraUser_NoEmailClaims_ReturnsEmptyString()
    {
        var umMock = CreateUserManagerMock();
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ReturnsAsync((AppUser?)null);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", EntraOid.ToString())
        ], authenticationType: "Entra"));

        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal(string.Empty, me.Email);
    }

    // ── Entra MSA: non-GUID sub claim ─────────────────────────────────────────

    [Fact]
    public async Task Get_EntraUser_GetUserAsyncThrowsFormatException_ReturnsGuidEmpty()
    {
        // Personal Microsoft accounts (MSA) issue tokens whose 'sub' claim is a base64-like
        // string, not a GUID. UserManager.GetUserAsync tries to parse it as Guid, throwing
        // FormatException. The controller must catch this and fall through to Guid.Empty —
        // the same result as any other unlinked Entra user.
        var umMock = CreateUserManagerMock();

        // No linked account
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", It.IsAny<string>()))
              .ReturnsAsync((AppUser?)null);

        // Simulates UserStoreBase.ConvertIdFromString failing on a non-GUID sub claim
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ThrowsAsync(new FormatException("Guid should contain 32 digits with 4 dashes"));

        // Principal with a non-GUID NameIdentifier (MSA sub claim)
        var msaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", EntraOid.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "AAAAABBBBBCCCCCddddd"),  // non-GUID MSA sub
            new Claim("preferred_username", "haveben@msn.com")
        ], authenticationType: "Entra"));

        var controller = BuildController(umMock, msaPrincipal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        // Must not throw — must return Guid.Empty to trigger the complete-profile redirect
        Assert.Equal(Guid.Empty, me.UserId);
        Assert.Equal("haveben@msn.com", me.Email);
    }

    [Fact]
    public async Task Get_EntraUser_LinkedAccountAfterFormatExceptionWouldNotBeReached()
    {
        // Verifies that FindByLoginAsync is checked BEFORE GetUserAsync, so a linked MSA
        // account is found even though GetUserAsync would throw for the same principal.
        var umMock = CreateUserManagerMock();
        var linkedUser = new AppUser { Id = LocalUserId, Email = "haveben@msn.com" };

        // OID IS linked
        umMock.Setup(m => m.FindByLoginAsync("Microsoft", EntraOid.ToString()))
              .ReturnsAsync(linkedUser);
        umMock.Setup(m => m.IsInRoleAsync(linkedUser, "SuperAdmin"))
              .ReturnsAsync(true);

        // GetUserAsync would throw, but it should never be called when FindByLoginAsync succeeds
        umMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
              .ThrowsAsync(new FormatException("Should not be reached"));

        var principal = EntraPrincipal(oid: EntraOid, preferredUsername: "haveben@msn.com");
        var controller = BuildController(umMock, principal);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var me = Assert.IsType<MeResponse>(ok.Value);
        Assert.Equal(LocalUserId, me.UserId);
        Assert.True(me.IsSuperAdmin);
    }
}
