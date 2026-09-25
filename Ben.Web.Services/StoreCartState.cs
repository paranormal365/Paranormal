using Ben.Service.Models.Store;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// The shopper's cart, one copy per circuit (storefront S3.4), so the header button, the drawer
/// and the cart page draw the same numbers and change together.
/// </summary>
/// <remarks>
/// <para><b>Scoped</b>, which in Blazor Server means one per circuit — the same lifetime as the
/// cart token it sends. Two tabs keep two copies, each re-read when it next changes.</para>
///
/// <para><b>Every change answers with the whole cart</b>, so after an add or a quantity change the
/// copy here is the server's, never a local guess. A refused change keeps the cart that was showing
/// and hands back the server's sentence for the surface to say.</para>
///
/// <para><b>Signing in re-reads the cart</b>: the server folds the browser's cart into the account's
/// on the first look after sign-in, and signing out goes back to the browser's (now empty) cart.</para>
///
/// <para><see cref="Changed"/> may be raised off the renderer's context; subscribers marshal back
/// with <c>InvokeAsync(StateHasChanged)</c>.</para>
/// </remarks>
public sealed class StoreCartState : IDisposable
{
    // IBenAdminClient, not IBenStoreClient: the website registers the one adapter under that name
    // only (as NotificationState takes it). Asking for the narrower interface stopped the site from
    // starting — found by the e2e harness, which is the only thing that builds the real container.
    private readonly IBenAdminClient _client;
    private readonly IBenUserState _userState;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private bool _disposed;

    public StoreCartState(IBenAdminClient client, IBenUserState userState)
    {
        _client = client;
        _userState = userState;
    }

    /// <summary>The cart as last read; null before the first read or after one failed.</summary>
    public StoreCartView? Cart { get; private set; }

    /// <summary>Units in the cart, for the badge.</summary>
    public int Count => Cart?.Count ?? 0;

    public bool Loading { get; private set; }

    /// <summary>Why the last read failed; null when it worked. Surfaces show it with a Retry.</summary>
    public string? LoadError { get; private set; }

    public bool DrawerOpen { get; private set; }

    public event Action? Changed;

    public event Action? DrawerChanged;

    /// <summary>
    /// Reads the cart the first time any surface asks. Does nothing during the server's prerender,
    /// where there is no circuit to keep a cart for.
    /// </summary>
    public async Task EnsureStartedAsync(bool isInteractive)
    {
        if (_started || _disposed) return;
        if (!await _userState.WaitUntilAuthReadyAsync(isInteractive)) return;
        if (_started || _disposed) return;
        _started = true;
        _userState.StateChanged += OnSignInChanged;
        await RefreshAsync();
    }

    /// <summary>
    /// Starts the cart if nothing has yet, else re-reads it — for the cart page, which must show
    /// stock and prices as they are now, not as the drawer last saw them.
    /// </summary>
    public async Task EnsureFreshAsync(bool isInteractive)
    {
        if (!_started) await EnsureStartedAsync(isInteractive);
        else await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        Loading = true;
        Changed?.Invoke();
        await _gate.WaitAsync();
        try
        {
            var result = await _client.GetCartAsync();
            if (result.Item is { } cart)
            {
                Cart = cart;
                LoadError = null;
            }
            else
            {
                LoadError = result.Reason ?? "Your cart couldn't be loaded.";
            }
        }
        catch (HttpRequestException)
        {
            LoadError = "Your cart couldn't be loaded.";
        }
        finally
        {
            Loading = false;
            _gate.Release();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Adds to the cart. The answer's <see cref="StoreCartChange.Cart"/> is null when nothing
    /// changed (refused); its <see cref="StoreCartChange.Message"/> is the sentence to show — the
    /// refusal, or "Only 3 left." when less was added than asked — or null when it went as asked.
    /// </summary>
    public Task<StoreCartChange> AddAsync(Guid variantId, int quantity)
        => ChangeAsync(() => _client.AddToCartAsync(new AddToCartRequest(variantId, quantity)));

    public Task<StoreCartChange> SetQuantityAsync(Guid variantId, int quantity)
        => ChangeAsync(() => _client.SetCartQuantityAsync(variantId, quantity));

    public Task<StoreCartChange> RemoveAsync(Guid variantId)
        => ChangeAsync(() => _client.RemoveFromCartAsync(variantId));

    public Task<StoreCartChange> ApplyCouponAsync(string code)
        => ChangeAsync(() => _client.ApplyCartCouponAsync(code));

    public Task<StoreCartChange> RemoveCouponAsync()
        => ChangeAsync(() => _client.RemoveCartCouponAsync());

    public void OpenDrawer()
    {
        DrawerOpen = true;
        DrawerChanged?.Invoke();
    }

    public void CloseDrawer()
    {
        if (!DrawerOpen) return;
        DrawerOpen = false;
        DrawerChanged?.Invoke();
    }

    private async Task<StoreCartChange> ChangeAsync(Func<Task<StoreCartChange>> send)
    {
        if (_disposed) return new StoreCartChange(null, null);
        await _gate.WaitAsync();
        StoreCartChange change;
        try
        {
            change = await send();
            if (change.Cart is { } cart)
            {
                Cart = cart;
                LoadError = null;
            }
        }
        catch (HttpRequestException)
        {
            change = new StoreCartChange(null, StoreCartChange.CouldNotUpdate);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
        return change;
    }

    private void OnSignInChanged() => _ = RefreshAsync();

    public void Dispose()
    {
        _disposed = true;
        if (_started) _userState.StateChanged -= OnSignInChanged;
    }
}
