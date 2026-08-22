using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The platform's price list: the member bands and what each one costs at each cadence.
/// </summary>
/// <remarks>
/// <para><b>Every write revalidates the whole list.</b> The bands must tile the entire member
/// range, and the failure of that rule is silent — delete the 4–10 band and a five-member group is
/// not "unpriced", it simply matches nothing and is billed nothing, which nobody reports. So a save
/// that would break the tiling is refused with the reason, and the refusal reaches the screen
/// rather than being logged and swallowed.</para>
///
/// <para><b>Bands are retired, never deleted.</b> A band that has priced a period is part of the
/// billing record. There is no delete endpoint here at all, which is the honest way to say so.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/subscription-tiers")]
public sealed class AdminSubscriptionTierController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IAuditLogService _auditLog;

    public AdminSubscriptionTierController(
        IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog)
    {
        _dbFactory = dbFactory;
        _auditLog  = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubscriptionTierAdminRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tiers = await db.SubscriptionTiers
            .AsNoTracking().Include(t => t.Prices).Include(t => t.Limits)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.MinMembers)
            .ToListAsync(ct);

        var counts = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.SubscriptionTierId != null)
            .GroupBy(s => s.SubscriptionTierId!.Value)
            .Select(g => new { TierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TierId, x => x.Count, ct);

        return Ok(tiers.Select(t => ToRecord(t, counts.GetValueOrDefault(t.Id))));
    }

    /// <summary>
    /// What is wrong with the price list as it stands, or null. Read by the editor so the problem
    /// is visible before somebody tries to save a fix for a different one.
    /// </summary>
    [HttpGet("validation")]
    public async Task<ActionResult<string?>> GetValidation(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tiers = await db.SubscriptionTiers.AsNoTracking().ToListAsync(ct);

        return Ok(SubscriptionTierResolver.Validate(tiers));
    }

    [HttpPost]
    public async Task<ActionResult<SubscriptionTierAdminRecord>> Create(
        [FromBody] SaveSubscriptionTierRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (Invalid(request) is { } bad) return BadRequest(bad);

        var now  = DateTime.UtcNow;
        var tier = new SubscriptionTier
        {
            Id                 = Guid.NewGuid(),
            DateCreated        = now,
            CreatedByAppUserId = userId,
        };

        Apply(tier, request, userId, now, isNew: true);
        db.SubscriptionTiers.Add(tier);

        if (await WouldBreakThePriceList(db, tier, ct) is { } problem) return BadRequest(problem);

        await db.SaveChangesAsync(ct);
        await _auditLog.LogCreateAsync(nameof(SubscriptionTier), tier.Id, tier, userId, AppSources.WebApi);

        return Ok(ToRecord(tier, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubscriptionTierAdminRecord>> Update(
        Guid id, [FromBody] SaveSubscriptionTierRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (Invalid(request) is { } bad) return BadRequest(bad);

        var tier = await db.SubscriptionTiers.Include(t => t.Prices).Include(t => t.Limits)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tier is null) return NotFound();

        var before = new { tier.Name, tier.MinMembers, tier.MaxMembers, tier.SortOrder, tier.IsActive };

        Apply(tier, request, userId, DateTime.UtcNow, isNew: false);

        if (await WouldBreakThePriceList(db, tier, ct) is { } problem) return BadRequest(problem);

        await db.SaveChangesAsync(ct);
        await _auditLog.LogUpdateAsync(nameof(SubscriptionTier), tier.Id, before, tier, userId, AppSources.WebApi);

        var inUse = await db.OrganizationSubscriptions.CountAsync(s => s.SubscriptionTierId == id, ct);
        return Ok(ToRecord(tier, inUse));
    }

    /// <summary>
    /// Rejects a band that cannot mean anything, before it is measured against the others.
    /// </summary>
    /// <remarks>
    /// Separate from the tiling check because these are wrong on their own terms — a band with no
    /// name, or one sold at the same cadence twice. The tiling check assumes each band is at least
    /// coherent.
    /// </remarks>
    private static string? Invalid(SaveSubscriptionTierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "A band needs a name.";
        if (request.MinMembers < 1)
            return "A band starts at one member or more.";
        if (request.MaxMembers is { } max && max < request.MinMembers)
            return "That band's upper limit is below its lower one.";
        if (request.Prices.Any(p => p.Price < 0))
            return "A price cannot be negative.";
        if (request.Prices.Select(p => p.Interval).Distinct().Count() != request.Prices.Count)
            return "That band is priced twice at the same cadence.";
        if (request.Limits.Select(l => l.Limit).Distinct().Count() != request.Limits.Count)
            return "That band caps the same thing twice.";
        if (request.Limits.Any(l => l.MaxValue is < 0))
            return "A cap cannot be negative. Zero turns the feature off; leaving it out means no cap.";
        if (!request.Prices.Any(p => p.IsActive))
            return "A band needs at least one cadence it is sold at, or nobody can subscribe to it.";

        return null;
    }

    /// <summary>
    /// The tiling rules, measured against the list as it <i>will be</i> rather than as it is.
    /// </summary>
    /// <remarks>
    /// The pending change is already on the tracked entity, so reading the other bands from the
    /// database and adding this one gives the post-save state. Validating the pre-save state would
    /// approve exactly the edit that breaks the list.
    /// </remarks>
    private static async Task<string?> WouldBreakThePriceList(
        BenDataContext db, SubscriptionTier changed, CancellationToken ct)
    {
        var others = await db.SubscriptionTiers.AsNoTracking()
            .Where(t => t.Id != changed.Id).ToListAsync(ct);

        others.Add(changed);

        return SubscriptionTierResolver.Validate(others) is { } problem
            ? $"That would leave the price list unusable: {problem}"
            : null;
    }

    /// <summary>
    /// Writes the request onto the band, replacing its prices with the ones sent.
    /// </summary>
    /// <remarks>
    /// A cadence dropped from the request is <b>retired, not removed</b>. Deleting the row would
    /// orphan any period billed against it, and the whole point of retiring rather than deleting is
    /// that "what did this group actually pay in March?" stays answerable.
    /// </remarks>
    private static void Apply(
        SubscriptionTier tier, SaveSubscriptionTierRequest request, Guid userId, DateTime now, bool isNew)
    {
        tier.Name       = request.Name.Trim();
        tier.MinMembers = request.MinMembers;
        tier.MaxMembers = request.MaxMembers;
        tier.SortOrder  = request.SortOrder;
        tier.IsActive   = request.IsActive;

        if (!isNew)
        {
            tier.DateUpdated        = now;
            tier.UpdatedByAppUserId = userId;
        }

        foreach (var existing in tier.Prices)
        {
            var sent = request.Prices.FirstOrDefault(p => p.Interval == existing.Interval);

            existing.Price              = sent?.Price ?? existing.Price;
            existing.IsActive           = sent?.IsActive ?? false;
            existing.DateUpdated        = now;
            existing.UpdatedByAppUserId = userId;
        }

        foreach (var sent in request.Prices.Where(p => tier.Prices.All(e => e.Interval != p.Interval)))
            tier.Prices.Add(new SubscriptionTierPrice
            {
                Id                 = Guid.NewGuid(),
                SubscriptionTierId = tier.Id,
                Interval           = sent.Interval,
                Price              = sent.Price,
                IsActive           = sent.IsActive,
                DateCreated        = now,
                CreatedByAppUserId = userId,
            });

        // Limits, unlike prices, really are removed when dropped: an absent cap IS the no-cap
        // state, and a retired-but-present cap would be indistinguishable from a live one on
        // every screen that renders the band.
        foreach (var gone in tier.Limits.Where(l => request.Limits.All(r => r.Limit != l.Limit)).ToList())
            tier.Limits.Remove(gone);

        foreach (var sent in request.Limits)
        {
            var existing = tier.Limits.FirstOrDefault(l => l.Limit == sent.Limit);
            if (existing is not null)
            {
                existing.MaxValue           = sent.MaxValue;
                existing.DateUpdated        = now;
                existing.UpdatedByAppUserId = userId;
            }
            else
                tier.Limits.Add(new SubscriptionTierLimit
                {
                    Id                 = Guid.NewGuid(),
                    SubscriptionTierId = tier.Id,
                    Limit              = sent.Limit,
                    MaxValue           = sent.MaxValue,
                    DateCreated        = now,
                    CreatedByAppUserId = userId,
                });
        }
    }

    private static SubscriptionTierAdminRecord ToRecord(SubscriptionTier tier, int organizationCount) =>
        new(tier.Id, tier.Name, tier.MinMembers, tier.MaxMembers, tier.SortOrder, tier.IsActive,
            [.. tier.Prices.OrderBy(p => (int)p.Interval).Select(p =>
                new SubscriptionTierPriceAdminRecord(
                    p.Interval, p.Price, p.IsActive,
                    SubscriptionPricing.SavingPercentAgainstMonthly(tier, p.Interval)))],
            [.. tier.Limits.OrderBy(l => (int)l.Limit)
                .Select(l => new SubscriptionTierLimitAdminRecord(l.Limit, l.MaxValue))],
            organizationCount);
}
