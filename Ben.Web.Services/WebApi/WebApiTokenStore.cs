using Ben.Data.Common.Constants;
using Ben.Data.Common.Helpers;
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

    /// <summary>Item 186 F5: may review reported posts and media awaiting screening.</summary>
    public bool IsModerator { get; set; }

    /// <summary>Store sellers (backlog 251): holds the Seller role.</summary>
    public bool IsSeller { get; set; }

    // Impersonation
    public bool IsImpersonating { get; set; }
    public string? OriginalAccessToken { get; set; }
    public string? OriginalRefreshToken { get; set; }
    public Guid? OriginalUserId { get; set; }
    public string? OriginalUserEmail { get; set; }
    public string? OriginalUserDisplayName { get; set; }

    public bool IsEntraSession { get; set; }

    /// <summary>The browser's own zone, once MainLayout has read it; null until then or if it could not be.</summary>
    public TimeZoneInfo? DetectedTimeZone { get; set; }

    /// <summary>
    /// The browser's zone, or America/Chicago when the browser has not said (Ben, 2026-09-28: "Use
    /// the Chicago/America as the default"). It was UTC, which no reader on this site keeps.
    /// </summary>
    public TimeZoneInfo BrowserTimeZone => DetectedTimeZone ?? HouseZone;

    /// <summary>The zone this person chose on their profile or at sign-up; null when they never chose.</summary>
    public string? SavedTimeZoneId
    {
        get => _savedTimeZoneId;
        set
        {
            _savedTimeZoneId = Zones.Normalize(value);
            _savedTimeZone = _savedTimeZoneId is { } id ? Zones.Find(id) : null;
        }
    }
    private string? _savedTimeZoneId;
    private TimeZoneInfo? _savedTimeZone;

    /// <summary>Their choice, else the browser's, else Chicago — see <see cref="IBenUserState.ViewerTimeZone"/>.</summary>
    public TimeZoneInfo ViewerTimeZone => _savedTimeZone ?? BrowserTimeZone;

    private static readonly TimeZoneInfo HouseZone = Zones.Find(HouseClock.ZoneId);

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
