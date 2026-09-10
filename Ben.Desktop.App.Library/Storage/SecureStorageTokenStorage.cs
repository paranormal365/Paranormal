using System.Text.Json;
using Ben.Data.WebApi.Client.Auth;

namespace Ben.Desktop.App.Library.Storage;

/// <summary>
/// Keeps the signed-in session in the operating system's own secret store: the Keychain on Mac
/// Catalyst, the credential locker on Windows.
/// </summary>
/// <remarks>
/// <para>A refresh token is long-lived and there is no endpoint on the server that can revoke one,
/// so anywhere readable is the wrong home for it: a copied file is a session that cannot be taken
/// back. This is the same decision the iOS app made with its Keychain item.</para>
///
/// <para>Every operation swallows the platform's own failures and answers as though nothing was
/// stored. A secret store can genuinely be unavailable — a locked keychain, a policy, a machine
/// that has changed underneath the app — and the honest response to "I cannot read your session"
/// is to ask somebody to sign in again, not to show them a crash on launch.</para>
/// </remarks>
public sealed class SecureStorageTokenStorage : ITokenStorage
{
    /// <summary>
    /// One entry holding the whole session, not one per field.
    /// </summary>
    /// <remarks>
    /// Two entries can disagree: a save that half-succeeds leaves an access token from one session
    /// beside a refresh token from another, and the failure surfaces much later as a refusal nobody
    /// can explain.
    /// </remarks>
    internal const string Key = "com.ishaunted.desktop.tokens";

    private readonly ISecureStore _store;

    public SecureStorageTokenStorage(ISecureStore? store = null)
        => _store = store ?? new PlatformSecureStore();

    public async Task<StoredTokens?> LoadAsync(CancellationToken token = default)
    {
        try
        {
            var json = await _store.GetAsync(Key);
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<StoredTokens>(json);
        }
        catch (JsonException)
        {
            // Written by an older shape of this record. Not recoverable and not worth reporting:
            // signing in again fixes it.
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SaveAsync(StoredTokens tokens, CancellationToken token = default)
    {
        try
        {
            await _store.SetAsync(Key, JsonSerializer.Serialize(tokens));
        }
        catch (Exception)
        {
            // The session still works for as long as this process lives; it simply will not
            // survive a restart. Failing the sign-in over that would be worse.
        }
    }

    public Task ClearAsync(CancellationToken token = default)
    {
        try { _store.Remove(Key); }
        catch (Exception) { /* already gone, or unreachable. Either way there is nothing to keep. */ }

        return Task.CompletedTask;
    }
}

/// <summary>The platform secret store, behind an interface so the storage above can be tested.</summary>
public interface ISecureStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    void Remove(string key);
}

/// <summary>MAUI's own <c>SecureStorage</c>.</summary>
public sealed class PlatformSecureStore : ISecureStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);
    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
