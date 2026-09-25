using Ben.Service.Models.Store;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// The signed-in shopper's hearts (storefront S6.3): which products they have kept, for the header's
/// count, the cards' and product page's hearts, and the favourites page — one read per visit, kept
/// in step as hearts are pressed.
/// </summary>
/// <remarks>
/// <para>A guest has no favourites: nothing is read, and a heart sends them to sign in. Signing in or
/// out reads again. Only products on the store today are counted — a hidden product's heart is
/// kept on the account but not shown (the API decides; this only holds its answer).</para>
///
/// <para>Takes <see cref="IBenAdminClient"/>, not <see cref="IBenStoreClient"/> — the website registers
/// the one adapter under that name only (the S3 trap that stopped the site from starting).</para>
/// </remarks>
public sealed class StoreFavouriteState : IDisposable
{
    private readonly IBenAdminClient _client;
    private readonly IBenUserState _userState;
    private readonly HashSet<Guid> _ids = [];
    private bool _started, _disposed;

    public StoreFavouriteState(IBenAdminClient client, IBenUserState userState)
    {
        _client = client;
        _userState = userState;
    }

    public int Count => _ids.Count;

    public bool IsSignedIn => _userState.IsAuthenticated;

    /// <summary>
    /// The first read is done, so <see cref="IsSignedIn"/> and the hearts can be believed. Before
    /// then a signed-in person looks signed out, and a heart drawn as "sign in" is a tap that sends
    /// them to the login page (StoreFavouritesTests caught it).
    /// </summary>
    public bool IsReady { get; private set; }

    public bool IsFavourite(Guid productId) => _ids.Contains(productId);

    public event Action? Changed;

    /// <summary>Reads the hearts the first time anything asks; nothing during the server's prerender.</summary>
    public async Task EnsureStartedAsync(bool isInteractive)
    {
        if (_started || _disposed) return;
        if (!await _userState.WaitUntilAuthReadyAsync(isInteractive)) return;
        if (_started || _disposed) return;
        _started = true;
        _userState.StateChanged += OnSignInChanged;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _ids.Clear();
        if (_userState.IsAuthenticated)
        {
            var favourites = await _client.GetMyStoreFavouritesAsync();
            foreach (var card in favourites.Items) _ids.Add(card.Id);
        }
        IsReady = true;
        Changed?.Invoke();
    }

    /// <summary>Hearts or un-hearts a product. Answers the sentence to show when it was refused; null when it went through.</summary>
    public async Task<string?> ToggleAsync(Guid productId)
    {
        if (!_userState.IsAuthenticated) return "Sign in to keep favourites.";
        var adding = !_ids.Contains(productId);
        var (count, error) = adding ? await _client.AddStoreFavouriteAsync(productId) : await _client.RemoveStoreFavouriteAsync(productId);
        if (count is null) return error ?? "That couldn't be saved just now.";
        if (adding) _ids.Add(productId); else _ids.Remove(productId);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Lets a product go (the Favourites page's Remove). Answers the refusal, or null.</summary>
    public async Task<string?> RemoveAsync(Guid productId)
    {
        var (count, error) = await _client.RemoveStoreFavouriteAsync(productId);
        if (count is null) return error ?? "That couldn't be saved just now.";
        _ids.Remove(productId);
        Changed?.Invoke();
        return null;
    }

    private void OnSignInChanged() => _ = RefreshAsync();

    public void Dispose()
    {
        _disposed = true;
        if (_started) _userState.StateChanged -= OnSignInChanged;
    }
}
