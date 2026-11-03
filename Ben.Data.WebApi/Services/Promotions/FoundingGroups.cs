using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Promotions;

/// <summary>
/// The founding groups promotion's rules, in one place (Ben, 11/02/2026). See <see cref="Promotion"/> for the rules
/// themselves; every screen and job asks here, so none of them can come to disagree.
/// </summary>
public static class FoundingGroups
{
    public const string Key = "founding-groups";

    /// <summary>The promotion, created switched off the first time anything asks for it.</summary>
    public static async Task<Promotion> GetAsync(BenDataContext db, CancellationToken ct)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Key == Key, ct);
        if (promotion is not null) return promotion;

        promotion = new Promotion
        {
            Id = Guid.NewGuid(), Key = Key, Name = "Founding groups",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty,
        };
        db.Promotions.Add(promotion);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two first askers at once: the other one's row stands.
            db.Entry(promotion).State = EntityState.Detached;
            promotion = await db.Promotions.FirstAsync(p => p.Key == Key, ct);
        }
        return promotion;
    }

    /// <summary>When claims stop: the window's end. Null while it has never been opened.</summary>
    public static DateTime? ClaimsCloseUtc(Promotion p)
        => p.OpenedUtc?.AddDays(p.ClaimWindowDays);

    /// <summary>Spots held: groups gathering their members, and groups that qualified.</summary>
    public static Task<int> SpotsTakenAsync(BenDataContext db, Promotion p, CancellationToken ct)
        => db.PromotionEntries.CountAsync(e => e.PromotionId == p.Id
            && (e.State == PromotionEntryState.Gathering || e.State == PromotionEntryState.Qualified), ct);

    /// <summary>Whether a group may claim a spot right now, as far as the promotion goes.</summary>
    public static bool ClaimsOpen(Promotion p, int spotsTaken, DateTime now)
        => p.IsOpen && p.OpenedUtc is not null && now < ClaimsCloseUtc(p) && spotsTaken < p.Spots;

    /// <summary>Why this group can't claim a spot now, in words for the page; null when it can.</summary>
    public static async Task<string?> WhyCantClaimAsync(
        BenDataContext db, Promotion p, Guid organizationId, Guid userId, DateTime now, CancellationToken ct)
    {
        if (!p.IsOpen || p.OpenedUtc is null) return "The founding groups promotion isn't open.";
        if (now >= ClaimsCloseUtc(p)) return "Claims for the founding groups have closed.";
        if (await SpotsTakenAsync(db, p, ct) >= p.Spots) return "All the founding group spots have been taken.";

        var org = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId).Select(o => new { o.Name, o.Kind }).FirstOrDefaultAsync(ct);
        if (org is null) return "That group couldn't be found.";
        if (SubscriptionTierResolver.IsBusinessKind(org.Kind))
            return "Tour businesses have a plan of their own, so they can't take part.";

        if (await db.PromotionEntries.AnyAsync(e => e.PromotionId == p.Id && e.OrganizationId == organizationId, ct))
            return "This group has already taken part.";

        if (await IsSiteAdministratorAsync(db, userId, ct))
            return "Site administrators' groups can't take part.";

        var paying = await db.OrganizationSubscriptions.AsNoTracking()
            .AnyAsync(s => s.OrganizationId == organizationId && s.Status == SubscriptionStatus.Active
                        && s.PriceAtPeriodStart > 0 && s.CurrentPeriodEnd > now, ct);
        if (paying) return "This group already pays for a plan, so it can't take a free one.";

        if (await CountedElsewhereAsync(db, p, userId, organizationId, ct) is { } elsewhere)
            return $"You're already counted for {elsewhere} in the promotion, so you can't start a second free group.";

        return null;
    }

    /// <summary>Takes a spot for the group and starts its week. The caller has checked <see cref="WhyCantClaimAsync"/>.</summary>
    public static PromotionEntry Claim(BenDataContext db, Promotion p, Guid organizationId, Guid userId, DateTime now)
    {
        var entry = new PromotionEntry
        {
            Id = Guid.NewGuid(), PromotionId = p.Id, OrganizationId = organizationId,
            State = PromotionEntryState.Gathering, ClaimedUtc = now, ClaimedByAppUserId = userId,
            GatherEndsUtc = now.AddDays(p.GatherDays),
            DateCreated = now, CreatedByAppUserId = userId,
        };
        db.PromotionEntries.Add(entry);
        return entry;
    }

    // ── who counts ────────────────────────────────────────────────────────────

    /// <summary>One member of a group in the promotion, and whether they count.</summary>
    public sealed record MemberStanding(Guid AppUserId, string Name, bool Counts, string? WhyNot);

    /// <summary>
    /// Every active member of the entry's group: counted, or why not. A person counts once their email is
    /// confirmed, when they're in no other group, and when nobody else in the promotion has counted them.
    /// </summary>
    public static async Task<List<MemberStanding>> MembersAsync(
        BenDataContext db, PromotionEntry entry, CancellationToken ct)
    {
        var members = await (
            from m in db.OrganizationUserMemberships.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.AppUserId equals u.Id
            where m.OrganizationId == entry.OrganizationId && m.IsActive
            select new
            {
                m.AppUserId,
                Name = u.DisplayName ?? u.UserName ?? "A member",
                u.EmailConfirmed,
                m.DateCreated,
                OtherGroups = db.OrganizationUserMemberships.Count(o => o.AppUserId == m.AppUserId && o.IsActive
                                                                      && o.OrganizationId != entry.OrganizationId),
                CountedHere = db.PromotionMembers.Any(c => c.PromotionEntryId == entry.Id && c.AppUserId == m.AppUserId),
                CountedElsewhere = db.PromotionMembers.Any(c => c.PromotionId == entry.PromotionId
                                                             && c.PromotionEntryId != entry.Id && c.AppUserId == m.AppUserId),
            })
            .OrderBy(m => m.DateCreated)
            .ToListAsync(ct);

        return [.. members.Select(m => new MemberStanding(m.AppUserId, m.Name, m.CountedHere,
            m.CountedHere ? null
            : m.CountedElsewhere ? "is counted for another group in the promotion"
            : !m.EmailConfirmed ? "hasn't confirmed their email address yet"
            : m.OtherGroups > 0 ? "is also a member of another group"
            : entry.State != PromotionEntryState.Gathering ? "joined after the week ended"
            : null))];
    }

    /// <summary>
    /// Counts every member who now qualifies, during the entry's week. Run by the job every few minutes, so a
    /// member is counted soon after they join and confirm; somebody counted stays counted. Returns how many were added.
    /// </summary>
    public static async Task<int> CountMembersAsync(BenDataContext db, PromotionEntry entry, DateTime now, CancellationToken ct)
    {
        if (entry.State != PromotionEntryState.Gathering || now > entry.GatherEndsUtc) return 0;

        var added = 0;
        foreach (var m in await MembersAsync(db, entry, ct))
        {
            if (m.Counts || m.WhyNot is not null) continue;
            db.PromotionMembers.Add(new PromotionMember
            {
                Id = Guid.NewGuid(), PromotionId = entry.PromotionId, PromotionEntryId = entry.Id,
                AppUserId = m.AppUserId, CountedUtc = now,
            });
            try { await db.SaveChangesAsync(ct); added++; }
            catch (DbUpdateException)
            {
                // Another group counted them a moment ago: the database's unique rule settles it.
                db.ChangeTracker.Clear();
            }
        }
        return added;
    }

    /// <summary>
    /// The end of the entry's week. Members who left during it stop counting; enough counted members and the group
    /// qualifies for its free year, otherwise the spot goes back. The caller saves.
    /// </summary>
    public static async Task<bool> EndTheWeekAsync(
        BenDataContext db, Promotion p, PromotionEntry entry, DateTime now, CancellationToken ct)
    {
        var counted = await db.PromotionMembers.Where(c => c.PromotionEntryId == entry.Id).ToListAsync(ct);
        var stillHere = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == entry.OrganizationId && m.IsActive)
            .Select(m => m.AppUserId).ToListAsync(ct);
        var left = counted.Where(c => !stillHere.Contains(c.AppUserId)).ToList();
        db.PromotionMembers.RemoveRange(left);
        var count = counted.Count - left.Count;

        entry.DateUpdated = now;
        if (count < p.MinimumMembers)
        {
            entry.State = PromotionEntryState.Released;
            db.PromotionMembers.RemoveRange(counted.Except(left));
            return false;
        }

        var tier = await TierForAsync(db, entry.OrganizationId, count, ct);
        entry.State = PromotionEntryState.Qualified;
        entry.QualifiedUtc = now;
        entry.FreeYearEndsUtc = now.AddMonths(p.FreeMonths);
        entry.MembersAtQualifying = count;
        entry.SubscriptionTierIdEarned = tier?.Id;

        await OpenFreeYearAsync(db, entry, tier, count, now, ct);
        return true;
    }

    /// <summary>
    /// The free year: an ordinary period on the plan the size earns, priced at nothing. After it the group's plan
    /// renews at its normal price, as any plan does.
    /// </summary>
    private static async Task OpenFreeYearAsync(
        BenDataContext db, PromotionEntry entry, SubscriptionTier? tier, int members, DateTime now, CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == entry.OrganizationId, ct);
        if (sub is null)
        {
            sub = new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = entry.OrganizationId,
                DateCreated = now, CreatedByAppUserId = entry.ClaimedByAppUserId,
            };
            db.OrganizationSubscriptions.Add(sub);
        }
        else { sub.DateUpdated = now; }

        var snapshot = PeriodOpener.Open(sub, tier, SubscriptionStatus.Active, BillingInterval.Monthly,
            entry.QualifiedUtc, entry.FreeYearEndsUtc, members, entry.ClaimedByAppUserId);
        sub.PriceAtPeriodStart = 0m;
        sub.CancelAtPeriodEnd = false;
        if (snapshot is not null)
        {
            snapshot.Price = 0m;
            await PeriodOpener.ReplaceSnapshotAsync(db, sub.Id, snapshot.PeriodStartUtc, ct);
            db.SubscriptionContractTerms.Add(snapshot);
        }
    }

    /// <summary>
    /// Growing for free (Ben: "The group can grow free if it is one of the first 10"): during its free year a group
    /// that has grown into a bigger plan moves onto it, still at nothing. Never moves down. Returns whether it moved;
    /// the caller saves.
    /// </summary>
    public static async Task<bool> GrowAsync(BenDataContext db, PromotionEntry entry, DateTime now, CancellationToken ct)
    {
        if (entry.State != PromotionEntryState.Qualified || entry.FreeYearEndsUtc is not { } ends || now >= ends) return false;

        var sub = await db.OrganizationSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == entry.OrganizationId, ct);
        if (sub is null || sub.Status != SubscriptionStatus.Active || sub.CurrentPeriodStart != entry.QualifiedUtc
            || sub.PriceAtPeriodStart != 0m) return false;

        var members = await ConfirmedMembersAsync(db, entry.OrganizationId, ct);
        var tier = await TierForAsync(db, entry.OrganizationId, members, ct);
        if (tier is null || tier.Id == sub.SubscriptionTierId) return false;

        var current = sub.SubscriptionTierId is { } currentId
            ? await db.SubscriptionTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == currentId, ct)
            : null;
        if (current is not null && tier.MinMembers <= current.MinMembers) return false;

        var periodStart = sub.CurrentPeriodStart!.Value;
        var periodEnd = sub.CurrentPeriodEnd;
        var snapshot = PeriodOpener.Open(sub, tier, SubscriptionStatus.Active, sub.Interval,
            periodStart, periodEnd, members, entry.ClaimedByAppUserId);
        sub.PriceAtPeriodStart = 0m;
        sub.DateUpdated = now;
        if (snapshot is not null)
        {
            snapshot.Price = 0m;
            await PeriodOpener.ReplaceSnapshotAsync(db, sub.Id, periodStart, ct);
            db.SubscriptionContractTerms.Add(snapshot);
        }
        return true;
    }

    /// <summary>Active members whose email is confirmed: the count a plan's size is read from in the promotion.</summary>
    public static Task<int> ConfirmedMembersAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => (from m in db.OrganizationUserMemberships
            join u in db.Users on m.AppUserId equals u.Id
            where m.OrganizationId == organizationId && m.IsActive && u.EmailConfirmed
            select m.Id).CountAsync(ct);

    /// <summary>The plan this many members earns on the live ladder, or null when the ladder is broken.</summary>
    public static async Task<SubscriptionTier?> TierForAsync(BenDataContext db, Guid organizationId, int members, CancellationToken ct)
    {
        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Limits).Include(t => t.Prices).ToListAsync(ct);
        if (SubscriptionTierResolver.Validate(tiers) is not null) return null;
        var kind = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId).Select(o => o.Kind).FirstOrDefaultAsync(ct);
        return SubscriptionTierResolver.Resolve(tiers, members, kind);
    }

    private static async Task<string?> CountedElsewhereAsync(
        BenDataContext db, Promotion p, Guid userId, Guid organizationId, CancellationToken ct)
        => await db.PromotionMembers.AsNoTracking()
            .Where(c => c.PromotionId == p.Id && c.AppUserId == userId && c.PromotionEntry.OrganizationId != organizationId)
            .Select(c => c.PromotionEntry.Organization.Name)
            .FirstOrDefaultAsync(ct);

    private static Task<bool> IsSiteAdministratorAsync(BenDataContext db, Guid userId, CancellationToken ct)
        => (from ur in db.Set<IdentityUserRole<Guid>>()
            join r in db.Set<IdentityRole<Guid>>() on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name == RoleNames.SuperAdmin
            select ur.UserId).AnyAsync(ct);
}
