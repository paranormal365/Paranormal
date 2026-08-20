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
