using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ben.Data.WebApi.Client.Auth;
using Ben.Data.WebApi.Client.External;
using Ben.Desktop.App.Library.External;

namespace Ben.Desktop.App.Library.ViewModels;

/// <summary>
/// The two external doors, and the form that appears when a provider vouches for somebody this
/// site has never seen.
/// </summary>
/// <remarks>
/// Both providers lead to the same two questions — create an account, or attach one that already
/// exists — so they share one screen. What differs is only how the identity was proved, and that
/// is settled before this point.
/// </remarks>
public sealed class ExternalSignInViewModel : INotifyPropertyChanged
{
    private readonly EntraSignInService _entra;
    private readonly EntraAccountClient _entraAccounts;
    private readonly AppleSignInClient _apple;
    private readonly IAppleIdentityProvider _appleIdentity;
    private readonly SessionStore _store;

    public ExternalSignInViewModel(
        EntraSignInService entra,
        EntraAccountClient entraAccounts,
        AppleSignInClient apple,
        IAppleIdentityProvider appleIdentity,
        SessionStore store)
    {
        _entra = entra;
        _entraAccounts = entraAccounts;
        _apple = apple;
        _appleIdentity = appleIdentity;
        _store = store;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether to offer the Microsoft button. A door that cannot work is worse than none.</summary>
    public bool CanSignInWithMicrosoft => _entra.IsAvailable;

    /// <summary>Whether to offer the Apple button. False on every platform without Apple's sheet.</summary>
    public bool CanSignInWithApple => _appleIdentity.IsAvailable;

    public bool ShowExternalOptions => CanSignInWithMicrosoft || CanSignInWithApple;

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set { _busy = value; Raise(); Raise(nameof(CanSubmit)); }
    }

    private string? _message;

    /// <summary>What to tell them, or null when there is nothing to say.</summary>
    public string? Message
    {
        get => _message;
        private set { _message = value; Raise(); }
    }

    // ── Apple's one-shot name ─────────────────────────────────────────────────

    /// <summary>
    /// Held from Apple's answer so the second attempt can carry it.
    /// </summary>
    /// <remarks>
    /// Apple gives a person's name on the FIRST authorization only. If it is dropped between the
    /// needs-profile answer and the retry, it is gone for good — not recoverable by signing out,
    /// deleting the app, or asking again — and the account ends up named whatever was invented.
    /// </remarks>
    private string? _appleIdentityToken;
    private string? _appleSuggestedName;

    /// <summary>Which provider is waiting for a profile, when one is.</summary>
    public ExternalProvider PendingProvider { get; private set; } = ExternalProvider.None;

    public bool NeedsProfile => PendingProvider != ExternalProvider.None;

    /// <summary>Whether the account form should offer a handle box. Only Apple asks for one.</summary>
    public bool NeedsHandle => PendingProvider == ExternalProvider.Apple;

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; Raise(); Raise(nameof(CanSubmit)); }
    }

    private string _handle = string.Empty;
    public string Handle
    {
        get => _handle;
        set { _handle = value; Raise(); Raise(nameof(CanSubmit)); }
    }

    public bool CanSubmit =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(DisplayName)
        && (!NeedsHandle || !string.IsNullOrWhiteSpace(Handle));

    // ── Starting a sign-in ────────────────────────────────────────────────────

    public async Task SignInWithMicrosoftAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Message = null;
        try
        {
            var outcome = await _entra.SignInAsync();

            // Closing the window is a decision, not a failure. Saying anything about it would be
            // telling somebody off for changing their mind.
            if (outcome.Cancelled) return;

            if (!outcome.Succeeded)
            {
                Message = outcome.Reason;
                return;
            }

            if (_store.State.NeedsLocalAccount)
            {
                PendingProvider = ExternalProvider.Microsoft;
                DisplayName = _store.State.ExternalEmail ?? string.Empty;
                Raise(nameof(NeedsProfile));
                Raise(nameof(NeedsHandle));
                Raise(nameof(CanLinkExistingAccount));
                Message = "You're signed in with Microsoft, but there's no account here yet. Pick a name to finish setting one up.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SignInWithAppleAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Message = null;
        try
        {
            var identity = await _appleIdentity.RequestAsync();
            if (identity is null) return;   // cancelled, or Apple refused; either way, say nothing

            _appleIdentityToken = identity.IdentityToken;
            _appleSuggestedName ??= identity.DisplayName;

            await SubmitAppleAsync(identity.DisplayName, handle: null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Finishing an account ──────────────────────────────────────────────────

    /// <summary>Creates the account the pending provider is waiting for.</summary>
    public async Task CreateAccountAsync()
    {
        if (!CanSubmit) return;

        IsBusy = true;
        Message = null;
        try
        {
            if (PendingProvider == ExternalProvider.Apple)
            {
                await SubmitAppleAsync(DisplayName, Handle);
                return;
            }

            var result = await _entraAccounts.RegisterAsync(DisplayName);
            if (!result.Succeeded)
            {
                Message = result.Reason;
                ShouldLinkInstead = result.ShouldLinkInstead;
                Raise(nameof(ShouldLinkInstead));
                return;
            }

            // The server's answer to "who is this" changes only after the account exists, so
            // asking again is what moves them off this screen.
            await _store.ResolveIdentityAsync();
            Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The server has told us the address already has an account, so creating a second one is not
    /// the answer and the create form is put away.
    /// </summary>
    /// <remarks>
    /// Only ever a HINT to hide the wrong form. It is NOT what decides whether linking is offered —
    /// see <see cref="CanLinkExistingAccount"/>.
    /// </remarks>
    public bool ShouldLinkInstead { get; private set; }

    /// <summary>
    /// Whether to offer "I already have an account". True for the whole of this screen, always.
    /// </summary>
    /// <remarks>
    /// <para>This used to appear only after the server noticed a collision, which meant it only
    /// ever appeared when the provider's address happened to equal an existing one. That is the
    /// easy case and not the common one: a Hide My Email relay address matches nothing here, and
    /// plenty of people have a work Microsoft account at one address and an account here at
    /// another. Every one of those was quietly given a SECOND account holding none of their cases,
    /// groups or history, with nothing offering to join them up.</para>
    ///
    /// <para>The server cannot detect those. The person can — they know whether they have an
    /// account — so the door has to be open for them to say so.</para>
    /// </remarks>
    public bool CanLinkExistingAccount => NeedsProfile;

    /// <summary>
    /// Attaches the pending external identity to an account that already exists here, whatever
    /// address it is under.
    /// </summary>
    /// <remarks>
    /// The password is what proves that account is theirs; holding a provider token proves only
    /// the provider identity, and the server refuses without both.
    /// </remarks>
    public async Task LinkExistingAccountAsync(string email, string password)
    {
        if (IsBusy || !NeedsProfile) return;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Message = "Enter the email address and password of the account you already have.";
            return;
        }

        IsBusy = true;
        Message = null;
        try
        {
            if (PendingProvider == ExternalProvider.Apple)
            {
                if (_appleIdentityToken is null)
                {
                    Message = "That sign-in expired. Start again with Apple.";
                    return;
                }

                // Apple's link answers with a full session, because an Apple identity token is not
                // a credential the API accepts on ordinary requests — there is nothing to fall
                // back on the way the Microsoft path has.
                var apple = await _apple.LinkAsync(_appleIdentityToken, email, password);
                if (!apple.Succeeded)
                {
                    Message = apple.Reason;
                    return;
                }

                Clear();
                return;
            }

            var result = await _entraAccounts.LinkAsync(email, password);
            if (!result.Succeeded)
            {
                Message = result.Reason;
                return;
            }

            // The Microsoft path still holds its own token, so the server's answer to "who is
            // this" is what changes — and asking again is the only thing that reveals it.
            await _store.ResolveIdentityAsync();
            Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SubmitAppleAsync(string? displayName, string? handle)
    {
        if (_appleIdentityToken is null) return;

        var outcome = await _apple.SignInAsync(
            _appleIdentityToken,
            displayName ?? _appleSuggestedName,
            handle,
            CancellationToken.None);

        if (outcome.Succeeded)
        {
            Clear();
            return;
        }

        if (outcome.RequiresProfile)
        {
            PendingProvider = ExternalProvider.Apple;

            // Apple's own suggestion first, and only on the first pass — it is never offered again.
            if (string.IsNullOrWhiteSpace(DisplayName))
                DisplayName = outcome.NeedsProfile!.SuggestedDisplayName ?? _appleSuggestedName ?? string.Empty;

            Raise(nameof(NeedsProfile));
            Raise(nameof(NeedsHandle));
            Raise(nameof(CanLinkExistingAccount));

            Message = outcome.NeedsProfile!.HandleProblem
                      ?? "Almost there — choose a name and an @name to finish setting up your account.";
            return;
        }

        Message = outcome.Reason;
    }

    /// <summary>Forgets a pending external sign-in, including Apple's one-shot name.</summary>
    public void Clear()
    {
        PendingProvider = ExternalProvider.None;
        _appleIdentityToken = null;
        _appleSuggestedName = null;
        DisplayName = string.Empty;
        Handle = string.Empty;
        ShouldLinkInstead = false;
        Message = null;

        Raise(nameof(NeedsProfile));
        Raise(nameof(NeedsHandle));
        Raise(nameof(ShouldLinkInstead));
        Raise(nameof(CanLinkExistingAccount));
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Which provider vouched for somebody who has no account here yet.</summary>
public enum ExternalProvider
{
    None,
    Microsoft,
    Apple,
}
