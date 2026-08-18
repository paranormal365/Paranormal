using Ben.Data.Common.Enums;
using Ben.Web.Services;

namespace Ben.Web.Library.Help;

/// <summary>
/// Works out how much of the help a given reader may see.
/// </summary>
/// <remarks>
/// <para>Circuit-scoped and cached for the session: the answer costs a round trip and only changes
/// when the reader's memberships or roles do, which cannot happen mid-session without a reload.</para>
///
/// <para>Failures resolve <i>downward</i>. If the call throws, a signed-in reader is treated as
/// signed-in-only rather than being handed the administration documents on the strength of an
/// error. Help is not a security boundary the way case data is, but a rule that fails open is a
/// rule nobody can rely on.</para>
/// </remarks>
public sealed class HelpViewerResolver : IDisposable
{
    private readonly IBenUserState _userState;
    private readonly IBenAdminClient _client;

    private HelpViewer? _cached;

    public HelpViewerResolver(IBenUserState userState, IBenAdminClient client)
    {
        _userState = userState;
        _client = client;

        // The circuit outlives the session: signing in, signing out, and impersonation all change
        // the answer without a page load, so the cache has to go when the session does.
        _userState.StateChanged += Invalidate;
    }

    /// <summary>The current reader's ceiling.</summary>
    public async Task<HelpViewer> ResolveAsync()
    {
        if (_cached is { } hit) return hit;

        // A fresh circuit always starts unauthenticated until MainLayout restores the persisted
        // session, so reading IsAuthenticated before this would show a signed-in administrator
        // the public-only index on every hard navigation to /help.
        await _userState.AuthReady;

        // An anonymous reader needs no round trip — and must not make one, since the endpoint
        // requires auth and a 401 here would be noise, not information.
        if (!_userState.IsAuthenticated)
            return (_cached = HelpViewer.Anonymous).Value;

        try
        {
            var audience = await _client.GetMyHelpAudienceAsync();
            _cached = new HelpViewer(audience ?? HelpAudience.SignedIn);
        }
        catch
        {
            _cached = new HelpViewer(HelpAudience.SignedIn);
        }

        return _cached.Value;
    }

    /// <summary>Drops the cache — call after something changes the reader's roles or memberships.</summary>
    public void Invalidate() => _cached = null;

    public void Dispose() => _userState.StateChanged -= Invalidate;
}
