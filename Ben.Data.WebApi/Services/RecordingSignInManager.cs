using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// A <see cref="SignInManager{TUser}"/> that writes a <see cref="SignInEvent"/> for every password
/// attempt.
/// </summary>
/// <remarks>
/// <para><b>Why here and not in a controller.</b> The sign-in endpoint is not ours: it is mapped
/// by <c>MapIdentityApi&lt;AppUser&gt;()</c>, so there is no action to add a line to. Identity
/// routes every password check through this class, which makes it the one place both outcomes
/// pass through — and it stays true if the endpoint's shape changes underneath us.</para>
///
/// <para><b>Recording must never break signing in.</b> A failure to write the row is swallowed and
/// logged: the dashboard losing a data point is a smaller problem than a database hiccup locking
/// everyone out of the site. That is the same reasoning the rate-limit provider uses when it
/// cannot reach its settings.</para>
///
/// <para><b>Entra is not covered here.</b> Those sign-ins never touch this class — they arrive as
/// a bearer token validated by the JWT handler — which is why the row carries a
/// <see cref="SignInEvent.Method"/> at all. Counting them is the claims-transformation's job,
/// where the tokens actually land.</para>
/// </remarks>
public sealed class RecordingSignInManager : SignInManager<AppUser>
{
    /// <summary>Value written to <see cref="SignInEvent.Method"/> for password sign-ins.</summary>
    public const string PasswordMethod = "password";

    /// <summary>Value written to <see cref="SignInEvent.Method"/> for Sign in with Apple.</summary>
    public const string AppleMethod = "apple";

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly ILogger<RecordingSignInManager> _log;

    public RecordingSignInManager(
        UserManager<AppUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<AppUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<AppUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<AppUser> confirmation,
        IDbContextFactory<BenDataContext> dbContextFactory,
        ILogger<RecordingSignInManager> recordingLogger)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _dbContextFactory = dbContextFactory;
        _log = recordingLogger;
    }

    /// <summary>
    /// A closed account can never sign in again, by any route.
    /// </summary>
    /// <remarks>
    /// <para>The permanent lockout set by <see cref="AccountClosureService"/> already stops the
    /// password path, and the anonymised email means nothing can find the account by address. This
    /// is the third answer, and the one that does not depend on either of those holding: Identity
    /// calls <c>CanSignInAsync</c> before every sign-in, including two-factor and external
    /// providers, so a closed account is refused even if some future path forgets to check.</para>
    ///
    /// <para>Deliberately no distinct message. Identity's caller turns this into the same "invalid
    /// email or password" every other refusal gives, and that is right — telling a stranger that
    /// an address belonged to a closed account tells them the address existed.</para>
    /// </remarks>
    public override async Task<bool> CanSignInAsync(AppUser user)
    {
        if (user.DateClosed is not null) return false;
        return await base.CanSignInAsync(user);
    }

    /// <summary>
    /// The password check itself. Overridden rather than <c>PasswordSignInAsync</c> because this
    /// is the one both the sign-in endpoint and the two-factor path funnel through.
    /// </summary>
    public override async Task<SignInResult> CheckPasswordSignInAsync(
        AppUser user, string password, bool lockoutOnFailure)
    {
        var result = await base.CheckPasswordSignInAsync(user, password, lockoutOnFailure);

        // user.Id is known here even on failure, because Identity has already found the account by
        // the address given — an attempt against an address matching nothing never reaches this
        // method, and is not counted.
        await RecordAsync(user.Id, result.Succeeded, PasswordMethod);

        return result;
    }

    /// <summary>
    /// Records a sign-in that never passes a password check — Sign in with Apple.
    /// </summary>
    /// <remarks>
    /// <para>Called explicitly rather than by overriding <c>SignInAsync</c>, which the
    /// password path also runs: overriding it would count every password sign-in twice, once here
    /// and once from <see cref="CheckPasswordSignInAsync"/>. An explicit call at the one place an
    /// Apple session is minted is the only shape that counts each sign-in exactly once.</para>
    ///
    /// <para><b>Entra still is not covered</b>, and not by oversight. Those sessions arrive as a
    /// bearer token validated per-request by the JWT handler; there is no moment that is "the
    /// sign-in", so the honest options are to invent one or to say so. The dashboard says so.</para>
    /// </remarks>
    public Task RecordExternalSignInAsync(Guid appUserId, string method)
        => RecordAsync(appUserId, succeeded: true, method);

    private async Task RecordAsync(Guid? appUserId, bool succeeded, string method)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);

            db.SignInEvents.Add(new SignInEvent
            {
                Id = Guid.NewGuid(),
                AppUserId = appUserId,
                Utc = DateTime.UtcNow,
                Succeeded = succeeded,
                Method = method,
            });

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Deliberately swallowed — see the class remarks. Signing in matters more than
            // counting sign-ins.
            _log.LogWarning(ex, "Could not record a sign-in event; the sign-in itself was unaffected.");
        }
    }
}
