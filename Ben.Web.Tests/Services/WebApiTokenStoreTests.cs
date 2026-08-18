using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

public class WebApiTokenStoreTests
{
    [Fact]
    public void IsAuthenticated_WhenAccessTokenSet_ReturnsTrue()
    {
        IWebApiTokenStore store = new WebApiTokenStore { AccessToken = "some-token" };
        Assert.True(store.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_WhenAccessTokenNull_ReturnsFalse()
    {
        IWebApiTokenStore store = new WebApiTokenStore { AccessToken = null };
        Assert.False(store.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_WhenAccessTokenEmpty_ReturnsFalse()
    {
        IWebApiTokenStore store = new WebApiTokenStore { AccessToken = "" };
        Assert.False(store.IsAuthenticated);
    }

    [Fact]
    public void NewStore_AllAuthFieldsAreNull()
    {
        var store = new WebApiTokenStore();
        Assert.Null(store.AccessToken);
        Assert.Null(store.RefreshToken);
        Assert.Null(store.UserEmail);
        Assert.Null(store.UserId);
        Assert.Null(store.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public void NewStore_IsSuperAdmin_DefaultsFalse()
    {
        var store = new WebApiTokenStore();
        Assert.False(store.IsSuperAdmin);
    }

    [Fact]
    public void NewStore_IsImpersonating_DefaultsFalse()
    {
        var store = new WebApiTokenStore();
        Assert.False(store.IsImpersonating);
    }

    [Fact]
    public void NewStore_OriginalImpersonationFields_AllNull()
    {
        var store = new WebApiTokenStore();
        Assert.Null(store.OriginalAccessToken);
        Assert.Null(store.OriginalRefreshToken);
        Assert.Null(store.OriginalUserId);
        Assert.Null(store.OriginalUserEmail);
    }

    [Fact]
    public void ImpersonationFields_CanBeSetAndRead()
    {
        var id    = Guid.NewGuid();
        var store = new WebApiTokenStore
        {
            IsImpersonating       = true,
            OriginalAccessToken   = "orig-access",
            OriginalRefreshToken  = "orig-refresh",
            OriginalUserId        = id,
            OriginalUserEmail     = "admin@test.com"
        };

        Assert.True(store.IsImpersonating);
        Assert.Equal("orig-access",   store.OriginalAccessToken);
        Assert.Equal("orig-refresh",  store.OriginalRefreshToken);
        Assert.Equal(id,              store.OriginalUserId);
        Assert.Equal("admin@test.com",store.OriginalUserEmail);
    }

    // ── IsEntraSession ────────────────────────────────────────────────────────

    [Fact]
    public void NewStore_IsEntraSession_DefaultsFalse()
    {
        var store = new WebApiTokenStore();
        Assert.False(store.IsEntraSession);
    }

    [Fact]
    public void IsEntraSession_CanBeSetAndRead()
    {
        var store = new WebApiTokenStore { IsEntraSession = true };
        Assert.True(store.IsEntraSession);

        store.IsEntraSession = false;
        Assert.False(store.IsEntraSession);
    }

    [Fact]
    public void IsEntraSession_IndependentOfIsAuthenticated()
    {
        // IsEntraSession only describes HOW the user authenticated, not WHETHER they are.
        IWebApiTokenStore entra = new WebApiTokenStore { AccessToken = "entra-token", IsEntraSession = true };
        Assert.True(entra.IsAuthenticated);
        Assert.True(entra.IsEntraSession);

        // Local session: authenticated but not via Entra
        IWebApiTokenStore local = new WebApiTokenStore { AccessToken = "local-token", IsEntraSession = false };
        Assert.True(local.IsAuthenticated);
        Assert.False(local.IsEntraSession);
    }
}
