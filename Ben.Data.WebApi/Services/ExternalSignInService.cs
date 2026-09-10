using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// What every external sign-in has in common once the provider's own token has been verified:
/// resolve the identity to somebody here, or claim an account that already exists, or create one.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Apple and Microsoft were two separate doors, each hand-rolling the
/// same checks, and on 2026-09-10 every defect found on one was then found on the other: linking
/// that skipped the second factor, a password door that counted no failures toward a lockout, a
/// session minted for a closed or locked account, a duplicate account whenever the provider's
/// address did not match. Fixing them twice is how they diverged again — the two link endpoints
/// ended the day answering the same refusal in two different shapes. A third provider would have
/// inherited none of it.</para>
///
/// <para><b>The rules, once.</b> An external identity is the provider's <b>subject</b>, stored as
/// an external login; the address is decoration. A returning subject signs in — but only after
/// <see cref="MaySignInAsync"/>, because <c>SignInManager.SignInAsync</c> is unconditional and an
/// administrator's refusal has to hold on this door too. An unknown subject whose address the
/// provider has <b>verified</b> joins the account holding that address; an unverified address
/// proves nothing and routes to the link door instead. Claiming an account is a real sign-in:
/// password, lockout, confirmed account, second factor. Creating one never invents a permanent
/// handle unless the caller says so, and confirms the address only when the provider did — an
/// unverified one is sent a confirmation, as a website sign-up is.</para>
///
/// <para>Controllers stay responsible for two things this class cannot do: validating the
/// provider's token, and writing HTTP. Every decision in between is here, where one test matrix
/// covers every door.</para>
/// </remarks>
public sealed class ExternalSignInService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserHandleService _handles;
    private readonly IConfirmationSender _confirmations;

    public ExternalSignInService(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        UserHandleService handles,
        IConfirmationSender confirmations)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _handles       = handles;
        _confirmations = confirmations;
    }

    // ── May this account be used at all ───────────────────────────────────────

    /// <summary>
    /// Whether a session may be minted for this account through an external provider: not closed,
    /// and not locked out.
    /// </summary>
    /// <remarks>
    /// <para>Two checks, deliberately, and neither is <c>CanSignInAsync</c>. Identity checks
    /// lockout <i>alongside</i> that method, never inside it, so asking it alone lets a locked
    /// account straight through — and lockout is the only lever an administrator has short of
    /// closing an account. A door that ignores it makes that lever do nothing.</para>
    ///
    /// <para><c>CanSignInAsync</c> is not asked because it folds in the confirmed-account
    /// requirement, and confirmation proves the <i>address</i>, not the person. On this door the
    /// provider's token proves the person. The only way an account holding an external login is
    /// unconfirmed is that it was created here from that provider's unverified address claim (a
    /// website sign-up cannot link before confirming: the password check refuses it), and refusing
    /// that account would strand the person it was just made for. What confirmation still gates
    /// is what an address is for: a password reset, and being written to.</para>
    ///
    /// <para>This is also the gate the Entra claims transformation applies to every Microsoft
    /// request, so the two Microsoft doors cannot disagree about who may come in.</para>
    /// </remarks>
    public async Task<bool> MaySignInAsync(AppUser user)
        => user.DateClosed is null && !await _userManager.IsLockedOutAsync(user);

    // ── Resolving a subject ───────────────────────────────────────────────────

    /// <summary>Resolves a verified external identity to an account here, or says why it cannot.</summary>
    public async Task<ResolveResult> ResolveAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        // 1. A returning identity. Looked up by SUBJECT, never by address — this is what makes a
        //    Hide My Email relay survivable, and it is why a rotated or withheld address changes
        //    nothing about who somebody is.
        var linked = await _userManager.FindByLoginAsync(identity.Provider, identity.Subject);
        if (linked is not null)
            return await MaySignInAsync(linked)
                ? new ResolveResult.Found(linked)
                : new ResolveResult.Refused();

        // 2. An identity whose VERIFIED address already has an account here joins it. Verified is
        //    the whole condition: an unverified claim on an address is not proof of holding it, and
        //    joining on one would let anybody with a provider account attach themselves to any
        //    address they could name.
        if (identity.EmailVerified && !string.IsNullOrWhiteSpace(identity.Email))
        {
            var byEmail = await _userManager.FindByEmailAsync(identity.Email);
            if (byEmail is not null)
            {
                var link = await _userManager.AddLoginAsync(
                    byEmail, new UserLoginInfo(identity.Provider, identity.Subject, identity.Email));
                if (!link.Succeeded)
                    return new ResolveResult.Failed(Describe(link));

                // An account that reaches us through a provider that has verified this address has
                // now had it proved; leaving it unconfirmed would lock them out of the website they
                // can already use on the phone.
                if (!byEmail.EmailConfirmed)
                {
                    byEmail.EmailConfirmed = true;
                    await _userManager.UpdateAsync(byEmail);
                }

                return await MaySignInAsync(byEmail)
                    ? new ResolveResult.Found(byEmail)
                    : new ResolveResult.Refused();
            }
        }

        // 3. Nobody here yet. If the (unverified) address is nonetheless somebody's, say which door
        //    to use — creating a second account would hold none of their history, and the server
        //    would refuse it anyway.
        var addressTaken = !string.IsNullOrWhiteSpace(identity.Email)
                        && await _userManager.FindByEmailAsync(identity.Email) is not null;

        return new ResolveResult.Unknown(ShouldLinkInstead: addressTaken);
    }

    // ── Claiming an account that already exists ───────────────────────────────

    /// <summary>
    /// Joins an external identity to an account somebody already has here, proving the account
    /// with a full sign-in.
    /// </summary>
    /// <remarks>
    /// The provider token proves the external identity; the password proves the account. Neither
    /// alone is enough. And the password check is the <i>whole</i> sign-in check, not a bare
    /// password comparison: <c>CheckPasswordSignInAsync</c> counts a failure toward lockout and
    /// refuses an account that may not sign in, but it never reports a second factor — that is
    /// <c>PasswordSignInAsync</c>'s job, which cannot be used here because it creates a cookie
    /// sign-in. So the second factor is checked explicitly. Without it, linking would not skip
    /// two-factor once; it would remove it for good, because afterwards the external identity signs
    /// in on its own and the code is never asked for again.
    /// </remarks>
    public async Task<LinkResult> LinkAsync(
        ExternalIdentity identity,
        string email,
        string password,
        string? twoFactorCode,
        string? twoFactorRecoveryCode,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email);

        // One answer for "no such account" and "wrong password", and the same SHAPE — anything
        // that tells them apart makes this a way to ask whether an address has an account here.
        if (user is null)
            return new LinkResult.Refused(LoginRefusal.Failed);

        var check = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut)  return new LinkResult.Refused(LoginRefusal.LockedOut);
        if (check.IsNotAllowed) return new LinkResult.Refused(LoginRefusal.NotAllowed);
        if (!check.Succeeded)   return new LinkResult.Refused(LoginRefusal.Failed);

        if (await _userManager.GetTwoFactorEnabledAsync(user)
            && !await VerifySecondFactorAsync(user, twoFactorCode, twoFactorRecoveryCode))
            return new LinkResult.Refused(LoginRefusal.RequiresTwoFactor);

        // Already held somewhere. Checked AFTER the password so the answer reveals nothing to a
        // caller who does not hold the account, and so an identity cannot be moved to a second
        // account by whoever asks last.
        var existingOwner = await _userManager.FindByLoginAsync(identity.Provider, identity.Subject);
        if (existingOwner is not null)
        {
            if (existingOwner.Id == user.Id)
                return new LinkResult.Linked(user, AlreadyWas: true);

            return new LinkResult.HeldByAnotherAccount();
        }

        var attach = await _userManager.AddLoginAsync(
            user, new UserLoginInfo(identity.Provider, identity.Subject, identity.Email ?? email));
        if (!attach.Succeeded)
            return new LinkResult.Failed(Describe(attach));

        return new LinkResult.Linked(user, AlreadyWas: false);
    }

    // ── Creating an account ───────────────────────────────────────────────────

    /// <summary>Creates an account for an identity nobody here has yet.</summary>
    /// <param name="handle">
    /// The chosen @name, or null to have one allocated. A handle is permanent, so a caller that can
    /// ask the person must ask; allocation is for a flow that cannot.
    /// </param>
    public async Task<RegisterResult> RegisterAsync(
        ExternalIdentity identity,
        string? displayName,
        string? handle,
        CancellationToken ct = default)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 200)
            return new RegisterResult.NeedsProfile(HandleProblem: null);

        string chosenHandle;
        if (handle is null)
        {
            // Allocated, for a caller with no way to ask. Done here rather than left to a backfill:
            // an account with no @name cannot be mentioned and is invisible to the feed until that
            // job next runs.
            chosenHandle = await _handles.AllocateAsync(name, identity.Email ?? string.Empty, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(handle))
                return new RegisterResult.NeedsProfile(HandleProblem: null);

            var (free, reason) = await _handles.IsAvailableAsync(handle, ct);
            if (!free)
                return new RegisterResult.NeedsProfile(HandleProblem: reason ?? "Choose another name.");

            chosenHandle = UserHandle.Normalize(handle);
        }

        // A withheld address gets a placeholder that can never resolve — .invalid is reserved for
        // exactly that — and the account remembers which kind of address it holds, because nothing
        // downstream can otherwise tell a machine-generated string from one somebody chose.
        var withheld = string.IsNullOrWhiteSpace(identity.Email);
        var email = withheld ? $"{identity.Subject}@{identity.Provider.ToLowerInvariant()}id.invalid" : identity.Email!;
        var kind =
            withheld                 ? EmailAddressKind.Unreachable
          : identity.IsPrivateEmail  ? EmailAddressKind.AppleRelay
          :                            EmailAddressKind.Ordinary;

        // The address belongs to somebody. Only reachable with an UNVERIFIED address — a verified
        // one was joined by ResolveAsync — which is exactly when joining automatically would be
        // wrong. Route to the link door; a second account would hold none of their history.
        if (await _userManager.FindByEmailAsync(email) is not null)
            return new RegisterResult.AddressTaken();

        // Confirmed only when the provider vouched for the address, or when there is no address to
        // confirm. An unverified claim is a claim: marking it confirmed let anybody hold an address
        // here that they do not own — not a takeover, since a taken address is refused above, but a
        // squat that stops the real owner signing up under their own address. So it starts
        // unconfirmed and is sent a confirmation, exactly as a website sign-up is. The placeholder
        // behind a withheld address can never receive mail, and leaving that account unconfirmed
        // would give it a state it could never leave.
        var confirmed = withheld || identity.EmailVerified;

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName        = name,
            Handle             = chosenHandle,
            EmailKind          = kind,
            EmailConfirmed     = confirmed,
            DateCreated        = DateTime.UtcNow,
        };

        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            // A race can lose the address or the handle between the checks above and the insert.
            // Same answers, so the person is routed rather than shown Identity's wording.
            if (created.Errors.Any(e => e.Description.Contains("Email", StringComparison.OrdinalIgnoreCase)))
                return new RegisterResult.AddressTaken();

            if (created.Errors.Any(e => e.Description.Contains("Handle", StringComparison.OrdinalIgnoreCase)))
                return new RegisterResult.NeedsProfile(HandleProblem: "That name was taken a moment ago. Try another.");

            return new RegisterResult.Failed(Describe(created));
        }

        var attach = await _userManager.AddLoginAsync(
            user, new UserLoginInfo(identity.Provider, identity.Subject, identity.Email ?? email));
        if (!attach.Succeeded)
        {
            await _userManager.DeleteAsync(user);   // rollback: an account nothing can sign into
            return new RegisterResult.Failed(Describe(attach));
        }

        // Sent after the login is attached, so a rollback above never leaves a confirmation link
        // pointing at an account that no longer exists. A send that fails does not fail the
        // creation: the account exists and its owner is signed in through their provider; the
        // sender has logged it and the profile offers to send again.
        if (!confirmed)
            await _confirmations.SendConfirmationAsync(user, returnUrl: null, ct);

        return new RegisterResult.Created(user, AwaitingConfirmation: !confirmed);
    }

    // ── The second factor, without a sign-in context ──────────────────────────

    /// <summary>
    /// Checks a second factor for a caller who has no sign-in context, which is what every
    /// external door is.
    /// </summary>
    /// <remarks>
    /// Returns false when nothing was supplied, which is how the caller is told to ask. A recovery
    /// code is <b>redeemed</b>, not merely checked — one that survived being used would not be a
    /// recovery code — and the two kinds are kept apart rather than guessed at by shape, because
    /// guessing wrong spends a single-use recovery code on a mistyped app code.
    /// </remarks>
    public async Task<bool> VerifySecondFactorAsync(AppUser user, string? code, string? recoveryCode)
    {
        // Spaces and hyphens are how these are printed and read aloud, and the rest of the site
        // strips them. Refusing a comfortably typed code would be a failure we caused.
        static string Clean(string value) =>
            value.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(recoveryCode))
            return await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, Clean(recoveryCode)) is { Succeeded: true };

        if (string.IsNullOrWhiteSpace(code))
            return false;

        return await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, Clean(code));
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}

/// <summary>A provider's verified statement about who is at the other end.</summary>
/// <param name="Provider">The external-login provider name: <c>"Apple"</c> or <c>"Microsoft"</c>.</param>
/// <param name="Subject">The provider's stable identifier for this person. THE identity.</param>
/// <param name="Email">What the provider said the address is, if it said. May be a relay.</param>
/// <param name="EmailVerified">
/// Whether the provider vouches for the address. Decides whether it may auto-link. False when the
/// provider does not say — an absent claim is not a verified one.
/// </param>
/// <param name="IsPrivateEmail">Apple's Hide My Email relay.</param>
public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified,
    bool IsPrivateEmail = false);

/// <summary>
/// Why a sign-in or link was refused, in Identity's own words — the same four <c>/login</c> puts in
/// its problem-detail, so one client mapping serves every door.
/// </summary>
public enum LoginRefusal
{
    /// <summary>Wrong password, or no such account. Deliberately one word for both.</summary>
    Failed,
    /// <summary>The account may not sign in yet — its address is unconfirmed.</summary>
    NotAllowed,
    /// <summary>Too many failures; waiting is the only cure.</summary>
    LockedOut,
    /// <summary>The password was right and a code is needed. Not a failure.</summary>
    RequiresTwoFactor,
}

public abstract record ResolveResult
{
    /// <summary>A known identity on an account that may sign in.</summary>
    public sealed record Found(AppUser User) : ResolveResult;

    /// <summary>A known identity on an account that may NOT sign in: closed or locked. Say nothing about why.</summary>
    public sealed record Refused() : ResolveResult;

    /// <summary>Nobody here yet.</summary>
    /// <param name="ShouldLinkInstead">The address already has an account; offer the link door, not the create form.</param>
    public sealed record Unknown(bool ShouldLinkInstead) : ResolveResult;

    public sealed record Failed(string Reason) : ResolveResult;
}

public abstract record LinkResult
{
    /// <param name="AlreadyWas">The identity was already on this very account; linking again is not an error.</param>
    public sealed record Linked(AppUser User, bool AlreadyWas) : LinkResult;

    public sealed record Refused(LoginRefusal Why) : LinkResult;

    /// <summary>This identity already belongs to a different account here.</summary>
    public sealed record HeldByAnotherAccount() : LinkResult;

    public sealed record Failed(string Reason) : LinkResult;
}

public abstract record RegisterResult
{
    /// <param name="AwaitingConfirmation">
    /// The address was not verified by the provider, so the account starts unconfirmed and a
    /// confirmation has been sent (or attempted). Usable through the provider meanwhile.
    /// </param>
    public sealed record Created(AppUser User, bool AwaitingConfirmation = false) : RegisterResult;

    /// <summary>A name and handle are still needed, or the handle offered will not do.</summary>
    public sealed record NeedsProfile(string? HandleProblem) : RegisterResult;

    /// <summary>The address already has an account; offer the link door.</summary>
    public sealed record AddressTaken() : RegisterResult;

    public sealed record Failed(string Reason) : RegisterResult;
}
