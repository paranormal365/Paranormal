using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Discount campaigns and the codes under them.
/// </summary>
/// <remarks>
/// <para><b>A campaign is not a code.</b> A shared campaign has exactly one code and the two are
/// created together, which is why <see cref="SaveCouponRequest.SharedCode"/> exists. A generated
/// campaign has as many codes as somebody asks for, made by
/// <see cref="Generate"/>. Everything downstream treats them identically.</para>
///
/// <para><b>Codes are withdrawn, never deleted</b>, and a campaign that has priced a period cannot
/// be deleted either. Both are part of the answer to "why was this group charged less?".</para>
///
/// <para><b>Misconfiguration is reported in the list, not only at redemption.</b> A coupon that
/// takes nothing off, or whose window closes before it opens, looks entirely normal on a screen and
/// fails silently for whoever types it. The list carries the problem so it is found by the person
/// who made it rather than by a customer.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/coupons")]
public sealed class AdminCouponController : BenControllerBase
{
    /// <summary>Most codes one request will make. A batch beyond this is a data-entry mistake.</summary>
    private const int MaxBatchSize = 2_000;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IAuditLogService _auditLog;

    public AdminCouponController(IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog)
    {
        _dbFactory = dbFactory;
        _auditLog  = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CouponAdminRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var coupons = await db.Coupons.AsNoTracking().Include(c => c.Codes)
            .OrderByDescending(c => c.DateCreated).ToListAsync(ct);

        return Ok(coupons.Select(ToRecord));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CouponAdminRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var coupon = await db.Coupons.AsNoTracking().Include(c => c.Codes)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return coupon is null ? NotFound() : Ok(ToRecord(coupon));
    }

    /// <summary>Every code under one campaign, with who each is addressed to.</summary>
    [HttpGet("{id:guid}/codes")]
    public async Task<ActionResult<IEnumerable<CouponCodeAdminRecord>>> GetCodes(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!await db.Coupons.AnyAsync(c => c.Id == id, ct)) return NotFound();

        // The addressed account's name is joined here rather than looked up per row by the screen.
        // A batch of five hundred addressed codes would otherwise be five hundred requests.
        var codes = await db.CouponCodes.AsNoTracking()
            .Where(c => c.CouponId == id)
            .OrderBy(c => c.Code)
            .Select(c => new
            {
                Code  = c,
                Owner = c.RestrictedToAppUserId == null
                    ? null
                    : db.Users.Where(u => u.Id == c.RestrictedToAppUserId)
                        .Select(u => u.DisplayName ?? u.UserName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(codes.Select(x => new CouponCodeAdminRecord(
            x.Code.Id, x.Code.Code, x.Code.MaxRedemptions, x.Code.RedemptionCount,
            x.Code.IssuedTo, x.Code.RestrictedToAppUserId, x.Owner,
            x.Code.IsActive, x.Code.DateCreated)));
    }

    /// <summary>
    /// Every redemption under one campaign, with the money — the referral report.
    /// </summary>
    /// <remarks>
    /// This answers "what do I owe whoever handed these codes out?" — Ben's requirement, stated
    /// as reimbursement. Each row carries the code, the code's IssuedTo note (the referrer), the
    /// redeeming group, and the frozen list/discount/paid amounts. Frozen, because a commission
    /// computed from live prices changes retroactively every time the price list is edited.
    /// </remarks>
    [HttpGet("{id:guid}/redemptions")]
    public async Task<ActionResult<IEnumerable<CouponRedemptionAdminRecord>>> GetRedemptions(
        Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!await db.Coupons.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var rows = await db.CouponRedemptions.AsNoTracking()
            .Where(r => r.CouponId == id)
            .OrderByDescending(r => r.RedeemedAtUtc)
            .Select(r => new CouponRedemptionAdminRecord(
                r.CouponCode.Code,
                r.CouponCode.IssuedTo,
                r.Organization.Name,
                r.RedeemedAtUtc,
                r.ListPrice, r.Discount, r.Payable))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<CouponAdminRecord>> Create(
        [FromBody] SaveCouponRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now    = DateTime.UtcNow;
        var coupon = new Coupon { Id = Guid.NewGuid(), DateCreated = now, CreatedByAppUserId = userId };

        Apply(coupon, request);

        if (CouponMath.Misconfiguration(coupon) is { } bad) return BadRequest(bad);

        if (request.Kind == CouponKind.Shared)
        {
            var code = CouponCodeGenerator.Normalise(request.SharedCode);

            if (code.Length == 0)
                return BadRequest("A shared campaign needs a code for people to type.");
            if (code.Length > 64)
                return BadRequest("That code is too long — 64 characters at most.");
            if (await db.CouponCodes.AnyAsync(c => c.Code == code, ct))
                return BadRequest($"The code {code} is already in use by another campaign.");

            coupon.Codes.Add(new CouponCode
            {
                Id                 = Guid.NewGuid(),
                CouponId           = coupon.Id,
                Code               = code,
                MaxRedemptions     = null,   // the campaign's own cap governs a shared code
                IsActive           = true,
                DateCreated        = now,
                CreatedByAppUserId = userId,
            });
        }

        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        await _auditLog.LogCreateAsync(nameof(Coupon), coupon.Id, coupon, userId, AppSources.WebApi);

        return Ok(ToRecord(coupon));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CouponAdminRecord>> Update(
        Guid id, [FromBody] SaveCouponRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var coupon = await db.Coupons.Include(c => c.Codes).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return NotFound();

        // Same-type clone for the audit diff — AuditChangeTracker refuses anonymous objects.
        var before = new Coupon
        {
            Id             = coupon.Id,
            Name           = coupon.Name,
            Kind           = coupon.Kind,
            PercentOff     = coupon.PercentOff,
            AmountOff      = coupon.AmountOff,
            Duration       = coupon.Duration,
            MaxRedemptions = coupon.MaxRedemptions,
            RedeemByUtc    = coupon.RedeemByUtc,
            AppliesTo      = coupon.AppliesTo,
            IsActive       = coupon.IsActive,
        };

        // The kind is fixed once codes exist. Turning a two-hundred-code batch into a shared
        // campaign would leave a hundred and ninety-nine codes that are valid and meaningless.
        if (request.Kind != coupon.Kind && coupon.Codes.Count > 1)
            return BadRequest(
                "That campaign already has several codes, so it cannot become a shared one. "
              + "Retire it and make a new campaign instead.");

        Apply(coupon, request);
        coupon.Kind               = request.Kind;
        coupon.DateUpdated        = DateTime.UtcNow;
        coupon.UpdatedByAppUserId = userId;

        if (CouponMath.Misconfiguration(coupon) is { } bad) return BadRequest(bad);

        await db.SaveChangesAsync(ct);
        await _auditLog.LogUpdateAsync(nameof(Coupon), coupon.Id, before, coupon, userId, AppSources.WebApi);

        return Ok(ToRecord(coupon));
    }

    /// <summary>Generates a batch of codes under an existing campaign.</summary>
    /// <remarks>
    /// Generated in one go and inserted in one transaction. A partially-written batch would leave
    /// a print run that does not match the database, and there is no way to tell afterwards which
    /// half is real.
    /// </remarks>
    [HttpPost("{id:guid}/codes")]
    public async Task<ActionResult<IEnumerable<CouponCodeAdminRecord>>> Generate(
        Guid id, [FromBody] GenerateCouponCodesRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return NotFound();

        if (request.Count < 1)
            return BadRequest("Generating no codes does nothing.");
        if (request.Count > MaxBatchSize)
            return BadRequest($"That is more than {MaxBatchSize:N0} codes in one go.");
        if (coupon.Kind == CouponKind.Shared)
            return BadRequest(
                "That is a shared campaign, which has one code. Change it to a generated batch first.");
        if ((request.Prefix ?? string.Empty).Length > CouponCodeGenerator.MaxPrefixLength)
            return BadRequest($"That prefix is longer than {CouponCodeGenerator.MaxPrefixLength} characters.");
        if (request.MaxRedemptionsPerCode is { } per && per < 1)
            return BadRequest("A code redeemable no times cannot be redeemed.");
        if (request.RestrictedToAppUserId is { } owner && !await db.Users.AnyAsync(u => u.Id == owner, ct))
            return BadRequest("That account does not exist.");
        if (request.RestrictedToAppUserId is not null && request.Count > 1)
            return BadRequest(
                "Addressing a whole batch to one person gives them several codes for the same offer, "
              + "and they can only use one. Generate a single code instead.");

        var now      = DateTime.UtcNow;
        var wanted   = CouponCodeGenerator.Batch(request.Count, request.Prefix);

        // Distinct within the batch is the generator's job; distinct against every other campaign
        // is this one's. The unique index is still the authority, and a clash here simply means
        // drawing again rather than failing the request.
        var taken    = await db.CouponCodes.AsNoTracking()
            .Where(c => wanted.Contains(c.Code)).Select(c => c.Code).ToListAsync(ct);

        var codes = wanted.Except(taken).ToList();
        while (codes.Count < request.Count)
        {
            var extra = CouponCodeGenerator.One(request.Prefix);
            if (!codes.Contains(extra) && !await db.CouponCodes.AnyAsync(c => c.Code == extra, ct))
                codes.Add(extra);
        }

        var rows = codes.Select(code => new CouponCode
        {
            Id                    = Guid.NewGuid(),
            CouponId              = coupon.Id,
            Code                  = code,
            MaxRedemptions        = request.MaxRedemptionsPerCode ?? 1,
            RestrictedToAppUserId = request.RestrictedToAppUserId,
            IsActive              = true,
            DateCreated           = now,
            CreatedByAppUserId    = userId,
        }).ToList();

        db.CouponCodes.AddRange(rows);
        await db.SaveChangesAsync(ct);

        await _auditLog.LogCreateAsync(
            nameof(CouponCode), coupon.Id,
            new { coupon.Name, Generated = rows.Count, request.Prefix, request.MaxRedemptionsPerCode },
            userId, AppSources.WebApi);

        return Ok(rows.Select(r => new CouponCodeAdminRecord(
            r.Id, r.Code, r.MaxRedemptions, 0, r.IssuedTo, r.RestrictedToAppUserId, null,
            r.IsActive, r.DateCreated)));
    }

    /// <summary>Edits one code — withdrawing it, capping it, or addressing it to somebody.</summary>
    [HttpPut("{id:guid}/codes/{codeId:guid}")]
    public async Task<ActionResult<CouponCodeAdminRecord>> UpdateCode(
        Guid id, Guid codeId, [FromBody] SaveCouponCodeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var code = await db.CouponCodes.FirstOrDefaultAsync(c => c.Id == codeId && c.CouponId == id, ct);
        if (code is null) return NotFound();

        if (request.MaxRedemptions is { } max && max < code.RedemptionCount)
            return BadRequest(
                $"That code has already been redeemed {code.RedemptionCount} times, "
              + $"so it cannot be capped at {max}.");

        if (request.RestrictedToAppUserId is { } owner && !await db.Users.AnyAsync(u => u.Id == owner, ct))
            return BadRequest("That account does not exist.");

        var before = new CouponCode
        {
            Id                    = code.Id,
            Code                  = code.Code,
            MaxRedemptions        = code.MaxRedemptions,
            IssuedTo              = code.IssuedTo,
            RestrictedToAppUserId = code.RestrictedToAppUserId,
            IsActive              = code.IsActive,
        };

        code.MaxRedemptions        = request.MaxRedemptions;
        code.IssuedTo              = string.IsNullOrWhiteSpace(request.IssuedTo) ? null : request.IssuedTo.Trim();
        code.RestrictedToAppUserId = request.RestrictedToAppUserId;
        code.IsActive              = request.IsActive;
        code.DateUpdated           = DateTime.UtcNow;
        code.UpdatedByAppUserId    = userId;

        await db.SaveChangesAsync(ct);
        await _auditLog.LogUpdateAsync(nameof(CouponCode), code.Id, before, code, userId, AppSources.WebApi);

        var ownerName = code.RestrictedToAppUserId is null ? null : await db.Users
            .Where(u => u.Id == code.RestrictedToAppUserId)
            .Select(u => u.DisplayName ?? u.UserName).FirstOrDefaultAsync(ct);

        return Ok(new CouponCodeAdminRecord(
            code.Id, code.Code, code.MaxRedemptions, code.RedemptionCount, code.IssuedTo,
            code.RestrictedToAppUserId, ownerName, code.IsActive, code.DateCreated));
    }

    private static void Apply(Coupon coupon, SaveCouponRequest request)
    {
        coupon.Name              = request.Name.Trim();
        coupon.Description       = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        coupon.Kind              = request.Kind;
        coupon.PercentOff        = request.PercentOff;
        coupon.AmountOff         = request.AmountOff;
        coupon.Duration          = request.Duration;
        coupon.DurationPeriods   = request.DurationPeriods;
        coupon.MaxRedemptions    = request.MaxRedemptions;
        coupon.ValidFromUtc      = request.ValidFromUtc;
        coupon.RedeemByUtc       = request.RedeemByUtc;
        coupon.AppliesToInterval = request.AppliesToInterval;
        coupon.AppliesTo         = request.AppliesTo;
        coupon.IsActive          = request.IsActive;
    }

    private static CouponAdminRecord ToRecord(Coupon coupon)
    {
        var codes = coupon.Codes.ToList();

        return new CouponAdminRecord(
            coupon.Id, coupon.Name, coupon.Description, coupon.Kind,
            coupon.PercentOff, coupon.AmountOff, coupon.Duration, coupon.DurationPeriods,
            coupon.MaxRedemptions, coupon.RedemptionCount,
            coupon.ValidFromUtc, coupon.RedeemByUtc, coupon.AppliesToInterval, coupon.AppliesTo,
            coupon.IsActive, codes.Count,
            coupon.Kind == CouponKind.Shared ? codes.FirstOrDefault()?.Code : null,
            CouponMath.Misconfiguration(coupon) ?? CouponMath.BatchMisconfiguration(coupon, codes),
            coupon.DateCreated);
    }
}
