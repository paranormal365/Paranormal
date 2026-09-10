#if MACCATALYST
using AuthenticationServices;
using Foundation;
using UIKit;

namespace Ben.Desktop.App.Library.External;

/// <summary>
/// Sign in with Apple on Mac Catalyst, through Apple's own sheet.
/// </summary>
/// <remarks>
/// <para>Needs the <c>com.apple.developer.applesignin</c> entitlement and an App ID that has the
/// capability turned on. Without them the sheet raises an error rather than appearing, so the
/// button should only be shown when <see cref="IsAvailable"/> says so.</para>
///
/// <para>The bundle id is also the audience our API checks the token against, so it has to appear
/// in the server's <c>Apple:ClientIds</c> or a perfectly good token is refused with a 401.</para>
/// </remarks>
public sealed class AppleIdentityProvider : NSObject,
    IAppleIdentityProvider,
    IASAuthorizationControllerDelegate,
    IASAuthorizationControllerPresentationContextProviding
{
    private TaskCompletionSource<AppleIdentity?>? _pending;

    /// <summary>Available from macOS 11 upward, which is below anything this app supports.</summary>
    public bool IsAvailable => true;

    public Task<AppleIdentity?> RequestAsync(CancellationToken token = default)
    {
        // One sheet at a time. A second request while one is open would leave the first awaiting
        // a result that never arrives.
        if (_pending is { Task.IsCompleted: false }) return _pending.Task;

        _pending = new TaskCompletionSource<AppleIdentity?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new ASAuthorizationAppleIdProvider();
        var request = provider.CreateRequest();

        // The name is only ever offered on a first authorization; asking costs nothing on later
        // ones. The email is requested because the account needs an address, though Apple may
        // answer with one of its private relay addresses.
        request.RequestedScopes = [ASAuthorizationScope.FullName, ASAuthorizationScope.Email];

        var controller = new ASAuthorizationController([request])
        {
            Delegate = this,
            PresentationContextProvider = this,
        };

        token.Register(() => _pending?.TrySetResult(null));
        controller.PerformRequests();

        return _pending.Task;
    }

    [Export("authorizationController:didCompleteWithAuthorization:")]
    public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
    {
        if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is not { } credential
            || credential.IdentityToken is null)
        {
            _pending?.TrySetResult(null);
            return;
        }

        var identityToken = NSString.FromData(credential.IdentityToken, NSStringEncoding.UTF8)?.ToString();
        if (string.IsNullOrWhiteSpace(identityToken))
        {
            _pending?.TrySetResult(null);
            return;
        }

        // Present only on a first authorization. Joining the parts rather than taking one: a
        // person with only a family name recorded would otherwise come out nameless.
        var name = credential.FullName is { } full
            ? string.Join(" ", new[] { full.GivenName, full.FamilyName }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim()
            : null;

        _pending?.TrySetResult(new AppleIdentity(
            identityToken, string.IsNullOrWhiteSpace(name) ? null : name));
    }

    [Export("authorizationController:didCompleteWithError:")]
    public void DidComplete(ASAuthorizationController controller, NSError error)
    {
        // Cancelling is by far the most common "error" here, and it is not one. Everything else is
        // reported the same way rather than guessed at: the caller shows a refusal either way, and
        // inventing a distinction from an opaque code would be a guess.
        _pending?.TrySetResult(null);
    }

    public UIWindow GetPresentationAnchor(ASAuthorizationController controller) =>
        UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow)
        ?? UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .First();
}
#endif
