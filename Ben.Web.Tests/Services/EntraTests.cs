using System.Text;
using Ben.Web.WebApp.Services;
using Ben.Web.WebApp.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for Microsoft Entra integration:
///   - EntraTokenHolder (captures OIDC token for Blazor circuit)
///   - WebApiAuthService — /api/me override of JWT-parsed claims after login
///   - StateChanged notification fires on auth state transitions
/// </summary>
public class EntraTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly Guid UserId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MeApiUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string MakeJwt(Guid userId, string role = "Member")
    {
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($$$"""{"sub":"{{{userId}}}","role":"{{{role}}}","exp":9999999999}"""));
        var header = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        return $"{header}.{payload}.sig";
    }

    private static (WebApiAuthService Svc, WebApiTokenStore Store,
        Mock<IWebApiIdentityClient> IdMock, Mock<IWebApiClient> ApiMock)
        Build()
    {
        var store  = new WebApiTokenStore();
        var idMock = new Mock<IWebApiIdentityClient>();
        var apiMock = new Mock<IWebApiClient>();
        var svc = new WebApiAuthService(idMock.Object, apiMock.Object, store);
        return (svc, store, idMock, apiMock);
    }

    private static WebApiTokenResponse TokenResponse(Guid userId, string role = "Member") =>
        new() { AccessToken = MakeJwt(userId, role), RefreshToken = "rt", ExpiresIn = 3600 };

    // ── EntraTokenHolder ──────────────────────────────────────────────────────

    [Fact]
    public void EntraTokenHolder_EntraOid_DefaultNull()
    {
        var holder = new EntraTokenHolder();
        Assert.Null(holder.EntraOid);
    }

    [Fact]
    public void EntraTokenHolder_EntraOid_CanBeSet()
    {
        var oid = Guid.NewGuid().ToString();
        var holder = new EntraTokenHolder { EntraOid = oid };
        Assert.Equal(oid, holder.EntraOid);
    }

    [Fact]
    public void EntraTokenHolder_Default_AllNullAndFalse()
    {
        var holder = new EntraTokenHolder();

        Assert.Null(holder.AccessToken);
        Assert.Null(holder.Email);
        Assert.False(holder.IsEntraAuthenticated);
    }

    [Fact]
    public void EntraTokenHolder_SetProperties_RetainsValues()
    {
        var holder = new EntraTokenHolder
        {
            AccessToken = "entra-token-abc",
            Email = "ben@example.com",
            IsEntraAuthenticated = true
        };

        Assert.Equal("entra-token-abc", holder.AccessToken);
        Assert.Equal("ben@example.com", holder.Email);
        Assert.True(holder.IsEntraAuthenticated);
    }

    [Fact]
    public void EntraTokenHolder_CanBeResetToFalse()
    {
        var holder = new EntraTokenHolder
        {
            AccessToken = "token",
            IsEntraAuthenticated = true
        };

        holder.AccessToken = null;
        holder.IsEntraAuthenticated = false;

        Assert.False(holder.IsEntraAuthenticated);
        Assert.Null(holder.AccessToken);
    }

    // ── WebApiAuthService — /api/me overrides JWT-parsed claims ───────────────

    [Fact]
    public async Task LoginAsync_WhenMeApiReturnsData_OverridesJwtParsedUserId()
    {
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync("u@t.com", "pw", default))
              .ReturnsAsync(TokenResponse(UserId1));

        // /api/me returns a DIFFERENT userId (simulating opaque token where sub ≠ local user id)
        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ReturnsAsync(new MeResult(MeApiUserId, "u@t.com", false, false));

        await svc.LoginAsync("u@t.com", "pw");

        Assert.Equal(MeApiUserId, store.UserId);
    }

    [Fact]
    public async Task LoginAsync_WhenMeApiReturnsSuperAdmin_SetsSuperAdminTrue()
    {
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1, "Member")); // JWT says Member

        // /api/me says SuperAdmin (server-side role check is authoritative for opaque tokens)
        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ReturnsAsync(new MeResult(MeApiUserId, "u@t.com", IsSuperAdmin: true, IsAdmin: false));

        await svc.LoginAsync("u@t.com", "pw");

        Assert.True(store.IsSuperAdmin);
    }

    [Fact]
    public async Task LoginAsync_WhenMeApiReturnsNull_KeepsJwtParsedClaims()
    {
        // When /api/me returns null (e.g. WebApi unreachable), JwtClaimsParser values remain.
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1, "SuperAdmin"));

        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ReturnsAsync((MeResult?)null);

        await svc.LoginAsync("u@t.com", "pw");

        // JWT-parsed values survive
        Assert.Equal(UserId1, store.UserId);
        Assert.True(store.IsSuperAdmin);
    }

    [Fact]
    public async Task LoginAsync_WhenMeApiThrows_KeepsJwtParsedClaims()
    {
        // Exceptions from /api/me are swallowed (non-fatal).
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1, "SuperAdmin"));

        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ThrowsAsync(new HttpRequestException("WebApi unreachable"));

        await svc.LoginAsync("u@t.com", "pw"); // must not throw

        Assert.Equal(UserId1, store.UserId);
        Assert.True(store.IsSuperAdmin);
    }

    [Fact]
    public async Task LoginAsync_AlwaysCallsMeApi_EvenForNonSuperAdminUsers()
    {
        var (svc, store, idMock, apiMock) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1, "Member"));

        apiMock.Setup(x => x.GetAsync<MeResult>("/api/me", default))
               .ReturnsAsync(new MeResult(UserId1, "u@t.com", false, false));

        await svc.LoginAsync("u@t.com", "pw");

        apiMock.Verify(x => x.GetAsync<MeResult>("/api/me", default), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ResetsIsEntraSession_WhenPreviouslyTrue()
    {
        // If the user was previously in an Entra session and then logs in locally,
        // the Entra session flag must be cleared so the local token gets persisted.
        var (svc, store, idMock, _) = Build();
        store.IsEntraSession = true;  // simulate prior Entra session

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1));

        await svc.LoginAsync("u@t.com", "pw");

        Assert.False(store.IsEntraSession);
    }

    [Fact]
    public async Task LoginAsync_IsEntraSession_RemainsDefaultFalseForNewSession()
    {
        var (svc, store, idMock, _) = Build();

        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1));

        await svc.LoginAsync("u@t.com", "pw");

        Assert.False(store.IsEntraSession);
    }

    // ── StateChanged notification ──────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_FiresStateChanged()
    {
        var (svc, store, idMock, _) = Build();
        idMock.Setup(x => x.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), default))
              .ReturnsAsync(TokenResponse(UserId1));

        int fireCount = 0;
        store.StateChanged += () => fireCount++;

        await svc.LoginAsync("u@t.com", "pw");

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Logout_FiresStateChanged()
    {
        var (svc, store, _, _) = Build();
        store.AccessToken = "token";

        int fireCount = 0;
        store.StateChanged += () => fireCount++;

        svc.Logout();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task ImpersonateAsync_FiresStateChanged()
    {
        var (svc, store, _, apiMock) = Build();
        store.AccessToken = "original-token";

        apiMock.Setup(x => x.ImpersonateAsync(UserId1, default))
               .ReturnsAsync(new WebApiTokenResponse
               {
                   AccessToken = MakeJwt(UserId1),
                   RefreshToken = "rt2",
                   ExpiresIn = 3600
               });

        int fireCount = 0;
        store.StateChanged += () => fireCount++;

        await svc.ImpersonateAsync(UserId1, "target@test.com");

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task StopImpersonating_WhenImpersonating_FiresStateChanged()
    {
        var (svc, store, _, _) = Build();
        store.AccessToken = "original";
        store.IsImpersonating = true;
        store.OriginalAccessToken = MakeJwt(UserId1, "SuperAdmin");

        int fireCount = 0;
        store.StateChanged += () => fireCount++;

        await svc.StopImpersonatingAsync();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task StopImpersonating_WhenNotImpersonating_DoesNotFireStateChanged()
    {
        var (svc, store, _, _) = Build();
        store.IsImpersonating = false;

        int fireCount = 0;
        store.StateChanged += () => fireCount++;

        await svc.StopImpersonatingAsync();

        Assert.Equal(0, fireCount);
    }

    // ── NotifyStateChanged ────────────────────────────────────────────────────

    [Fact]
    public void NotifyStateChanged_MultipleSubscribers_AllFire()
    {
        var store = new WebApiTokenStore();
        int count = 0;
        store.StateChanged += () => count++;
        store.StateChanged += () => count++;
        store.StateChanged += () => count++;

        store.NotifyStateChanged();

        Assert.Equal(3, count);
    }

    [Fact]
    public void NotifyStateChanged_NoSubscribers_DoesNotThrow()
    {
        var store = new WebApiTokenStore();
        var ex = Record.Exception(() => store.NotifyStateChanged());
        Assert.Null(ex);
    }
}
