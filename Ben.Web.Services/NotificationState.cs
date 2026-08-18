using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Ben.Web.Services;

/// <summary>
/// Keeps one copy of the caller's unread counts per circuit, so every badge on the page reads the
/// same numbers from memory instead of each fetching its own.
/// </summary>
/// <remarks>
/// <para>Registered scoped, which in Blazor Server means one instance per circuit — the same
/// lifetime as the token store whose session it mirrors. It is therefore <i>not</i> shared between
/// a user's browser tabs: two tabs poll independently.</para>
///
/// <para>Refreshes come from three places, in descending order of how much they matter:
/// sign-in state changing (a hard reset — the old numbers belong to a different user), navigation
/// (cheap and catches most staleness, but debounced so a click-heavy stretch doesn't hammer the
/// API), and a slow background poll that exists only so a badge eventually appears for a user
/// sitting on one page. Real-time push would replace the poll; at current user counts it isn't
/// worth a hub.</para>
///
/// <para><b><see cref="Changed"/> can be raised off the renderer's synchronization context</b>
/// (the poll ticks on a thread-pool thread). Component subscribers must marshal back with
/// <c>InvokeAsync(StateHasChanged)</c> rather than calling <c>StateHasChanged</c> directly.</para>
/// </remarks>
public sealed class NotificationState : IDisposable
{
    /// <summary>
    /// How long a navigation-triggered refresh will reuse the last result. Long enough that
    /// clicking through several pages costs one fetch, short enough that acting on a notification
    /// and navigating away updates the badge.
    /// </summary>
    private static readonly TimeSpan NavigationDebounce = TimeSpan.FromSeconds(10);

    /// <summary>Background poll interval — the only refresh a user who never navigates will get.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IBenAdminClient _client;
    private readonly IBenUserState _userState;
    private readonly NavigationManager _navManager;

    private readonly CancellationTokenSource _disposalCts = new();
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private DateTimeOffset _lastFetchUtc = DateTimeOffset.MinValue;
    private bool _started;
    private bool _disposed;

    public NotificationState(
        IBenAdminClient client, IBenUserState userState, NavigationManager navManager)
    {
        _client     = client;
        _userState  = userState;
        _navManager = navManager;
    }

    /// <summary>
    /// The most recent summary, or <see cref="NotificationSummaryResponse.Empty"/> before the first
    /// successful fetch. Never null, so badge markup needs no null handling — a failed fetch and a
    /// genuinely empty inbox both render as "nothing waiting", which is the right failure mode for
    /// a decoration.
    /// </summary>
    public NotificationSummaryResponse Current { get; private set; } = NotificationSummaryResponse.Empty;

    /// <summary>True once a fetch has completed, so callers can tell "no news" from "no data yet".</summary>
    public bool HasLoaded { get; private set; }

    /// <summary>Raised after <see cref="Current"/> changes. See the class remarks on threading.</summary>
    public event Action? Changed;

    /// <summary>
    /// Begins tracking for this circuit: first fetch, then navigation and poll refreshes. Safe to
    /// call from every consumer's first render — only the first call does anything, so the bell and
    /// the drawer don't need to coordinate over who owns startup.
    /// </summary>
    /// <param name="isInteractive">
    /// <c>RendererInfo.IsInteractive</c>. During static SSR prerender there is no auth state to
    /// wait on and no circuit to keep polling for, so this returns immediately.
    /// </param>
    public async Task EnsureStartedAsync(bool isInteractive)
    {
        if (_started || _disposed) return;
        if (!await _userState.WaitUntilAuthReadyAsync(isInteractive)) return;
        if (_started || _disposed) return;   // re-check: the await above yields
        _started = true;

        _userState.StateChanged     += OnAuthStateChanged;
        _navManager.LocationChanged += OnLocationChanged;

        await RefreshAsync(force: true);
        _ = PollLoopAsync();
    }

    /// <summary>
    /// Fetches a fresh summary. Without <paramref name="force"/> the call is skipped when the last
    /// one finished inside <see cref="NavigationDebounce"/>.
    /// </summary>
    public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        if (_disposed) return;

        if (!_userState.IsAuthenticated)
        {
            Publish(NotificationSummaryResponse.Empty);
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _lastFetchUtc < NavigationDebounce) return;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token, ct);

        // One fetch at a time: navigation and the poll can land together, and two in-flight calls
        // could otherwise resolve out of order and leave the older counts on screen.
        if (!await _fetchGate.WaitAsync(TimeSpan.Zero, linked.Token)) return;
        try
        {
            var summary = await _client.GetNotificationSummaryAsync(linked.Token);
            _lastFetchUtc = DateTimeOffset.UtcNow;
            if (summary is not null) Publish(summary);
            HasLoaded = true;
        }
        catch (OperationCanceledException) { /* circuit tearing down, or a superseded refresh */ }
        catch
        {
            // A badge is not worth surfacing an error for. Keep the last known counts and let the
            // next refresh correct them; only stamp the clock so a hard-down API isn't retried on
            // every single navigation.
            _lastFetchUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private void Publish(NotificationSummaryResponse summary)
    {
        Current = summary;
        Changed?.Invoke();
    }

    private void OnAuthStateChanged()
    {
        // The counts on screen belong to whoever was signed in a moment ago. Clear them
        // synchronously rather than waiting for the refetch, so a logout can't briefly leave the
        // previous user's unread count visible.
        Current   = NotificationSummaryResponse.Empty;
        HasLoaded = false;
        Changed?.Invoke();
        _ = RefreshAsync(force: true);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => _ = RefreshAsync();

    private async Task PollLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(_disposalCts.Token))
                await RefreshAsync(force: true, _disposalCts.Token);
        }
        catch (OperationCanceledException) { /* disposed */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _userState.StateChanged     -= OnAuthStateChanged;
        _navManager.LocationChanged -= OnLocationChanged;

        _disposalCts.Cancel();
        _disposalCts.Dispose();
        _fetchGate.Dispose();
    }
}
