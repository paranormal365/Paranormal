using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Closing your own account: the person goes, the work stays.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> App Review Guideline 5.1.1(v) requires an app that lets you create
/// an account to let you delete it from inside the app. A link to a web form does not satisfy it.
/// Nothing here had a delete path at all.</para>
///
/// <para><b>Why it anonymises instead of deleting.</b> A member does not own the things they
/// authored on their own — a case file belongs to the group and often to a paying client, and an
/// investigation report is a record other people depend on and may be legally obliged to keep.
/// Hard-deleting the row would take all of it with them, so one person leaving could erase a
/// group's case history. Ben chose this shape on 2026-08-28. The person's identity, credentials
/// and contact details are destroyed; the work stays, attributed to
/// <see cref="AccountClosure.FormerMemberName"/>.</para>
///
/// <para><b>Why an owner is refused.</b> Exactly one <see cref="OrganizationMemberRole.Owner"/>
/// exists per organization. Anonymising one leaves a group with no one able to administer it, no
/// route to billing, and no way to appoint a replacement — a wreck that has to be repaired by hand
/// in the database. So an owner is told, by name, which organizations they must hand over first.
/// Apple accepts a blocked path when the app says clearly what to do about it, which is why
/// <see cref="CheckAsync"/> returns the organizations rather than a bare refusal.</para>
///
/// <para><b>It is not reversible and does not pretend to be.</b> There is no undo, no grace
/// period and no reactivation: the credentials are gone and the email address is freed for
/// re-registration. The confirmation belongs in the UI, not in a soft-delete nobody would ever
/// come back through.</para>
/// </remarks>
public sealed class AccountClosureService
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly ILogger<AccountClosureService> _log;

    public AccountClosureService(
        IDbContextFactory<BenDataContext> dbContextFactory,
        ILogger<AccountClosureService> log)
    {
        _dbContextFactory = dbContextFactory;
        _log = log;
    }

    /// <summary>An organization the caller owns, and therefore has to hand over first.</summary>
    public sealed record BlockingOrganization(Guid OrganizationId, string Name, string UrlName);

    /// <summary>
    /// Whether this account can be closed, and what stands in the way if not.
    /// </summary>
    /// <param name="CanClose">True when nothing blocks it.</param>
    /// <param name="OwnedOrganizations">
    /// Organizations the caller owns. Non-empty means <see cref="CanClose"/> is false; the UI names
    /// them, because "you can't do this" without saying what to do about it is a dead end.
    /// </param>
    public sealed record ClosureCheck(bool CanClose, IReadOnlyList<BlockingOrganization> OwnedOrganizations);

    /// <summary>What the caller must do before their account can be closed, if anything.</summary>
    public async Task<ClosureCheck> CheckAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Active memberships only. A membership that has been deactivated is not ownership of
        // anything any more, and refusing over one would strand somebody permanently on a group
        // they already left.
        var owned = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId
                     && m.IsActive
                     && m.Role == OrganizationMemberRole.Owner)
            .Join(db.Organizations.AsNoTracking(),
                  m => m.OrganizationId, o => o.Id,
                  (m, o) => new BlockingOrganization(o.Id, o.Name, o.UrlName))
            .ToListAsync(ct);

        return new ClosureCheck(owned.Count == 0, owned);
    }

    /// <summary>The outcome of a close attempt.</summary>
    /// <param name="Closed">True when the account was closed.</param>
    /// <param name="Refusal">
    /// A sentence to show the person when it was not. Null on success. Deliberately prose rather
    /// than a code — <c>WebApiClient</c> only surfaces a server's own sentences.
    /// </param>
    public sealed record ClosureResult(bool Closed, string? Refusal);

    /// <summary>
    /// Closes the account: anonymises the person, keeps everything they authored.
    /// </summary>
    /// <remarks>
    /// One transaction. A half-closed account — contact rows gone, credentials intact — is worse
    /// than either outcome, and this touches six tables.
    /// </remarks>
    public async Task<ClosureResult> CloseAsync(Guid userId, CancellationToken ct = default)
    {
        var check = await CheckAsync(userId, ct);
        if (!check.CanClose)
        {
            var names = string.Join(", ", check.OwnedOrganizations.Select(o => o.Name));
            return new ClosureResult(false,
                $"You still own {names}. Make someone else the owner, or close the group, and then "
              + "you can delete your account.");
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return new ClosureResult(false, "That account no longer exists.");

        if (user.DateClosed is not null)
            // Idempotent on purpose: a retry after a dropped connection must not read as an error
            // and send somebody looking for an account that is already gone.
            return new ClosureResult(true, null);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // ── the person ────────────────────────────────────────────────────────
        user.DateClosed = DateTime.UtcNow;
        user.DisplayName = AccountClosure.FormerMemberName;
        user.FirstName = null;
        user.LastName = null;
        user.Gender = null;
        user.BirthYear = null;
        user.SharePrivatePhotoWithClients = false;

        // The @name is replaced rather than nulled. It appears inside other people's posts, so
        // removing it entirely would break those mentions; a fresh opaque one keeps them rendering
        // while no longer naming anybody. Handle has a unique index, hence the account id.
        // "former-" + 23 hex characters is exactly UserHandle.MaxLength (30) and still unique
        // enough that a collision is not a thing that happens.
        user.Handle = $"former-{userId:N}"[..Ben.Data.Common.Helpers.UserHandle.MaxLength];

        var closedEmail = AccountClosure.ClosedEmailFor(userId);
        user.Email = closedEmail;
        user.NormalizedEmail = closedEmail.ToUpperInvariant();
        user.UserName = closedEmail;
        user.NormalizedUserName = closedEmail.ToUpperInvariant();
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;

        // ── the credentials ───────────────────────────────────────────────────
        user.PasswordHash = null;
        user.TwoFactorEnabled = false;
        // A new stamp invalidates anything derived from the old one. Belt and braces alongside the
        // lockout below — this account must not be able to sign in by any route that still exists.
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.AccessFailedCount = 0;

        // ── the contact details ───────────────────────────────────────────────
        // Straight deletes: these tables hold nothing but the person. Nothing references them, so
        // there is no history to keep by anonymising them instead.
        db.UserAddresses.RemoveRange(db.UserAddresses.Where(a => a.AppUserId == userId));
        db.UserEmails.RemoveRange(db.UserEmails.Where(e => e.AppUserId == userId));
        db.UserPhones.RemoveRange(db.UserPhones.Where(p => p.AppUserId == userId));
        db.UserLinks.RemoveRange(db.UserLinks.Where(l => l.AppUserId == userId));

        // The photo JOIN rows go; the UploadFile bytes are left to the file sweeper rather than
        // deleted from under anything else that might reference the same file.
        db.AppUserPhotos.RemoveRange(db.AppUserPhotos.Where(p => p.AppUserId == userId));

        await db.SaveChangesAsync(ct);

        // ── external sign-ins, roles, claims and tokens ───────────────────────
        // A left-behind login row would let Sign in with Apple walk straight back into the
        // anonymised account, which is the one hole that would make everything above pointless.
        // With it gone, a later Apple sign-in creates a NEW account — the correct outcome.
        //
        // Done through this DbContext rather than UserManager on purpose. UserManager resolves its
        // own scoped context: its writes would land outside this transaction, on a row it had read
        // before any of the changes above were committed, and saving that stale row would undo
        // them. The Identity tables are part of BenDataContext, so they are reachable from here.
        db.UserLogins.RemoveRange(db.UserLogins.Where(l => l.UserId == userId));
        db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == userId));
        db.UserClaims.RemoveRange(db.UserClaims.Where(c => c.UserId == userId));
        db.UserTokens.RemoveRange(db.UserTokens.Where(t => t.UserId == userId));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // No email address is left to write, and nothing here names the person: the point of the
        // log line is that a closure happened and when, for a support question later.
        _log.LogInformation("Account {UserId} was closed by its owner at {ClosedAt:u}.",
            userId, user.DateClosed);

        return new ClosureResult(true, null);
    }
}
