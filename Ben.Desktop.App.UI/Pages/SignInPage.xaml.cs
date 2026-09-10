using Ben.Data.WebApi.Client.Auth;
using Ben.Desktop.App.Library.ViewModels;

namespace Ben.Desktop.App.UI.Pages;

/// <summary>
/// Signing in, including the second factor.
/// </summary>
/// <remarks>
/// Every sentence this page shows comes from <see cref="SessionViewModel.Message"/>, which is
/// derived from what the server actually said. That matters more here than anywhere: Identity
/// answers a wrong password, a two-factor account, an unconfirmed address and a locked account
/// with the same 401, and three of those four are sent somewhere useless by "check your email and
/// password".
/// </remarks>
public partial class SignInPage : ContentPage
{
    private readonly SessionViewModel _session;

    public SignInPage(SessionViewModel session)
    {
        InitializeComponent();
        _session = session;
        _session.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        var busy = _session.IsBusy;
        Busy.IsBusy = busy;
        Busy.Caption = _session.State.Phase switch
        {
            SessionPhase.Authenticating => "Checking your details",
            SessionPhase.FetchingIdentity => "Signing you in",
            _ => null,
        };

        var challenge = _session.NeedsTwoFactor;
        CredentialsSection.IsVisible = !challenge;
        TwoFactorSection.IsVisible = challenge;

        SessionEndedBanner.IsVisible = _session.ShowSessionEndedBanner;

        // The two-factor prompt is instruction, not error, and the section above already carries
        // it. Repeating it in red would report a working password as a problem.
        var message = challenge ? null : _session.Message;
        MessageText.Text = message;
        MessageText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private async void OnSignIn(object? sender, EventArgs e)
    {
        _session.AcknowledgeSessionEnded();
        await _session.SignInAsync(EmailField.Text ?? string.Empty, PasswordField.Text ?? string.Empty);

        if (_session.IsSignedIn || _session.NeedsTwoFactor)
            PasswordField.Text = string.Empty;
    }

    private async void OnSubmitCode(object? sender, EventArgs e)
    {
        await _session.SubmitTwoFactorAsync(CodeField.Code, RecoveryToggle.IsChecked);
        CodeField.Code = string.Empty;
    }

    private void OnCancelTwoFactor(object? sender, EventArgs e)
    {
        _session.CancelTwoFactor();
        PasswordField.Text = string.Empty;
        CodeField.Code = string.Empty;
    }
}
