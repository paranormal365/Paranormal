using System.Text.RegularExpressions;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The store's discount codes — GHOST10 and friends (storefront S1.5). Separate from the plan
/// coupons in <see cref="AdminCouponController"/>, which discount a group's subscription.
/// </summary>
/// <remarks>
/// <para><b>A used code is frozen.</b> An order keeps the code it was bought with, so once a code
/// has been used it can be retired (switched off) but not renamed or deleted — the order page, the
/// invoice and the refund would otherwise name a code that no longer exists, or a different one.</para>
///
/// <para><b>The list says what is wrong.</b> A code that takes nothing off, or whose window closes
/// before it opens, looks entirely normal on a screen and fails silently for whoever types it.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/coupons")]
public sealed partial class AdminStoreCouponController(IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog)
    : BenControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreCouponAdminRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, null, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StoreCouponAdminRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await ListAsync(db, id, ct)).SingleOrDefault() is { } record ? Ok(record) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<StoreCouponAdminRecord>> Create([FromBody] SaveStoreCouponRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var coupon = new StoreCoupon { Id = Guid.NewGuid(), DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId };
        if (await ApplyAsync(db, coupon, request, usedOn: 0, ct) is { } refused) return refused;
        db.StoreCoupons.Add(coupon);
        if (await SaveOrConflictAsync(db, coupon, ct) is { } conflict) return conflict;

        await TryAuditAsync(auditLog.LogCreateAsync(nameof(StoreCoupon), coupon.Id, coupon, userId, AppSources.WebApi));
        return Ok((await ListAsync(db, coupon.Id, ct)).Single());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StoreCouponAdminRecord>> Update(Guid id, [FromBody] SaveStoreCouponRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var coupon = await db.StoreCoupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return NotFound();
        var before = Clone(coupon);

        var usedOn = await db.StoreOrders.CountAsync(o => o.CouponId == id, ct);
        if (await ApplyAsync(db, coupon, request, usedOn, ct) is { } refused) return refused;
        coupon.DateUpdated = DateTime.UtcNow;
        coupon.UpdatedByAppUserId = userId;
        if (await SaveOrConflictAsync(db, coupon, ct) is { } conflict) return conflict;

        await TryAuditAsync(auditLog.LogUpdateAsync(nameof(StoreCoupon), id, before, coupon, userId, AppSources.WebApi));
        return Ok((await ListAsync(db, id, ct)).Single());
    }

    /// <summary>Removes a code nobody has used. A used one is retired instead.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var coupon = await db.StoreCoupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return NotFound();

        var usedOn = await db.StoreOrders.CountAsync(o => o.CouponId == id, ct);
        if (usedOn > 0)
            return BadRequest($"{coupon.Code} has been used on {Orders(usedOn)}. "
                            + "Retire it instead — an order keeps the code it was bought with.");

        db.StoreCoupons.Remove(coupon);
        await db.SaveChangesAsync(ct);
        await TryAuditAsync(auditLog.LogDeleteAsync(nameof(StoreCoupon), id, coupon, userId, AppSources.WebApi));
        return NoContent();
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    [GeneratedRegex("^[A-Z0-9-]{3,64}$")]
    private static partial Regex CodeShape();

    private static string Orders(int n) => n == 1 ? "1 order" : $"{n} orders";

    /// <summary>Copies the request onto the row, or answers why not.</summary>
    private async Task<ActionResult?> ApplyAsync(
        BenDataContext db, StoreCoupon coupon, SaveStoreCouponRequest request, int usedOn, CancellationToken ct)
    {
        var code = CouponCodeGenerator.Normalise(request.Code);
        if (code.Length == 0) return BadRequest("A coupon needs a code for people to type.");
        if (!CodeShape().IsMatch(code)) return BadRequest("A code is 3–64 letters, digits or dashes.");
        if (usedOn > 0 && code != coupon.Code)
            return BadRequest($"{coupon.Code} has been used on {Orders(usedOn)}; make a new code instead of renaming this one.");
        if (await db.StoreCoupons.AnyAsync(c => c.Id != coupon.Id && c.Code == code, ct))
            return Conflict($"The code {code} is already in use.");

        if (request.PercentOff is not null && request.AmountOff is not null)
            return BadRequest("Set a percentage or an amount, not both.");
        var kind = request.PercentOff is not null ? StoreCouponKind.Percent
                 : request.AmountOff is not null ? StoreCouponKind.Fixed
                 : request.Kind;
        if (kind == StoreCouponKind.Percent && request.PercentOff is not (>= 1 and <= 100))
            return BadRequest("Percent off is 1 to 100.");
        if (kind == StoreCouponKind.Fixed && request.AmountOff is not > 0m)
            return BadRequest("An amount off is more than $0.00.");
        if (request.AmountOff is { } amount && amount != StoreMoney.Round(amount)
            || request.MinimumOrderAmount is { } min && min != StoreMoney.Round(min))
            return BadRequest("Amounts are dollars and cents — two decimal places at most.");
        if (request.MinimumOrderAmount is < 0m) return BadRequest("A minimum order can't be negative.");
        if (request.StartsUtc is { } starts && request.EndsUtc is { } ends && ends <= starts)
            return BadRequest("The window closes before it opens.");
        if (request.MaxRedemptions is < 1)
            return BadRequest("Leave the total uses empty for unlimited, or allow at least one.");
        if (request.MaxRedemptionsPerBuyer is < 1)
            return BadRequest("Leave uses per buyer empty for unlimited, or allow at least one.");

        var name = string.IsNullOrWhiteSpace(request.Name) ? code : request.Name.Trim();
        if (name.Length > 150) return BadRequest("A coupon's name is 150 characters at most.");

        coupon.Code = code;
        coupon.Name = name;
        coupon.Kind = kind;
        coupon.PercentOff = kind == StoreCouponKind.Percent ? request.PercentOff : null;
        coupon.AmountOff = kind == StoreCouponKind.Fixed ? request.AmountOff : null;
        coupon.MinimumOrderAmount = request.MinimumOrderAmount is > 0m ? request.MinimumOrderAmount : null;
        coupon.StartsUtc = request.StartsUtc;
        coupon.EndsUtc = request.EndsUtc;
        coupon.MaxRedemptions = request.MaxRedemptions;
        coupon.MaxRedemptionsPerBuyer = request.MaxRedemptionsPerBuyer;
        coupon.IsActive = request.IsActive;

        // The shared rules the checkout also reads, as a backstop: nothing reaches a buyer that
        // the redemption check would call unrecognised.
        return StoreCouponMath.Misconfiguration(coupon) is { } bad ? BadRequest(bad) : null;
    }

    private async Task<ActionResult?> SaveOrConflictAsync(BenDataContext db, StoreCoupon coupon, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            if (await db.StoreCoupons.AsNoTracking().AnyAsync(c => c.Id != coupon.Id && c.Code == coupon.Code, ct))
                return Conflict($"The code {coupon.Code} is already in use.");
            throw;
        }
    }

    /// <summary>What stops the code working today, in two or three words; null when it works.</summary>
    internal static string? Problem(StoreCoupon c, DateTime now)
    {
        if (c.Kind == StoreCouponKind.Percent ? c.PercentOff is not > 0 : c.AmountOff is not > 0m) return "Takes nothing off.";
        if (c.StartsUtc is { } starts && c.EndsUtc is { } ends && ends <= starts) return "Expires before it starts.";
        if (c.MaxRedemptions is { } max && c.RedemptionCount >= max) return "Used up.";
        if (c.EndsUtc is { } end && now >= end) return "Expired.";
        return null;
    }

    private static async Task<List<StoreCouponAdminRecord>> ListAsync(BenDataContext db, Guid? only, CancellationToken ct)
    {
        var coupons = await db.StoreCoupons.AsNoTracking()
            .Where(c => only == null || c.Id == only)
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.Code)
            .Select(c => new { Coupon = c, Orders = db.StoreOrders.Count(o => o.CouponId == c.Id) })
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        return coupons.Select(x => new StoreCouponAdminRecord(
            x.Coupon.Id, x.Coupon.Code, x.Coupon.Name, x.Coupon.Kind, x.Coupon.PercentOff, x.Coupon.AmountOff,
            x.Coupon.MinimumOrderAmount, x.Coupon.StartsUtc, x.Coupon.EndsUtc, x.Coupon.MaxRedemptions,
            x.Coupon.RedemptionCount, x.Coupon.MaxRedemptionsPerBuyer, x.Coupon.IsActive, Problem(x.Coupon, now),
            x.Orders, x.Coupon.DateCreated)).ToList();
    }

    private static StoreCoupon Clone(StoreCoupon c) => new()
    {
        Id = c.Id, Code = c.Code, Name = c.Name, Kind = c.Kind, PercentOff = c.PercentOff, AmountOff = c.AmountOff,
        MinimumOrderAmount = c.MinimumOrderAmount, StartsUtc = c.StartsUtc, EndsUtc = c.EndsUtc,
        MaxRedemptions = c.MaxRedemptions, MaxRedemptionsPerBuyer = c.MaxRedemptionsPerBuyer, IsActive = c.IsActive,
    };
}
