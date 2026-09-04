using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Admin;

/// <summary>
/// What deleting one person would destroy, and what it would leave standing.
/// </summary>
/// <remarks>
/// <para><c>RowWillSurvive</c> is true when the account row itself cannot go, because records
/// authored for a group still point at it. Said before the button is pressed rather than
/// discovered afterwards: "delete" that leaves a row behind is a promise the screen has to stop
/// making in advance.</para>
///
/// <para><c>Refusal</c> is a sentence when this cannot be done at all, and null otherwise. It is
/// the only thing that disables the button — <c>OwnedOrganizations</c> and
/// <c>PaidSubscriptions</c> are notices to read, not bars (Ben, 2026-09-04).</para>
/// </remarks>
public sealed record AppUserPurgePreview(
    Guid AppUserId,
    string DisplayName,
    string? Email,
    bool AlreadyClosed,

    // ── destroyed ─────────────────────────────────────────────────────────────
    int PersonalFieldSessions,
    int StoredFiles,
    int Memberships,
    int SignInEvents,
    int MessagesReceived,
    int FollowsAndBlocks,
    int ContactRows,
    int ExternalLogins,

    // ── kept, with the name removed ───────────────────────────────────────────
    int CaseNotes,
    int TimelineEntries,
    int GroupMessages,
    int GroupFieldSessions,
    int EventEvidence,
    int OtherAuthoredRecords,

    // ── consequences worth reading before pressing the button ─────────────────
    bool RowWillSurvive,
    IReadOnlyList<string> OwnedOrganizations,
    IReadOnlyList<string> PaidSubscriptions,
    string? Refusal);

/// <summary>What actually happened.</summary>
public sealed record AppUserPurgeResult(
    Guid AppUserId, string DisplayName, bool RowRemoved,
    int PersonalFieldSessions, int StoredFiles, int Memberships, int SignInEvents,
    int MessagesReceived, int FollowsAndBlocks);

/// <summary>
/// Deleting a person, as far as a person can be deleted (SuperAdmin).
/// </summary>
/// <remarks>
/// <para><b>Why this is not a row delete.</b> <c>AppUsers</c> is the principal of <b>335</b>
/// foreign keys, and <b>124</b> of those are a required <c>CreatedByAppUserId</c> on tables like
/// case notes, timeline entries and group messages. Deleting the row means deleting every one of
/// those rows too — which is a group's record of its own work, written by somebody who has left.
/// One person going must not erase it. So the account is stripped of the person and the work stays,
/// exactly as self-service closure already does.</para>
///
/// <para><b>But it does delete.</b> Everything that is only ever the person's — their personal
/// field sessions, the files under those, their sign-in history, their memberships, follows,
/// blocks, contact rows and external logins — is genuinely destroyed. An account that holds
/// nothing else therefore disappears completely: nothing points at it any more, so the row goes
/// too. That is the common case this exists for, and it is why the row delete is attempted rather
/// than assumed impossible.</para>
///
/// <para><b>The screen is told which of the two it will be, first.</b>
/// <see cref="AppUserPurgePreview.RowWillSurvive"/> is computed by asking the model — every
/// foreign key into <c>AppUsers</c>, counted — rather than from a list somebody keeps up to date.
/// A table added next year is covered on the day it is added, which is the lesson the
/// organization purge learned twice.</para>
///
/// <para><b>Only one refusal</b> (Ben, 2026-09-04): the last SuperAdmin, because there is no way
/// back from locking the platform out of itself. Owning a group and holding a paid subscription
/// are <i>notices</i>, not refusals — a SuperAdmin can hand a group over afterwards, and being
/// told is what matters.</para>
/// </remarks>
public sealed class AppUserPurge
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Ben.Data.Common.Interfaces.IFileStorageService _fileStorage;
    private readonly ILogger<AppUserPurge> _log;

    public AppUserPurge(
        IDbContextFactory<BenDataContext> dbFactory,
        Ben.Data.Common.Interfaces.IFileStorageService fileStorage,
        ILogger<AppUserPurge> log)
    {
        _dbFactory = dbFactory;
        _fileStorage = fileStorage;
        _log = log;
    }

    /// <summary>What this would do, before anybody commits to it.</summary>
    public async Task<AppUserPurgePreview?> PreviewAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var personalSessionIds = await PersonalSessionIdsAsync(db, userId, ct);

        var owned = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive && m.Role == OrganizationMemberRole.Owner)
            .Join(db.Organizations.AsNoTracking(), m => m.OrganizationId, o => o.Id, (m, o) => o.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        // Seats the PERSON pays for. An organization's own subscription is the group's bill and
        // survives them; a seat is theirs and keeps charging a card nobody is watching.
        var paid = await db.MemberSeatSubscriptions.AsNoTracking()
            .Where(s => s.AppUserId == userId && s.Status == SubscriptionStatus.Active)
            .Join(db.Organizations.AsNoTracking(), s => s.OrganizationId, o => o.Id, (s, o) => o.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        var refusal = await LastSuperAdminRefusalAsync(db, userId, ct);

        var caseNotes = await db.CaseNotes.AsNoTracking().CountAsync(x => x.AuthorAppUserId == userId, ct);
        var timeline = await db.CaseTimelineEntries.AsNoTracking().CountAsync(x => x.AuthorAppUserId == userId, ct);
        var groupMessages = await db.OrgMessages.AsNoTracking().CountAsync(x => x.AuthorAppUserId == userId, ct);
        var evidence = await db.EventEvidenceSubmissions.AsNoTracking().CountAsync(x => x.SubmittedByAppUserId == userId, ct);
        var groupSessions = await db.FieldSessionUploads.AsNoTracking()
            .CountAsync(s => s.SubmittedByAppUserId == userId && s.InvestigationId != null, ct);

        var counted = new AppUserPurgePreview(
            AppUserId:            userId,
            DisplayName:          user.DisplayName ?? user.Email ?? userId.ToString(),
            Email:                user.Email,
            AlreadyClosed:        user.DateClosed is not null,

            PersonalFieldSessions: personalSessionIds.Count,
            StoredFiles:           await StoredFileIdsAsync(db, userId, personalSessionIds, ct) is var files ? files.Count : 0,
            Memberships:           await db.OrganizationUserMemberships.AsNoTracking().CountAsync(m => m.AppUserId == userId, ct),
            SignInEvents:          await db.SignInEvents.AsNoTracking().CountAsync(e => e.AppUserId == userId, ct),
            MessagesReceived:      await db.UserMessageTos.AsNoTracking().CountAsync(m => m.ToAppUserId == userId, ct),
            FollowsAndBlocks:      await db.UserFollows.AsNoTracking().CountAsync(f => f.FollowerAppUserId == userId || f.FollowedAppUserId == userId, ct)
                                 + await db.UserBlocks.AsNoTracking().CountAsync(b => b.BlockerAppUserId == userId || b.BlockedAppUserId == userId, ct),
            ContactRows:           await db.UserAddresses.AsNoTracking().CountAsync(a => a.AppUserId == userId, ct)
                                 + await db.UserEmails.AsNoTracking().CountAsync(e => e.AppUserId == userId, ct)
                                 + await db.UserPhones.AsNoTracking().CountAsync(p => p.AppUserId == userId, ct)
                                 + await db.UserLinks.AsNoTracking().CountAsync(l => l.AppUserId == userId, ct),
            ExternalLogins:        await db.UserLogins.AsNoTracking().CountAsync(l => l.UserId == userId, ct),

            CaseNotes:            caseNotes,
            TimelineEntries:      timeline,
            GroupMessages:        groupMessages,
            GroupFieldSessions:   groupSessions,
            EventEvidence:        evidence,
            OtherAuthoredRecords: 0,

            RowWillSurvive:    true,
            OwnedOrganizations: owned,
            PaidSubscriptions:  paid,
            Refusal:            refusal);

        // Everything else that would still name them once the deletes above have run, counted from
        // the model rather than from this method's imagination — and it is also what decides
        // whether the row can go at all.
        var (residual, itemised) = await ResidualReferencesAsync(db, userId, personalSessionIds, ct);
        var named = caseNotes + timeline + groupMessages + groupSessions + evidence;

        return counted with
        {
            OtherAuthoredRecords = Math.Max(0, residual - named),
            RowWillSurvive       = residual > 0,
        };
    }

    /// <summary>Does it.</summary>
    /// <remarks>
    /// <paramref name="confirmation"/> is the display name typed by the SuperAdmin, checked here as
    /// well as in the UI: the screen's job is to make an accident hard, and the server's is to make
    /// one impossible.
    /// </remarks>
    /// <param name="userId">The account to delete.</param>
    /// <param name="confirmation">The display name, typed to confirm.</param>
    /// <param name="actingUserId">The SuperAdmin doing it, for the audit line.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<(AppUserPurgeResult? Result, string? Error)> PurgeAsync(
        Guid userId, string confirmation, Guid actingUserId, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(userId, ct);
        if (preview is null) return (null, "That account no longer exists.");
        if (preview.Refusal is not null) return (null, preview.Refusal);

        if (!string.Equals(confirmation?.Trim(), preview.DisplayName, StringComparison.Ordinal))
            return (null, $"Type “{preview.DisplayName}” exactly to confirm.");

        if (userId == actingUserId)
            return (null, "Delete your own account from your profile, not from here.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return (null, "That account no longer exists.");

        var displayName = user.DisplayName ?? user.Email ?? userId.ToString();
        var personalSessionIds = await PersonalSessionIdsAsync(db, userId, ct);
        var fileIds = await StoredFileIdsAsync(db, userId, personalSessionIds, ct);

        // The paths are read BEFORE the rows go. Afterwards nothing remembers where the bytes
        // were, and orphaned files on disk are the one part of this nobody can clean up later.
        var paths = await db.UploadFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id) && f.StoragePath != null && f.StoragePath != "")
            .Select(f => f.StoragePath!)
            .ToListAsync(ct);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // ── the sessions that were only ever theirs ───────────────────────
            // Share links before the file rows, for the reason item 207's purge gives: the link's
            // key to a single recording is NoAction.
            await db.FieldSessionShareLinks
                .Where(l => personalSessionIds.Contains(l.FieldSessionUploadId)).ExecuteDeleteAsync(ct);
            await db.FieldSessionUploadFiles
                .Where(f => personalSessionIds.Contains(f.FieldSessionUploadId)).ExecuteDeleteAsync(ct);
            await db.FieldSessionUploads
                .Where(s => personalSessionIds.Contains(s.Id)).ExecuteDeleteAsync(ct);

            // ── what nobody else has a claim on ───────────────────────────────
            await db.SignInEvents.Where(e => e.AppUserId == userId).ExecuteDeleteAsync(ct);
            await db.UserTourStates.Where(t => t.AppUserId == userId).ExecuteDeleteAsync(ct);
            await db.UserMessageTos.Where(m => m.ToAppUserId == userId).ExecuteDeleteAsync(ct);
            await db.UserFollows
                .Where(f => f.FollowerAppUserId == userId || f.FollowedAppUserId == userId).ExecuteDeleteAsync(ct);
            await db.UserBlocks
                .Where(b => b.BlockerAppUserId == userId || b.BlockedAppUserId == userId).ExecuteDeleteAsync(ct);
            await db.OrganizationMembershipRequests.Where(r => r.AppUserId == userId).ExecuteDeleteAsync(ct);
            await db.OrganizationAccessGrants.Where(g => g.AppUserId == userId).ExecuteDeleteAsync(ct);
            await db.OrganizationUserMemberships.Where(m => m.AppUserId == userId).ExecuteDeleteAsync(ct);

            // ── the person ────────────────────────────────────────────────────
            // Shared with self-service closure rather than restated. Two copies of these rules
            // would drift, and the one that drifts leaves a credential behind.
            await AccountClosureService.AnonymiseAsync(db, user, ct);

            await transaction.CommitAsync(ct);
        }

        // ── the files, afterwards and on their own ────────────────────────────
        // Two dozen tables point at UploadFiles and several do so with NoAction, so one file
        // another feature still holds must not take the whole transaction down with it.
        var filesRemoved = 0;
        foreach (var fileId in fileIds)
        {
            try { filesRemoved += await db.UploadFiles.Where(f => f.Id == fileId).ExecuteDeleteAsync(ct); }
            catch (DbUpdateException) { /* still referenced elsewhere; the row stays and so do its bytes */ }
        }

        foreach (var path in paths)
        {
            try { await _fileStorage.DeleteAsync(path, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Could not remove {Path} while deleting an account", path); }
        }

        // ── and finally the row itself, if anything is left pointing at it ────
        var rowRemoved = false;
        var (residual, itemised) = await ResidualReferencesAsync(db, userId, [], ct);
        if (residual == 0)
        {
            try
            {
                await db.AppUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
                rowRemoved = true;
            }
            catch (DbUpdateException ex)
            {
                // The census said nothing pointed at it and the database disagreed. The account is
                // already anonymised, so this is a worse outcome than intended rather than a
                // broken one — but it means the census has a gap, and that is worth a log line
                // somebody will actually find.
                _log.LogWarning(ex,
                    "Account {UserId} was anonymised but its row could not be removed, "
                  + "though the reference census found nothing pointing at it", userId);
            }
        }
        else
        {
            _log.LogInformation(
                "Account {UserId} was anonymised; its row survives because {Count} record(s) still "
              + "refer to it: {Tables}", userId, residual, string.Join(", ", itemised.Take(10)));
        }

        _log.LogInformation(
            "SuperAdmin {ActingUserId} deleted account {UserId} (row removed: {RowRemoved})",
            actingUserId, userId, rowRemoved);

        return (new AppUserPurgeResult(
            userId, displayName, rowRemoved,
            preview.PersonalFieldSessions, filesRemoved, preview.Memberships,
            preview.SignInEvents, preview.MessagesReceived, preview.FollowsAndBlocks), null);
    }

    // ── the rules, each in one place ──────────────────────────────────────────

    /// <summary>
    /// The only thing that stops this outright.
    /// </summary>
    /// <remarks>
    /// Ben chose one refusal and only one (2026-09-04). Owning a group is a notice rather than a
    /// bar — a SuperAdmin can appoint a new owner afterwards — but there is no afterwards for a
    /// platform with nobody able to administer it.
    /// </remarks>
    private static async Task<string?> LastSuperAdminRefusalAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
    {
        var superAdminRoleIds = await db.Roles.AsNoTracking()
            .Where(r => r.Name == Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            .Select(r => r.Id).ToListAsync(ct);
        if (superAdminRoleIds.Count == 0) return null;

        var isSuperAdmin = await db.UserRoles.AsNoTracking()
            .AnyAsync(r => r.UserId == userId && superAdminRoleIds.Contains(r.RoleId), ct);
        if (!isSuperAdmin) return null;

        var others = await db.UserRoles.AsNoTracking()
            .Where(r => superAdminRoleIds.Contains(r.RoleId) && r.UserId != userId)
            .Select(r => r.UserId)
            .Distinct()
            .CountAsync(ct);

        return others > 0
            ? null
            : "This is the only SuperAdmin account. Make somebody else a SuperAdmin first — "
            + "there is no way back from a platform nobody can administer.";
    }

    /// <summary>Sessions that were never part of anybody's investigation.</summary>
    /// <remarks>
    /// The <c>InvestigationId == null</c> half is the whole rule. A session recorded FOR a group is
    /// the group's evidence and outlives whoever carried the phone; one recorded on somebody's own
    /// walk-through is theirs alone.
    /// </remarks>
    private static async Task<List<Guid>> PersonalSessionIdsAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
        => await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.SubmittedByAppUserId == userId && s.InvestigationId == null)
            .Select(s => s.Id)
            .ToListAsync(ct);

    /// <summary>The stored files those sessions were made of, plus the person's own uploads.</summary>
    private static async Task<List<Guid>> StoredFileIdsAsync(
        BenDataContext db, Guid userId, List<Guid> personalSessionIds, CancellationToken ct)
    {
        var ids = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => personalSessionIds.Contains(s.Id))
            .Select(s => s.DocumentUploadFileId)
            .ToListAsync(ct);

        ids.AddRange(await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => personalSessionIds.Contains(f.FieldSessionUploadId))
            .Select(f => f.UploadFileId)
            .ToListAsync(ct));

        return ids.Distinct().ToList();
    }

    /// <summary>
    /// How many records would still name this account once the deletes above have run.
    /// </summary>
    /// <remarks>
    /// <para><b>Asked of the model, not of a list.</b> Every foreign key whose principal is
    /// <c>AppUsers</c> is enumerated from <c>db.Model</c> and counted with a parameterised query.
    /// Table and column names come from EF, never from anything a caller sent, so there is nothing
    /// here to inject into.</para>
    ///
    /// <para>335 counts is a lot of round trips, and this is a SuperAdmin screen somebody opens a
    /// handful of times a year. A hand-kept list would be faster and would be wrong within a
    /// release — which is exactly how the organization purge came to be refused by the database
    /// twice in front of Ben.</para>
    ///
    /// <para><paramref name="pendingSessionIds"/> are sessions the purge is about to delete, so
    /// their references must not be counted as reasons the row has to survive. Passed empty after
    /// the deletes have actually happened.</para>
    /// </remarks>
    private static async Task<(int Total, List<string> Itemised)> ResidualReferencesAsync(
        BenDataContext db, Guid userId, List<Guid> pendingSessionIds, CancellationToken ct)
    {
        // Tables this purge empties of the user before the row delete is attempted. Counting them
        // would make every account look permanent. Kept in step with the deletes above by
        // AppUserPurgeCoverageTests, which fails in both directions.
        var sweptEntities = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SignInEvent), nameof(UserTourState), nameof(UserMessageTo), nameof(UserFollow),
            nameof(UserBlock), nameof(OrganizationMembershipRequest), nameof(OrganizationAccessGrant),
            nameof(OrganizationUserMembership), nameof(UserAddress), nameof(UserEmail),
            nameof(UserPhone), nameof(UserLink), nameof(AppUserPhoto), nameof(FieldSessionUpload),
            nameof(FieldSessionUploadFile), nameof(FieldSessionShareLink), nameof(UploadFile),
        };

        var total = 0;
        var itemised = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            // Identity's own join tables are cleared by the anonymise step; their CLR names are
            // generic-arity spellings that no nameof() above can match.
            if (entity.ClrType.Name.StartsWith("IdentityUser", StringComparison.Ordinal)) continue;
            if (sweptEntities.Contains(entity.ClrType.Name)) continue;

            foreach (var fk in entity.GetForeignKeys())
            {
                if (fk.PrincipalEntityType.ClrType != typeof(AppUser)) continue;
                // Composite references are not counted, and there are none. A guard test fails on
                // the day somebody adds one, rather than this quietly under-counting.
                if (fk.Properties.Count != 1) continue;

                var count = await CountReferencesAsync(
                    db, entity.ClrType, fk.Properties[0].Name, userId, ct);
                if (count <= 0) continue;

                total += count;
                itemised.Add($"{entity.ClrType.Name}.{fk.Properties[0].Name} ({count})");
            }
        }

        // Sessions that are about to go do not count as reasons to keep the row.
        if (pendingSessionIds.Count > 0)
        {
            var pending = await db.FieldSessionUploads.AsNoTracking()
                .CountAsync(s => pendingSessionIds.Contains(s.Id), ct);
            total = Math.Max(0, total - pending);
        }

        return (total, itemised);
    }

    /// <summary>
    /// How many rows of one entity name this account through one property.
    /// </summary>
    /// <remarks>
    /// <para><b>LINQ rather than SQL, and that is not a style choice.</b> The first version issued
    /// a parameterised <c>COUNT</c> per foreign key, which is faster and cannot run on the
    /// in-memory provider at all — so every test of the preview died inside the census before
    /// reaching a single decision. A census only exercisable against SQL Server is a census
    /// nothing checks.</para>
    ///
    /// <para>Reflection because the entity type is only known at run time; <c>EF.Property</c>
    /// because the column is too. Both providers translate it.</para>
    /// </remarks>
    private static Task<int> CountReferencesAsync(
        BenDataContext db, Type clrType, string property, Guid userId, CancellationToken ct)
        => (Task<int>)CountForMethod.MakeGenericMethod(clrType)
            .Invoke(null, [db, property, userId, ct])!;

    private static readonly System.Reflection.MethodInfo CountForMethod =
        typeof(AppUserPurge).GetMethod(nameof(CountForAsync),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static async Task<int> CountForAsync<TEntity>(
        BenDataContext db, string property, Guid userId, CancellationToken ct)
        where TEntity : class
        => await db.Set<TEntity>().AsNoTracking()
            .CountAsync(e => EF.Property<Guid?>(e, property) == userId, ct);
}
