namespace Ben.Desktop.App.Library.External;

/// <summary>
/// Runs Apple's own sign-in sheet and returns what it produced.
/// </summary>
/// <remarks>
/// Only Apple platforms have one. Everywhere else the implementation reports that it is
/// unavailable, and the button is not offered — which is the correct outcome rather than a
/// failure, and is why this answers a value instead of throwing.
/// </remarks>
public interface IAppleIdentityProvider
{
    /// <summary>Whether this platform can show Apple's sheet at all.</summary>
    bool IsAvailable { get; }

    Task<AppleIdentity?> RequestAsync(CancellationToken token = default);
}

/// <summary>What Apple's sheet produced.</summary>
/// <param name="IdentityToken">The signed JWT to present to our API.</param>
/// <param name="DisplayName">
/// The person's name, on a FIRST authorization only.
/// </param>
/// <remarks>
/// Apple gives the name exactly once, on the very first authorization for an app, and never again
/// — not on a later sign-in, and not after the app is deleted and reinstalled. A name that arrives
/// here has to be used now or the account ends up called whatever the client invented.
/// </remarks>
public sealed record AppleIdentity(string IdentityToken, string? DisplayName);

/// <summary>The stand-in for platforms with no Apple sign-in.</summary>
public sealed class UnavailableAppleIdentityProvider : IAppleIdentityProvider
{
    public bool IsAvailable => false;

    public Task<AppleIdentity?> RequestAsync(CancellationToken token = default)
        => Task.FromResult<AppleIdentity?>(null);
}
