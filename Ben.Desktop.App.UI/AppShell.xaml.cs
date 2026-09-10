using Ben.Desktop.App.Library.ViewModels;

namespace Ben.Desktop.App.UI;

/// <summary>
/// Shows the sign-in page or the signed-in one, and swaps between them when the session changes.
/// </summary>
/// <remarks>
/// The swap is driven by <see cref="SessionViewModel"/> rather than by whoever pressed a button,
/// so a session ending on its own — a restarted API, an expired token — lands somebody back on the
/// sign-in page without any screen having to know that could happen.
/// </remarks>
public partial class AppShell : Shell
{
    private readonly SessionViewModel _session;
    private readonly Pages.SignInPage _signIn;
    private readonly Pages.HomePage _home;
    private readonly Pages.CompleteProfilePage _completeProfile;

    public AppShell(
        SessionViewModel session,
        Pages.SignInPage signIn,
        Pages.HomePage home,
        Pages.CompleteProfilePage completeProfile)
    {
        InitializeComponent();
        _session = session;
        _signIn = signIn;
        _home = home;
        _completeProfile = completeProfile;

        _session.PropertyChanged += (_, _) => Show();
        Show();
    }

    private void Show()
    {
        // Three states, not two. A provider can vouch for somebody who has no account here, and
        // that is neither signed in nor signed out — showing them the app would mean every page
        // refusing them, and showing them the sign-in form would lose the identity they just proved.
        var wanted =
            _session.IsSignedIn ? (Page)_home
          : _session.NeedsLocalAccount ? _completeProfile
          : _signIn;

        if (!ReferenceEquals(Root.Content, wanted))
            Root.Content = wanted;
    }
}
