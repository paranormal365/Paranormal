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
    private readonly IMediaIngestService _media;
    private readonly ILogger<AccountClosureService> _log;

    private readonly Apple.AppleCredentialService _apple;

    public AccountClosureService(
        IDbContextFactory<BenDataContext> dbContextFactory,
        Apple.AppleCredentialService apple,
        IMediaIngestService media,
        ILogger<AccountClosureService> log)
    {
        _dbContextFactory = dbContextFactory;
        _apple = apple;
        _media = media;
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

        // Apple first, outside the transaction: it is a network call, and a deletion must not
        // fail because Apple is slow or down. A token Apple refuses stays behind, stamped, for a
        // later attempt; the person is gone either way (item 229, App Review 5.1.1(v)).
        await _apple.RevokeAllAsync(userId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await AnonymiseAsync(db, user, ct, _media, _log);

        await transaction.CommitAsync(ct);

        // No email address is left to write, and nothing here names the person: the point of the
        // log line is that a closure happened and when, for a support question later.
        _log.LogInformation("Account {UserId} was closed by its owner at {ClosedAt:u}.",
            userId, user.DateClosed);

        // A seller who leaves with money on the books (store sellers P10): allowed — the store
        // still owes it, or is owed it — and the SuperAdmins are told, so it is settled by hand.
        var unpaid = (await db.StoreSellerEarnings.AsNoTracking().Where(e => e.SellerAppUserId == userId && e.PayoutId == null)
            .Select(e => e.Amount).ToListAsync(ct)).Sum();
        if (unpaid != 0m)
        {
            try
            {
                var admins = await Store.StoreAlerts.SuperAdminIdsAsync(db, ct);
                if (admins.Count > 0)
                    await new PlatformMessageService(_dbContextFactory).SendAsync(
                        "A seller closed their account with earnings unpaid",
                        $"A seller closed their account while {Ben.Service.Models.Store.StoreMoney.Format(unpaid)} of their earnings was unpaid. "
                      + $"Settle it on their page: /admin/store/sellers/{userId}", admins, admins[0], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "The admins could not be told that closed account {UserId} had unpaid earnings.", userId);
            }
        }

        return new ClosureResult(true, null);
    }

    /// <summary>
    /// Strips the person out of an account row and destroys everything that is only theirs.
    /// </summary>
    /// <remarks>
    /// <para><b>Shared with the SuperAdmin purge</b> (item: delete a user). Two copies of these
    /// rules would drift, and the copy that drifts is always the one that leaves a credential
    /// behind — so the caller decides who may do this and what else goes, and this decides what
    /// "the person is gone" means.</para>
    ///
    /// <para>Assumes an open transaction and does not commit: the caller may have more to do in
    /// the same one, and a half-closed account — contact rows gone, credentials intact — is worse
    /// than either outcome.</para>
    /// </remarks>
    /// <param name="media">
    /// Used to delete the avatar's bytes and its derivatives. Optional because
    /// <c>AppUserPurge</c> also calls this and does not hold one; when it is null the photo rows
    /// still go and the bytes are left, which is the behaviour before 2026-09-17 and is recorded
    /// as a separate finding against that purge (it uses <c>DeleteAsync</c> where it should use
    /// <c>DeleteAllAsync</c>, so it leaves thumbnails behind too).
    /// </param>
    internal static async Task AnonymiseAsync(
        BenDataContext db, AppUser user, CancellationToken ct,
        IMediaIngestService? media = null, ILogger? log = null)
    {
        var userId = user.Id;

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

        // ── the photos, and their bytes ───────────────────────────────────────
        //
        // This used to remove the join rows and leave the bytes "to the file sweeper"
        // (2026-09-17 audit). There is no file sweeper. The only job that deletes bytes is
        // MediaRetentionJob, and it selects rows with an ExpiresAtUtc, which only the tour plan
        // sets — so an avatar's UploadFile row, its bytes and its thumbnail stayed indefinitely,
        // still carrying the AppUserId of the account just anonymised.
        //
        // your-profile.md promises the opposite in as many words: name, email, password, phone,
        // addresses, "photos" and sign-in methods "are destroyed". This is also the path built
        // for Apple Guideline 5.1.1(v), so the promise is one somebody relied on twice.
        //
        // Through DeleteAllAsync rather than DeleteAsync, so the EXIF-stripped copy and the
        // thumbnail go with the original — siblings of the same path, and the thing an earlier
        // fix to MediaIngestService records having been missed once before.
        var photoRows = await db.AppUserPhotos
            .Where(p => p.AppUserId == userId)
            .ToListAsync(ct);

        var photoFileIds = photoRows.Select(p => p.UploadFileId).Distinct().ToList();

        // Only files nothing else points at. The old comment's caution was the right instinct
        // about the wrong subject: an avatar is minted per upload, but a shared file must not be
        // pulled out from under whatever else holds it.
        var photoFiles = await db.UploadFiles
            .Where(f => photoFileIds.Contains(f.Id)
                     && !db.AppUserPhotos.Any(o => o.UploadFileId == f.Id && o.AppUserId != userId))
            .ToListAsync(ct);

        db.AppUserPhotos.RemoveRange(photoRows);
        db.UploadFiles.RemoveRange(photoFiles);

        await db.SaveChangesAsync(ct);

        // ── their store orders ────────────────────────────────────────────────
        // Kept — a sale is a tax record — but no longer theirs: finished orders lose the name,
        // email, phone and street now; an order still on its way keeps its address until it is
        // delivered (storefront, Ben 09/24/2026). Here rather than in each caller, for the reason
        // above: closure and the SuperAdmin purge must not disagree about what an order keeps.
        await Store.StoreOrderScrub.DetachAndScrubAsync(db, userId, DateTime.UtcNow, ct);

        // Items they sold stay in the store as the site's own: the item is not theirs to take
        // with them, and a seller who has gone cannot be paid or asked a question.
        // Tracked rather than ExecuteUpdate: closure's own tests run on the InMemory provider.
        foreach (var sold in await db.StoreProducts.Where(p => p.SellerAppUserId == userId).ToListAsync(ct))
            sold.SellerAppUserId = null;
        // Their name on the packages they sent goes too (store sellers P10): the buyer's "Ships
        // from" reads as a former seller. The packages and the earnings stay — they are money records.
        foreach (var sent in await db.StoreOrderParcels.Where(x => x.SellerAppUserId == userId && x.SellerName != null).ToListAsync(ct))
            sent.SellerName = AccountClosure.FormerMemberName;
        // Their questions about the store's items (store sellers P12) are about them, and go with them.
        // An answer promoted to an FAQ stays: it was copied, and names nobody.
        db.StoreProductQuestions.RemoveRange(await db.StoreProductQuestions.Where(q => q.AskerAppUserId == userId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        // Bytes after the rows, and never fatal: a closure that has already anonymised the
        // account must not fail because one blob would not delete. It is logged instead.
        if (media is not null)
        {
            foreach (var file in photoFiles)
            {
                if (string.IsNullOrWhiteSpace(file.StoragePath)) continue;
                try { await media.DeleteAllAsync(file.StoragePath, ct); }
                catch (Exception ex)
                {
                    log?.LogWarning(ex,
                        "Closed account {UserId}: could not delete photo bytes at {Path}.",
                        userId, file.StoragePath);
                }
            }
        }

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
    }
}
