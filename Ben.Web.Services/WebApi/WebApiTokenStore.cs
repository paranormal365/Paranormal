using Ben.Web.Services;

namespace Ben.Web.Services.WebApi;

public sealed class WebApiTokenStore : IWebApiTokenStore, IBenUserState
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    public string? UserEmail { get; set; }
    public string? UserDisplayName { get; set; }
    public Guid? UserId { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool IsAdmin { get; set; }

    // Impersonation
    public bool IsImpersonating { get; set; }
    public string? OriginalAccessToken { get; set; }
    public string? OriginalRefreshToken { get; set; }
    public Guid? OriginalUserId { get; set; }
    public string? OriginalUserEmail { get; set; }

    public bool IsEntraSession { get; set; }

    public TimeZoneInfo BrowserTimeZone { get; set; } = TimeZoneInfo.Utc;

    // IBenUserState (computed)
    bool IBenUserState.IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    // State change notification
    public event Action? StateChanged;
    public void NotifyStateChanged() => StateChanged?.Invoke();

    // Auth-ready gate — see IWebApiTokenStore.AuthReady / IBenUserState.AuthReady for why this exists.
    private readonly TaskCompletionSource _authReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task AuthReady => _authReadyTcs.Task;
    public void SignalAuthReady() => _authReadyTcs.TrySetResult();
}
