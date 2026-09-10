using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ben.Data.WebApi.Client.Auth;
using Ben.Web.Services.WebApi;

namespace Ben.Desktop.App.Library.ViewModels;

/// <summary>
/// The sign-in screens' view of <see cref="SessionStore"/>: the same state machine, in the shape
/// XAML binds to.
/// </summary>
/// <remarks>
/// Deliberately thin. The decisions — what a 401 means, when to refresh, whether a session ending
/// deserves a banner — all live in the client library, where the website's own client can be held
/// to the same answers. Anything decided here would be a decision only the desktop app makes.
/// </remarks>
public sealed class SessionViewModel : INotifyPropertyChanged
{
    private readonly SessionStore _store;

    public SessionViewModel(SessionStore store)
    {
        _store = store;
        _store.StateChanged += _ => RaiseAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionState State => _store.State;

    public bool IsSignedIn => State.IsSignedIn;
    public bool IsBusy => State.IsBusy;
    public bool NeedsTwoFactor => State.NeedsTwoFactor;

    /// <summary>A provider vouched for them, but no account here is linked to it yet.</summary>
    public bool NeedsLocalAccount => State.NeedsLocalAccount;
    public string? DisplayName => State.Me?.Email;

    /// <summary>Whether the "your session ended" banner should be on screen.</summary>
    public bool ShowSessionEndedBanner => State.SessionEndedUnexpectedly;

    /// <summary>
    /// What to tell somebody about the last attempt, or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Every branch names what actually happened. The one thing this must never do is answer
    /// "check your email and password" for a refusal that had nothing to do with either — that
    /// sends people to reset a password that was always right.
    /// </remarks>
    public string? Message => State.Failure switch
    {
        null => null,
        LoginFailure.InvalidCredentials => "That email address and password don't match an account.",
        LoginFailure.EmailNotConfirmed =>
            "This account's email address hasn't been confirmed yet. Use the link we sent, or ask for another.",
        LoginFailure.LockedOut =>
            "This account is locked after too many attempts. Waiting is the only thing that helps — the right password won't work until it clears.",
        LoginFailure.RateLimited => State.RetryAfter is { } wait
            ? $"Too many attempts. Try again in about {Math.Ceiling(wait.TotalSeconds)} seconds."
            : "Too many attempts. Give it a minute before trying again.",
        LoginFailure.Unreachable =>
            "Couldn't reach the server, so your details were never checked. This is a connection or a setting, not your password.",
        LoginFailure.RequiresTwoFactor => "Enter the code from your authenticator app.",
        LoginFailure.UnknownRefusal =>
            "The server refused the sign-in without saying why. Try again — and if it keeps happening, it isn't your password.",
        _ => null,
    };

    public Task SignInAsync(string email, string password) => _store.SignInAsync(email, password);

    public Task SubmitTwoFactorAsync(string code, bool isRecoveryCode = false)
        => _store.SubmitTwoFactorAsync(code, isRecoveryCode);

    public void CancelTwoFactor() => _store.CancelTwoFactor();

    public Task SignOutAsync() => _store.SignOutAsync();

    public void AcknowledgeSessionEnded() => _store.AcknowledgeSessionEnded();

    private void RaiseAll()
    {
        Raise(nameof(State));
        Raise(nameof(IsSignedIn));
        Raise(nameof(IsBusy));
        Raise(nameof(NeedsTwoFactor));
        Raise(nameof(NeedsLocalAccount));
        Raise(nameof(DisplayName));
        Raise(nameof(ShowSessionEndedBanner));
        Raise(nameof(Message));
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
