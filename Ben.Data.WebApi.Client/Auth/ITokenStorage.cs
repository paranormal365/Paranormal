namespace Ben.Data.WebApi.Client.Auth;

/// <summary>
/// Where a client keeps its tokens between runs.
/// </summary>
/// <remarks>
/// <para>Async because the real implementations are: a desktop or mobile app stores these in the
/// operating system's own secret store, which is an out-of-process call on every platform.</para>
///
/// <para>Refresh tokens are long-lived and there is no endpoint on the server that can revoke
/// one, so an implementation that writes them anywhere readable hands over a session that cannot
/// be taken back. Plain files and unencrypted preferences are not acceptable homes for these.</para>
/// </remarks>
public interface ITokenStorage
{
    /// <summary>What was stored, or null when nothing is signed in.</summary>
    /// <remarks>
    /// Must answer null rather than throwing when the store is unreadable. A person whose keychain
    /// entry cannot be decrypted should be asked to sign in again, not shown a crash.
    /// </remarks>
    Task<StoredTokens?> LoadAsync(CancellationToken token = default);

    Task SaveAsync(StoredTokens tokens, CancellationToken token = default);

    /// <summary>Removes the stored session. Must succeed even when there was nothing to remove.</summary>
    Task ClearAsync(CancellationToken token = default);
}

/// <summary>Token storage that lasts as long as the process. For tests, and for previews.</summary>
public sealed class InMemoryTokenStorage : ITokenStorage
{
    private StoredTokens? _tokens;

    public InMemoryTokenStorage(StoredTokens? initial = null) => _tokens = initial;

    public Task<StoredTokens?> LoadAsync(CancellationToken token = default) => Task.FromResult(_tokens);

    public Task SaveAsync(StoredTokens tokens, CancellationToken token = default)
    {
        _tokens = tokens;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken token = default)
    {
        _tokens = null;
        return Task.CompletedTask;
    }
}
