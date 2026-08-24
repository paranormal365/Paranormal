using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Microsoft.AspNetCore.Components;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Covers the refresh policy rather than the fetch itself — when the service is allowed to skip a
/// call, when it must not, and what the badges show across a sign-in change. Those rules are the
/// only real logic here, and getting them wrong is invisible in the UI until it isn't.
/// </summary>
public class NotificationStateTests
{
    // ── Test doubles ─────────────────────────────────────────────────────────

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://localhost/", "https://localhost/");
        public void Navigate(string relativePath)
        {
            Uri = ToAbsoluteUri(relativePath).ToString();
            NotifyLocationChanged(isInterceptedLink: false);
        }
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class FakeUserState : IBenUserState
    {
        public bool IsAuthenticated { get; set; } = true;
        public bool IsSuperAdmin => false;
        public bool IsModerator => false;
        public bool IsAdmin => false;
        public bool IsImpersonating => false;
        public string? UserEmail => "test@benco.dev";
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public Task AuthReady => Task.CompletedTask;
        public TimeZoneInfo BrowserTimeZone => TimeZoneInfo.Utc;

        public event Action? StateChanged;
        public void RaiseStateChanged() => StateChanged?.Invoke();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static NotificationSummaryResponse SummaryWith(int orgMessages) =>
        new(new NotificationBucket(orgMessages, DateTime.UtcNow),
            NotificationBucket.Empty, NotificationBucket.Empty,
            NotificationBucket.Empty, NotificationBucket.Empty,
            NotificationBucket.Empty, NotificationBucket.Empty,
            NotificationBucket.Empty);

    private static (NotificationState State, Mock<IBenAdminClient> Client, FakeUserState User, TestNavigationManager Nav)
        Build(NotificationSummaryResponse? returns = null)
    {
        var client = new Mock<IBenAdminClient>(MockBehavior.Loose);
        client.Setup(c => c.GetNotificationSummaryAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(returns ?? SummaryWith(3));

        var user = new FakeUserState();
        var nav  = new TestNavigationManager();
        return (new NotificationState(client.Object, user, nav), client, user, nav);
    }

    private static void VerifyFetchCount(Mock<IBenAdminClient> client, int times) =>
        client.Verify(c => c.GetNotificationSummaryAsync(It.IsAny<CancellationToken>()), Times.Exactly(times));

    // ── Startup ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureStarted_DuringPrerender_DoesNotFetch()
    {
        var (state, client, _, _) = Build();

        await state.EnsureStartedAsync(isInteractive: false);

        VerifyFetchCount(client, 0);
        Assert.False(state.HasLoaded);
        Assert.Equal(0, state.Current.TotalCount);
    }

    [Fact]
    public async Task EnsureStarted_WhenInteractive_FetchesOnceAndPublishes()
    {
        var (state, client, _, _) = Build(SummaryWith(3));
        var changedCount = 0;
        state.Changed += () => changedCount++;

        await state.EnsureStartedAsync(isInteractive: true);

        VerifyFetchCount(client, 1);
        Assert.True(state.HasLoaded);
        Assert.Equal(3, state.Current.TotalCount);
        Assert.Equal(1, changedCount);

        state.Dispose();
    }

    [Fact]
    public async Task EnsureStarted_CalledByEveryConsumer_StillFetchesOnce()
    {
        // The bell and the drawer both call this on first render and must not coordinate.
        var (state, client, _, _) = Build();

        await Task.WhenAll(
            state.EnsureStartedAsync(isInteractive: true),
            state.EnsureStartedAsync(isInteractive: true),
            state.EnsureStartedAsync(isInteractive: true));

        VerifyFetchCount(client, 1);
        state.Dispose();
    }

    // ── Refresh policy ───────────────────────────────────────────────────────

    [Fact]
    public async Task Navigation_ShortlyAfterAFetch_IsDebounced()
    {
        var (state, client, _, nav) = Build();
        await state.EnsureStartedAsync(isInteractive: true);

        nav.Navigate("/cases");
        nav.Navigate("/organizations");
        await state.RefreshAsync();          // the un-forced path, same as a navigation

        VerifyFetchCount(client, 1);
        state.Dispose();
    }

    [Fact]
    public async Task Refresh_WithForce_IgnoresTheDebounce()
    {
        var (state, client, _, _) = Build();
        await state.EnsureStartedAsync(isInteractive: true);

        await state.RefreshAsync(force: true);

        VerifyFetchCount(client, 2);
        state.Dispose();
    }

    [Fact]
    public async Task Refresh_WhenSignedOut_ClearsWithoutCallingTheApi()
    {
        var (state, client, user, _) = Build();
        await state.EnsureStartedAsync(isInteractive: true);
        Assert.Equal(3, state.Current.TotalCount);

        user.IsAuthenticated = false;
        await state.RefreshAsync(force: true);

        VerifyFetchCount(client, 1);                     // no second call
        Assert.Equal(0, state.Current.TotalCount);
        state.Dispose();
    }

    [Fact]
    public async Task Refresh_WhenTheApiThrows_KeepsTheLastKnownCounts()
    {
        // A badge is decoration: a transient API failure should not blank it or surface an error.
        var (state, client, _, _) = Build(SummaryWith(3));
        await state.EnsureStartedAsync(isInteractive: true);

        client.Setup(c => c.GetNotificationSummaryAsync(It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("api down"));

        await state.RefreshAsync(force: true);

        Assert.Equal(3, state.Current.TotalCount);
        state.Dispose();
    }

    // ── Sign-in changes ──────────────────────────────────────────────────────

    [Fact]
    public async Task AuthStateChange_ClearsTheOldUsersCountsImmediately()
    {
        // The clear must be synchronous with the signal — not deferred to the refetch — so a
        // logout can never leave the previous user's unread count on screen.
        var (state, _, user, _) = Build();
        await state.EnsureStartedAsync(isInteractive: true);
        Assert.Equal(3, state.Current.TotalCount);

        user.IsAuthenticated = false;
        user.RaiseStateChanged();

        Assert.Equal(0, state.Current.TotalCount);
        Assert.False(state.HasLoaded);
        state.Dispose();
    }

    [Fact]
    public async Task AuthStateChange_RefetchesForTheNewUserIgnoringTheDebounce()
    {
        var (state, client, user, _) = Build();
        await state.EnsureStartedAsync(isInteractive: true);

        client.Setup(c => c.GetNotificationSummaryAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(SummaryWith(7));
        user.RaiseStateChanged();

        // The handler kicks off the refresh without awaiting it; give it a moment to land.
        for (var i = 0; i < 50 && state.Current.TotalCount != 7; i++) await Task.Delay(10);

        Assert.Equal(7, state.Current.TotalCount);
        state.Dispose();
    }

    // ── Teardown ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_StopsRespondingToNavigation()
    {
        var (state, client, _, nav) = Build();
        await state.EnsureStartedAsync(isInteractive: true);

        state.Dispose();
        nav.Navigate("/cases");
        await state.RefreshAsync(force: true);

        VerifyFetchCount(client, 1);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (state, _, _, _) = Build();
        state.Dispose();
        state.Dispose();
    }
}
