using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Apple;

/// <summary>
/// Keeps a person's Sign in with Apple refresh token at sign-in, and spends it at deletion.
/// </summary>
/// <remarks>
/// <para><b>Remembering is best-effort.</b> The identity token already proved who the person
/// is; the authorization code is only for later. A code that will not exchange — spent, expired,
/// minted for a client this server has no secret for, Apple unreachable — is logged and the
/// sign-in goes on. An older client that sends no code leaves nothing to revoke, and that is
/// the state the site was in before item 229.</para>
///
/// <para><b>Revoking is thorough but never blocking.</b> Every stored token is presented to
/// Apple; one Apple accepts is deleted, one it refuses is kept with the failure stamped, so a
/// later sweep can try again — and the deletion proceeds either way. A person must be able to
/// leave whether or not Apple is answering that minute.</para>
/// </remarks>
public sealed class AppleCredentialService
{
    private const string Purpose = "Ben.Apple.RefreshToken.v1";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IAppleTokenClient _apple;
    private readonly IDataProtector _protector;
    private readonly ILogger<AppleCredentialService> _log;

    public AppleCredentialService(
        IDbContextFactory<BenDataContext> dbFactory,
        IAppleTokenClient apple,
        IDataProtectionProvider protection,
        ILogger<AppleCredentialService> log)
    {
        _dbFactory = dbFactory;
        _apple     = apple;
        _protector = protection.CreateProtector(Purpose);
        _log       = log;
    }

    /// <summary>
    /// Exchanges the authorization code a sign-in brought and keeps the refresh token for
    /// <paramref name="userId"/>. True when something was kept.
    /// </summary>
    public async Task<bool> RememberAsync(Guid userId, string clientId, string? authorizationCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode) || !_apple.IsConfigured) return false;

        var exchange = await _apple.ExchangeCodeAsync(authorizationCode, clientId, ct);
        if (!exchange.Succeeded)
        {
            _log.LogWarning("Kept no Apple refresh token for {UserId} ({ClientId}): {Error}.", userId, clientId, exchange.Error);
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.AppleCredentials.FirstOrDefaultAsync(c => c.AppUserId == userId && c.ClientId == clientId, ct);
        if (row is null)
        {
            row = new AppleCredential { Id = Guid.NewGuid(), AppUserId = userId, ClientId = clientId };
            db.AppleCredentials.Add(row);
        }
        row.Subject               = exchange.Subject;
        row.ProtectedRefreshToken = _protector.Protect(exchange.RefreshToken!);
        row.DateCreated           = DateTime.UtcNow;
        row.DateRevocationFailed  = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Presents every token kept for <paramref name="userId"/> to Apple for revocation. Returns
    /// how many Apple accepted; the rest stay, stamped, for a later attempt.
    /// </summary>
    public async Task<int> RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AppleCredentials.Where(c => c.AppUserId == userId).ToListAsync(ct);
        if (rows.Count == 0) return 0;

        var revoked = 0;
        foreach (var row in rows)
        {
            string token;
            try { token = _protector.Unprotect(row.ProtectedRefreshToken); }
            catch (Exception ex)
            {
                // A token that cannot be read cannot be revoked and never will be: the key ring
                // that protected it is gone. Nothing to present to Apple; the row goes.
                _log.LogError(ex, "An Apple refresh token for {UserId} could not be unprotected; dropping it.", userId);
                db.AppleCredentials.Remove(row);
                continue;
            }

            if (await _apple.RevokeAsync(token, row.ClientId, ct))
            {
                db.AppleCredentials.Remove(row);
                revoked++;
            }
            else
            {
                row.DateRevocationFailed = DateTime.UtcNow;
                _log.LogError("Apple did not revoke a token for {UserId} ({ClientId}); kept for a later attempt.", userId, row.ClientId);
            }
        }
        await db.SaveChangesAsync(ct);
        return revoked;
    }
}
