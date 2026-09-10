using Ben.Desktop.App.Library.ViewModels;

namespace Ben.Desktop.App.UI.Pages;

/// <summary>Creating or attaching an account for somebody a provider has already vouched for.</summary>
public partial class CompleteProfilePage : ContentPage
{
    private readonly ExternalSignInViewModel _external;
    private readonly SessionViewModel _session;

    public CompleteProfilePage(ExternalSignInViewModel external, SessionViewModel session)
    {
        InitializeComponent();
        _external = external;
        _session = session;
        _external.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        Busy.IsBusy = _external.IsBusy;
        Busy.Caption = "Setting up your account";

        Explanation.Text = _external.PendingProvider switch
        {
            ExternalProvider.Apple =>
                "Apple confirmed who you are. Choose how you'd like to appear here.",
            ExternalProvider.Microsoft =>
                "Microsoft confirmed who you are. There's no account here yet, so let's make one.",
            _ => string.Empty,
        };

        HandleSection.IsVisible = _external.NeedsHandle;

        // Always available. The server can only notice a collision when the addresses match, and
        // the cases that produce a duplicate account are precisely the ones where they do not.
        LinkSection.IsVisible = _external.CanLinkExistingAccount;

        LinkHeading.Text = _external.ShouldLinkInstead
            ? "That address already has an account"
            : "Already have an account here?";

        // The create form stays visible alongside the link one only when creating is still
        // possible. Once the server has said the address is taken, offering it again would invite
        // a second refusal.
        CreateSection.IsVisible = !_external.ShouldLinkInstead;

        if (DisplayNameField.Text != _external.DisplayName)
            DisplayNameField.Text = _external.DisplayName;

        if (HandleField.Text != _external.Handle)
            HandleField.Text = _external.Handle;

        CreateButton.IsEnabled = _external.CanSubmit;

        LinkTwoFactorSection.IsVisible = _external.LinkNeedsTwoFactor;
        LinkCodeLabel.Text = _external.UseRecoveryCode
            ? "Recovery code"
            : "Code from your authenticator app";
        LinkButton.Text = _external.LinkNeedsTwoFactor ? "Verify and link it" : "Link it";

        if (LinkCodeField.Text != _external.TwoFactorCode)
            LinkCodeField.Text = _external.TwoFactorCode;

        MessageText.Text = _external.Message;
        MessageText.IsVisible = !string.IsNullOrWhiteSpace(_external.Message);
    }

    private void OnDisplayNameChanged(object? sender, TextChangedEventArgs e)
        => _external.DisplayName = e.NewTextValue ?? string.Empty;

    private void OnHandleChanged(object? sender, TextChangedEventArgs e)
        => _external.Handle = e.NewTextValue ?? string.Empty;

    private async void OnCreate(object? sender, EventArgs e) => await _external.CreateAccountAsync();

    private void OnLinkCodeChanged(object? sender, TextChangedEventArgs e)
        => _external.TwoFactorCode = e.NewTextValue ?? string.Empty;

    private void OnRecoveryToggled(object? sender, CheckedChangedEventArgs e)
        => _external.UseRecoveryCode = e.Value;

    private async void OnLink(object? sender, EventArgs e)
        => await _external.LinkExistingAccountAsync(
            LinkEmailField.Text ?? string.Empty, LinkPasswordField.Text ?? string.Empty);

    /// <summary>
    /// Abandons the half-finished sign-in, including the session behind it.
    /// </summary>
    /// <remarks>
    /// Leaving the external token in place would keep somebody signed in to a provider with no
    /// account here — a state where the app looks signed in and every page refuses them.
    /// </remarks>
    private async void OnCancel(object? sender, EventArgs e)
    {
        _external.Clear();
        await _session.SignOutAsync();
    }
}
