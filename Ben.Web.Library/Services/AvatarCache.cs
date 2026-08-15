using System.Collections.Concurrent;

namespace Ben.Web.Library.Services;

/// <summary>
/// Circuit-scoped store of already-fetched avatars, keyed by user id.
/// </summary>
/// <remarks>
/// <para>Avatars are fetched as bytes through the authenticated client and handed to the browser
/// as data URIs. A plain <c>&lt;img src="…/api/users/{id}/avatar"&gt;</c> cannot work: the browser
/// sends no bearer token with an image request, so the endpoint would see an anonymous caller and
/// refuse — the same trap that made the private photo render broken on the profile page.</para>
///
/// <para>Without a cache this would be one HTTP call per name per render, and a member list or
/// message thread re-renders constantly. Keyed by user rather than by photo because the caller
/// only knows the person; which photo they're allowed to see is the server's decision.</para>
///
/// <para>Scoped to the circuit, so it lives and dies with the signed-in session. That matters for
/// correctness as much as memory: the resolution depends on <i>who is asking</i>, so a cache that
/// outlived the session could hand one user a photo resolved for another.</para>
/// </remarks>
public sealed class AvatarCache
{
    private readonly IBenAdminClient _client;
    private readonly ConcurrentDictionary<Guid, Task<string?>> _entries = new();

    public AvatarCache(IBenAdminClient client) => _client = client;

    /// <summary>
    /// A data URI for this user's avatar, or null when they have none the viewer may see.
    /// Concurrent callers for the same user share one in-flight request.
    /// </summary>
    public Task<string?> GetAsync(Guid userId)
        => _entries.GetOrAdd(userId, FetchAsync);

    /// <summary>
    /// Drops a cached entry so the next request refetches — call after the signed-in user changes
    /// their own photo, or their avatar would stay stale for the rest of the session.
    /// </summary>
    public void Invalidate(Guid userId) => _entries.TryRemove(userId, out _);

    private async Task<string?> FetchAsync(Guid userId)
    {
        try
        {
            var avatar = await _client.GetUserAvatarAsync(userId);
            return avatar is { } a
                ? $"data:{a.ContentType};base64,{Convert.ToBase64String(a.Data)}"
                : null;
        }
        catch
        {
            // A missing picture is never worth breaking a page over — the caller falls back to
            // initials. Not cached as a permanent null: GetOrAdd stores this completed task, so
            // a transient failure sticks for the session; Invalidate is the way back.
            return null;
        }
    }
}
