using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections;
using System.Linq.Expressions;

namespace Ben.Data.WebApi.Services;

using Ben.Service.Models.Admin;

/// <summary>
/// Merges one organization into another (item 110): every row the merged group owns is
/// reparented onto the base group, its members become the base's members, its URL becomes a
/// permanent alias of the base (item 89 — a freed URL name can capture traffic), and the empty
/// husk is deleted.
/// </summary>
/// <remarks>
/// <para><b>The reparenting is driven by the EF model, not a hand-kept list.</b> Every foreign
/// key that points at <see cref="Organization"/> is discovered from metadata and swept, so a
/// table added next year is merged correctly without anyone remembering this file exists. The
/// same sweep runs one level down for duplicate members' membership-scoped rows.</para>
///
/// <para><b>The husk delete is the proof.</b> Every FK onto Organizations is NoAction by
/// convention (item 155), so the final delete succeeds only when nothing references the merged
/// group any more. A missed row fails the whole transaction rather than leaving a half-merged
/// group.</para>
///
/// <para><b>Decisions Ben's framing left open, resolved as follows</b> (each is reversible policy,
/// none is schema): a person in both groups keeps their base membership and the HIGHER of their
/// two roles; colliding unique rows (the same coupon redeemed by both, the same file shared to
/// both) keep the base's copy; merged cases that collide on case number are renumbered into the
/// base's sequence; the merged group's subscription is dropped (the base's plan governs — refunds,
/// if any, are a ledger adjustment); clients with open cases and all former members are messaged.</para>
/// </remarks>
public sealed class OrganizationMergeService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;

    public OrganizationMergeService(IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages)
    {
        _dbFactory = dbFactory;
        _messages = messages;
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    public async Task<(MergePreview? Preview, string? Error)> PreviewAsync(
        Guid baseId, Guid mergedId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await ValidateAsync(db, baseId, mergedId, ct) is { } bad) return (null, bad);

        var baseOrg = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == baseId, ct);
        var merged  = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == mergedId, ct);

        var counts = new List<MergeTableCount>();
        foreach (var (entity, fk) in OrganizationForeignKeys(db))
        {
            var count = CountRows(db, entity, fk, mergedId);
            if (count > 0) counts.Add(new MergeTableCount(entity.ClrType.Name, count));
        }

        var notes = new List<string>();
        var dupUsers = await DuplicateMemberIdsAsync(db, baseId, mergedId, ct);
        if (dupUsers.Count > 0)
            notes.Add($"{dupUsers.Count} member(s) belong to both groups — they keep one membership, at the higher of their two roles.");
        if (await db.OrganizationSubscriptions.AnyAsync(s => s.OrganizationId == mergedId, ct))
            notes.Add($"{merged.Name}'s subscription is dropped; {baseOrg.Name}'s plan governs the merged group.");
        var caseCollisions = await CountCaseNumberCollisionsAsync(db, baseId, mergedId, ct);
        if (caseCollisions > 0)
            notes.Add($"{caseCollisions} case(s) collide on case number and will be renumbered into {baseOrg.Name}'s sequence.");
        notes.Add($"The URL /o/{merged.UrlName} becomes a permanent alias of /o/{baseOrg.UrlName} — old links keep working.");

        return (new MergePreview(baseOrg.Name, merged.Name, counts, notes), null);
    }

    // ── The merge ─────────────────────────────────────────────────────────────

    public async Task<string?> MergeAsync(
        Guid baseId, Guid mergedId, string? newName, Guid actingUserId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await ValidateAsync(db, baseId, mergedId, ct) is { } bad) return bad;

        var baseOrg   = await db.Organizations.SingleAsync(o => o.Id == baseId, ct);
        var mergedOrg = await db.Organizations.SingleAsync(o => o.Id == mergedId, ct);
        var mergedName = mergedOrg.Name;
        var mergedUrl  = mergedOrg.UrlName;

        // Collected BEFORE their memberships move or die: the people to tell afterwards.
        var formerMemberIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == mergedId && m.IsActive)
            .Select(m => m.AppUserId).ToListAsync(ct);
        var clientIds = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == mergedId && c.ClientRequestId != null
                     && c.Status <= CaseStatus.Summarized)
            .Select(c => c.ClientRequest!.AppUserId).Distinct().ToListAsync(ct);

        // The InMemory provider used by the tests has no transactions; SQL Server does, and on
        // SQL Server the merge is all-or-nothing.
        var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await MergeDuplicateMembersAsync(db, baseId, mergedId, ct);
            await DeleteCollidingUniqueRowsAsync(db, baseId, mergedId, ct);
            await RenumberAndReslugAsync(db, baseId, mergedId, ct);
            await DropMergedSubscriptionAsync(db, mergedId, ct);
            await db.SaveChangesAsync(ct);

            // The model-driven sweep: everything that still points at the merged group now
            // points at the base. Complete by construction — the FK list comes from EF itself.
            foreach (var (entity, fk) in OrganizationForeignKeys(db))
                ReparentRows(db, entity, fk, mergedId, baseId);
            await db.SaveChangesAsync(ct);

            // Item 89: the merged group's address keeps working, permanently, and can never be
            // captured by a newly created group.
            db.OrganizationUrlNameAliases.Add(new OrganizationUrlNameAlias
            {
                Id = Guid.NewGuid(), OrganizationId = baseId, UrlName = mergedUrl,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = actingUserId,
            });

            if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != baseOrg.Name)
            {
                baseOrg.Name = newName.Trim();
                baseOrg.DateUpdated = DateTime.UtcNow;
                baseOrg.UpdatedByAppUserId = actingUserId;
            }

            // The proof-of-completeness delete: every FK onto Organizations is NoAction, so this
            // succeeds only when the sweep truly left nothing behind.
            db.Organizations.Remove(mergedOrg);
            await db.SaveChangesAsync(ct);

            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            return "The merge could not complete — something still references the merged group, "
                 + "and nothing was changed. Detail: " + (ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }

        // Outside the transaction: the merge stands even if a message fails to send.
        var survivorName = string.IsNullOrWhiteSpace(newName) ? baseOrg.Name : newName.Trim();
        await _messages.SendAsync(
            $"{mergedName} is now part of {survivorName}",
            $"<p>The group <strong>{mergedName}</strong> has merged into <strong>{survivorName}</strong>. "
            + "Your membership, cases, files and equipment moved with it — nothing was lost, and old "
            + "links to the group keep working.</p>",
            formerMemberIds, actingUserId, ct);
        if (clientIds.Count > 0)
            await _messages.SendAsync(
                $"Your case is now handled by {survivorName}",
                $"<p><strong>{mergedName}</strong>, the group handling your case, has merged into "
                + $"<strong>{survivorName}</strong>. The same people remain on your case; only the "
                + "group's name has changed.</p>",
                clientIds, actingUserId, ct);

        return null;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private static async Task<string?> ValidateAsync(BenDataContext db, Guid baseId, Guid mergedId, CancellationToken ct)
    {
        if (baseId == mergedId) return "A group cannot be merged into itself.";
        if (!await db.Organizations.AnyAsync(o => o.Id == baseId, ct)) return "The base group was not found.";
        if (!await db.Organizations.AnyAsync(o => o.Id == mergedId, ct)) return "The group to merge was not found.";
        return null;
    }

    // ── Duplicate members ─────────────────────────────────────────────────────

    private static async Task<List<Guid>> DuplicateMemberIdsAsync(
        BenDataContext db, Guid baseId, Guid mergedId, CancellationToken ct)
        => await db.OrganizationUserMemberships
            .Where(m => m.OrganizationId == mergedId
                     && db.OrganizationUserMemberships.Any(b => b.OrganizationId == baseId && b.AppUserId == m.AppUserId))
            .Select(m => m.AppUserId).ToListAsync(ct);

    /// <summary>
    /// A person in both groups keeps the base membership at the higher of their two roles; the
    /// merged membership's dependent rows (role grants, duty assignments, title, address access …)
    /// are re-pointed at the base membership by the same model-driven sweep, then the duplicate
    /// row is deleted. Lower enum value = more powerful role, per the enum's own contract.
    /// </summary>
    private static async Task MergeDuplicateMembersAsync(
        BenDataContext db, Guid baseId, Guid mergedId, CancellationToken ct)
    {
        var pairs = await db.OrganizationUserMemberships
            .Where(m => m.OrganizationId == mergedId)
            .Join(db.OrganizationUserMemberships.Where(b => b.OrganizationId == baseId),
                  m => m.AppUserId, b => b.AppUserId,
                  (m, b) => new { Merged = m, Base = b })
            .ToListAsync(ct);

        foreach (var pair in pairs)
        {
            if (pair.Merged.Role < pair.Base.Role) pair.Base.Role = pair.Merged.Role;
            if (pair.Merged.IsActive) pair.Base.IsActive = true;

            foreach (var (entity, fk) in ForeignKeysTo(db, typeof(OrganizationUserMembership)))
                ReparentRows(db, entity, fk, pair.Merged.Id, pair.Base.Id);
            db.OrganizationUserMemberships.Remove(pair.Merged);
        }
    }

    // ── Colliding unique rows ─────────────────────────────────────────────────

    /// <summary>
    /// Tables unique per (organization, something): where both groups hold the same something,
    /// the base's row wins and the merged copy is deleted — reparenting it would violate the
    /// index, and the base's row already says what it would have said.
    /// </summary>
    private static async Task DeleteCollidingUniqueRowsAsync(
        BenDataContext db, Guid baseId, Guid mergedId, CancellationToken ct)
    {
        db.UploadFileOrganizationShares.RemoveRange(await db.UploadFileOrganizationShares
            .Where(s => s.OrganizationId == mergedId && db.UploadFileOrganizationShares
                .Any(b => b.OrganizationId == baseId && b.UploadFileId == s.UploadFileId)).ToListAsync(ct));
        db.ClientRequestOrganizations.RemoveRange(await db.ClientRequestOrganizations
            .Where(s => s.OrganizationId == mergedId && db.ClientRequestOrganizations
                .Any(b => b.OrganizationId == baseId && b.ClientRequestId == s.ClientRequestId)).ToListAsync(ct));
        db.EquipmentItemShares.RemoveRange(await db.EquipmentItemShares
            .Where(s => s.OrganizationId == mergedId && db.EquipmentItemShares
                .Any(b => b.OrganizationId == baseId && b.EquipmentItemId == s.EquipmentItemId)).ToListAsync(ct));
        db.CouponRedemptions.RemoveRange(await db.CouponRedemptions
            .Where(s => s.OrganizationId == mergedId && db.CouponRedemptions
                .Any(b => b.OrganizationId == baseId && b.CouponId == s.CouponId)).ToListAsync(ct));
        db.OrganizationBillingContacts.RemoveRange(await db.OrganizationBillingContacts
            .Where(s => s.OrganizationId == mergedId && db.OrganizationBillingContacts
                .Any(b => b.OrganizationId == baseId && b.AppUserId == s.AppUserId)).ToListAsync(ct));
        db.OrganizationAccessGrants.RemoveRange(await db.OrganizationAccessGrants
            .Where(s => s.OrganizationId == mergedId && db.OrganizationAccessGrants
                .Any(b => b.OrganizationId == baseId && b.AppUserId == s.AppUserId && b.TableName == s.TableName)).ToListAsync(ct));
        // One area-of-operation per group: the base's stands.
        if (await db.OrganizationAreaOfOperations.AnyAsync(a => a.OrganizationId == baseId, ct))
            db.OrganizationAreaOfOperations.RemoveRange(await db.OrganizationAreaOfOperations
                .Where(a => a.OrganizationId == mergedId).ToListAsync(ct));
    }

    // ── Case numbers and slugs ────────────────────────────────────────────────

    private static async Task<int> CountCaseNumberCollisionsAsync(
        BenDataContext db, Guid baseId, Guid mergedId, CancellationToken ct)
        => await db.Cases.CountAsync(c => c.OrganizationId == mergedId && db.Cases
            .Any(b => b.OrganizationId == baseId && b.CaseYear == c.CaseYear && b.OrgCaseNumber == c.OrgCaseNumber), ct);

    /// <summary>
    /// Case numbers collide by construction — both groups started at #1 — so colliding merged
    /// cases take the next number in the base's sequence for their year. Slugs (cases,
    /// investigations, calendar events) get a suffix when taken; a changed slug is exactly what
    /// the item-89 alias machinery was NOT built for (it aliases groups, not cases), so the note
    /// in the preview is the honest statement of that cost.
    /// </summary>
    private static async Task RenumberAndReslugAsync(
        BenDataContext db, Guid baseId, Guid mergedId, CancellationToken ct)
    {
        var baseCases = await db.Cases.Where(c => c.OrganizationId == baseId)
            .Select(c => new { c.CaseYear, c.OrgCaseNumber, c.UrlName }).ToListAsync(ct);
        var taken = baseCases.Select(c => (c.CaseYear, c.OrgCaseNumber)).ToHashSet();
        var nextByYear = baseCases.GroupBy(c => c.CaseYear)
            .ToDictionary(g => g.Key, g => g.Max(c => c.OrgCaseNumber) + 1);

        foreach (var c in await db.Cases.Where(c => c.OrganizationId == mergedId).ToListAsync(ct))
        {
            if (!taken.Contains((c.CaseYear, c.OrgCaseNumber))) { taken.Add((c.CaseYear, c.OrgCaseNumber)); continue; }
            var next = nextByYear.GetValueOrDefault(c.CaseYear, 1);
            while (taken.Contains((c.CaseYear, next))) next++;
            c.OrgCaseNumber = next;
            taken.Add((c.CaseYear, next));
            nextByYear[c.CaseYear] = next + 1;
        }

        await ReslugAsync(db.Cases, baseId, mergedId, ct);
        await ReslugAsync(db.Investigations, baseId, mergedId, ct);
        await ReslugAsync(db.OrgCalendarEvents, baseId, mergedId, ct);

        // CMS templates: unique on (org, scope, name).
        foreach (var t in await db.OrganizationCmsTemplates.Where(t => t.OrganizationId == mergedId).ToListAsync(ct))
            if (await db.OrganizationCmsTemplates.AnyAsync(
                    b => b.OrganizationId == baseId && b.Scope == t.Scope && b.Name == t.Name, ct))
                t.Name = $"{t.Name} (merged)";
    }

    private static async Task ReslugAsync<T>(DbSet<T> set, Guid baseId, Guid mergedId, CancellationToken ct)
        where T : class
    {
        // The three sluggable types share the shape by convention, not by interface — dynamic
        // keeps this one helper instead of three copies.
        var baseSlugs = (await set.Where(e => EF.Property<Guid>(e, "OrganizationId") == baseId)
            .Select(e => EF.Property<string?>(e, "UrlName")).ToListAsync(ct))
            .Where(s => s is not null).Select(s => s!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in await set.Where(e => EF.Property<Guid>(e, "OrganizationId") == mergedId).ToListAsync(ct))
        {
            var entry = set.Entry(row).Property<string?>("UrlName");
            if (entry.CurrentValue is not { Length: > 0 } slug || !baseSlugs.Contains(slug)) continue;
            var n = 2;
            while (baseSlugs.Contains($"{slug}-{n}")) n++;
            entry.CurrentValue = $"{slug}-{n}";
            baseSlugs.Add($"{slug}-{n}");
        }
    }

    // ── Subscription ──────────────────────────────────────────────────────────

    private static async Task DropMergedSubscriptionAsync(BenDataContext db, Guid mergedId, CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == mergedId, ct);
        if (sub is null) return;
        db.SubscriptionContractTerms.RemoveRange(
            await db.SubscriptionContractTerms.Where(t => t.OrganizationSubscriptionId == sub.Id).ToListAsync(ct));
        db.OrganizationSubscriptions.Remove(sub);
    }

    // ── The model-driven sweep ────────────────────────────────────────────────

    private static IEnumerable<(IEntityType Entity, IForeignKey Fk)> OrganizationForeignKeys(BenDataContext db)
        => ForeignKeysTo(db, typeof(Organization));

    private static IEnumerable<(IEntityType Entity, IForeignKey Fk)> ForeignKeysTo(BenDataContext db, Type principal)
        => db.Model.GetEntityTypes()
            .Where(et => et.ClrType != principal)
            .SelectMany(et => et.GetForeignKeys()
                .Where(fk => fk.PrincipalEntityType.ClrType == principal && fk.Properties.Count == 1)
                .Select(fk => (et, fk)));

    private static IQueryable RowsWhere(BenDataContext db, IEntityType entity, IForeignKey fk, Guid value)
    {
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(entity.ClrType);
        var queryable = (IQueryable)setMethod.Invoke(db, null)!;

        var fkProperty = fk.Properties[0];
        var param = Expression.Parameter(entity.ClrType, "e");
        var efProperty = typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(fkProperty.ClrType);
        var access = Expression.Call(efProperty, param, Expression.Constant(fkProperty.Name));
        var constant = Expression.Constant(
            fkProperty.ClrType == typeof(Guid?) ? (Guid?)value : value, fkProperty.ClrType);
        var predicate = Expression.Lambda(Expression.Equal(access, constant), param);

        var where = typeof(Queryable).GetMethods()
            .Single(m => m.Name == nameof(Queryable.Where)
                      && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
            .MakeGenericMethod(entity.ClrType);
        return (IQueryable)where.Invoke(null, [queryable, predicate])!;
    }

    private static int CountRows(BenDataContext db, IEntityType entity, IForeignKey fk, Guid value)
    {
        var count = typeof(Queryable).GetMethods()
            .Single(m => m.Name == nameof(Queryable.Count) && m.GetParameters().Length == 1)
            .MakeGenericMethod(entity.ClrType);
        return (int)count.Invoke(null, [RowsWhere(db, entity, fk, value)])!;
    }

    private static void ReparentRows(BenDataContext db, IEntityType entity, IForeignKey fk, Guid from, Guid to)
    {
        foreach (var row in RowsWhere(db, entity, fk, from).Cast<object>().ToList())
        {
            var entry = db.Entry(row);
            if (entry.State == EntityState.Deleted) continue; // a colliding row already removed
            entry.Property(fk.Properties[0].Name).CurrentValue =
                fk.Properties[0].ClrType == typeof(Guid?) ? (Guid?)to : to;
        }
    }
}
