using System.Text;
using Ben.Web.WebApp.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

public class WebApiAuthServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly Guid SuperAdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TargetUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static string MakeJwt(Guid userId, string role)
    {
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($$$"""{"sub":"{{{userId}}}","role":"{{{role}}}","exp":9999999999}"""));
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        return $"{header}.{payload}.sig";
    }

    private static WebApiTokenResponse TokenResponse(Guid userId, string role, string refresh = "rt") =>
        new() { AccessToken = MakeJwt(userId, role), RefreshToken = refresh, ExpiresIn = 3600 };

    private static (WebApiAuthService Svc, WebApiTokenStore Store,
        Mock<IWebApiIdentityClient> IdClient, Mock<IWebApiClient> ApiClient)
        Build()
    {
        var store    = new WebApiTokenStore();
        var idMock   = new Mock<IWebApiIdentityClient>();
        var apiMock  = new Mock<IWebApiClient>();
        var svc      = new WebApiAuthService(idMock.Object, apiMock.Object, store);
        return (svc, store, idMock, apiMock);
    }

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_Success_SetsEmailFromParameter()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync("admin@test.com", "pass", default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));

        var ok = await svc.LoginAsync("admin@test.com", "pass");

        Assert.True(ok);
        Assert.Equal("admin@test.com", store.UserEmail);
    }

    [Fact]
    public async Task LoginAsync_Success_SetsUserIdFromJwt()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));

        await svc.LoginAsync("admin@test.com", "pass");

        Assert.Equal(SuperAdminId, store.UserId);
    }

    [Fact]
    public async Task LoginAsync_SuperAdminRole_SetsSuperAdminFlag()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));

        await svc.LoginAsync("admin@test.com", "pass");

        Assert.True(store.IsSuperAdmin);
    }

    [Fact]
    public async Task LoginAsync_NonAdminRole_DoesNotSetSuperAdminFlag()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(TargetUserId, "Member"));

        await svc.LoginAsync("user@test.com", "pass");

        Assert.False(store.IsSuperAdmin);
    }

    [Fact]
    public async Task LoginAsync_WhenApiFails_ReturnsFalse()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync((WebApiTokenResponse?)null);

        var ok = await svc.LoginAsync("user@test.com", "badpass");

        Assert.False(ok);
        Assert.Null(store.AccessToken);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ClearsAllAuthFields()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));

        await svc.LoginAsync("admin@test.com", "pass");
        svc.Logout();

        Assert.Null(store.AccessToken);
        Assert.Null(store.RefreshToken);
        Assert.Null(store.UserEmail);
        Assert.Null(store.UserId);
        Assert.False(store.IsSuperAdmin);
        Assert.False(store.IsImpersonating);
    }

    // ── ImpersonateAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImpersonateAsync_SavesOriginalSuperAdminTokens()
    {
        var (svc, store, idMock, apiMock) = Build();

        // Log in as SuperAdmin first
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin", "admin-refresh"));
        await svc.LoginAsync("admin@test.com", "pass");

        var originalToken = store.AccessToken;

        // Mock impersonation API call
        apiMock.Setup(x => x.ImpersonateAsync(TargetUserId, default))
               .ReturnsAsync(TokenResponse(TargetUserId, "Member", "target-refresh"));

        await svc.ImpersonateAsync(TargetUserId, "target@test.com");

        // Original tokens preserved
        Assert.Equal(originalToken, store.OriginalAccessToken);
        Assert.Equal("admin-refresh", store.OriginalRefreshToken);
        Assert.Equal(SuperAdminId, store.OriginalUserId);
        Assert.Equal("admin@test.com", store.OriginalUserEmail);
    }

    [Fact]
    public async Task ImpersonateAsync_AppliesTargetUserTokenAndEmail()
    {
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));
        await svc.LoginAsync("admin@test.com", "pass");

        apiMock.Setup(x => x.ImpersonateAsync(TargetUserId, default))
               .ReturnsAsync(TokenResponse(TargetUserId, "Member", "target-refresh"));

        var ok = await svc.ImpersonateAsync(TargetUserId, "target@test.com");

        Assert.True(ok);
        Assert.Equal(TargetUserId, store.UserId);
        Assert.Equal("target@test.com", store.UserEmail);
        Assert.False(store.IsSuperAdmin);   // target is not SuperAdmin
        Assert.True(store.IsImpersonating);
    }

    // ── StopImpersonating ─────────────────────────────────────────────────────

    [Fact]
    public async Task StopImpersonating_RestoresOriginalTokensAndFlags()
    {
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin", "admin-refresh"));
        await svc.LoginAsync("admin@test.com", "pass");

        var adminToken = store.AccessToken!;

        apiMock.Setup(x => x.ImpersonateAsync(TargetUserId, default))
               .ReturnsAsync(TokenResponse(TargetUserId, "Member"));
        await svc.ImpersonateAsync(TargetUserId, "target@test.com");

        // The restored token is the Identity API's real opaque (non-JWT) shape in
        // production, so StopImpersonatingAsync can only re-derive IsSuperAdmin via
        // /api/me — assert that path is actually exercised, not a JWT re-parse.
        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ReturnsAsync(new MeResult(SuperAdminId, "admin@test.com", true));

        await svc.StopImpersonatingAsync();

        Assert.Equal(adminToken, store.AccessToken);
        Assert.Equal("admin-refresh", store.RefreshToken);
        Assert.Equal("admin@test.com", store.UserEmail);
        Assert.Equal(SuperAdminId, store.UserId);
        Assert.True(store.IsSuperAdmin);
        Assert.False(store.IsImpersonating);
    }

    [Fact]
    public async Task StopImpersonating_WhenNotImpersonating_DoesNothing()
    {
        var (svc, store, _, _) = Build();
        store.AccessToken = "original";

        await svc.StopImpersonatingAsync(); // should not throw or change state

        Assert.Equal("original", store.AccessToken);
        Assert.False(store.IsImpersonating);
    }

    [Fact]
    public async Task StopImpersonating_ClearsImpersonationStateFields()
    {
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));
        await svc.LoginAsync("admin@test.com", "pass");

        apiMock.Setup(x => x.ImpersonateAsync(TargetUserId, default))
               .ReturnsAsync(TokenResponse(TargetUserId, "Member"));
        await svc.ImpersonateAsync(TargetUserId, "target@test.com");

        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ReturnsAsync(new MeResult(SuperAdminId, "admin@test.com", true));

        await svc.StopImpersonatingAsync();

        Assert.Null(store.OriginalAccessToken);
        Assert.Null(store.OriginalRefreshToken);
        Assert.Null(store.OriginalUserId);
        Assert.Null(store.OriginalUserEmail);
    }

    [Fact]
    public async Task StopImpersonating_WhenMeCallFails_LeavesIsSuperAdminFalse()
    {
        // Regression guard for the real production bug: if /api/me can't be reached,
        // IsSuperAdmin must not silently come back true from some other stale source.
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));
        await svc.LoginAsync("admin@test.com", "pass");

        apiMock.Setup(x => x.ImpersonateAsync(TargetUserId, default))
               .ReturnsAsync(TokenResponse(TargetUserId, "Member"));
        await svc.ImpersonateAsync(TargetUserId, "target@test.com");

        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ThrowsAsync(new HttpRequestException("network error"));

        await svc.StopImpersonatingAsync();

        Assert.False(store.IsSuperAdmin);
        Assert.False(store.IsImpersonating);
    }

    // ── ImpersonateAsync failure ──────────────────────────────────────────────

    [Fact]
    public async Task ImpersonateAsync_WhenApiFails_ReturnsFalseAndPreservesState()
    {
        var (svc, store, idMock, apiMock) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin"));
        await svc.LoginAsync("admin@test.com", "pass");
        var originalToken = store.AccessToken;

        apiMock.Setup(x => x.ImpersonateAsync(TargetUserId, default))
               .ReturnsAsync((WebApiTokenResponse?)null);

        var ok = await svc.ImpersonateAsync(TargetUserId, "target@test.com");

        Assert.False(ok);
        Assert.Equal(originalToken, store.AccessToken);
        Assert.False(store.IsImpersonating);
    }

    // ── RefreshIfNeededAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RefreshIfNeededAsync_WhenTokenStillValid_ReturnsTrueWithoutCallingApi()
    {
        var (svc, store, idMock, _) = Build();
        store.AccessToken             = "still-valid";
        store.RefreshToken            = "refresh";
        store.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        var result = await svc.RefreshIfNeededAsync();

        Assert.True(result);
        idMock.Verify(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_WhenTokenExpired_RefreshesAndUpdatesStore()
    {
        var (svc, store, idMock, _) = Build();
        store.AccessToken             = "expired";
        store.RefreshToken            = "old-refresh";
        store.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        idMock.Setup(x => x.RefreshAsync("old-refresh", default))
              .ReturnsAsync(TokenResponse(SuperAdminId, "SuperAdmin", "new-refresh"));

        var result = await svc.RefreshIfNeededAsync();

        Assert.True(result);
        Assert.NotEqual("expired", store.AccessToken);
        Assert.Equal("new-refresh", store.RefreshToken);
        Assert.Equal(SuperAdminId, store.UserId);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_WhenNoRefreshToken_ReturnsFalse()
    {
        var (svc, store, _, _) = Build();
        store.AccessToken             = "expired";
        store.RefreshToken            = null;
        store.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = await svc.RefreshIfNeededAsync();

        Assert.False(result);
    }
}
