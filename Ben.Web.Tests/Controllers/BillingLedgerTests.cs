using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 168: the money trail. The invariants worth regressing are the financial ones — tax is
/// frozen on the row, receipt numbers are sequential and unique, and THE LEDGER CANNOT BE
/// EDITED, which is pinned structurally: a future PUT endpoint fails a test, not a review.
/// </summary>
public sealed class BillingLedgerTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminBillingController Admin(IDbContextFactory<BenDataContext> factory, Guid userId) =>
        new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid adminId)> SeedOrgAsync(
        string? state = "TN", decimal? stateRate = 9.75m)
    {
        var factory = Factory();
        Guid orgId = Guid.NewGuid(), adminId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = adminId, UserName = "a@t.com", Email = "a@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Night Watch", UrlName = "nw", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        if (state is not null)
        {
            db.OrganizationAddresses.Add(new OrganizationAddress
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, OrganizationAddressTypeId = SeedAddressType(db, adminId),
                StreetAddress1 = "1 Main St", City = "Nashville", State = state, ZipCode = "37201",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            });
        }
        if (stateRate is { } rate)
        {
            db.TaxRateRules.Add(new TaxRateRule
            {
                Id = Guid.NewGuid(), State = "TN", RatePercent = rate,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            });
        }
        await db.SaveChangesAsync();
        return (factory, orgId, adminId);
    }

    private static Guid SeedAddressType(BenDataContext db, Guid userId)
    {
        var id = Guid.NewGuid();
        db.OrganizationAddressTypes.Add(new OrganizationAddressType
        {
            Id = id, Name = "Main", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        return id;
    }

    private static BillingLedgerEntryRecord Body(ActionResult<BillingLedgerEntryRecord> result)
        => Assert.IsType<BillingLedgerEntryRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // ── The structural invariant ─────────────────────────────────────────────

    [Fact]
    public void The_ledger_is_append_only_no_update_or_delete_endpoint_exists()
    {
        // Tax-rate rules are configuration and may be edited or deleted; ledger rows are the
        // financial record and may not. The test walks the controller so the invariant survives
        // every future contributor, including this one.
        var mutating = typeof(AdminBillingController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<HttpPutAttribute>() is not null
                     || m.GetCustomAttribute<HttpDeleteAttribute>() is not null
                     || m.GetCustomAttribute<HttpPatchAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(["DeleteTaxRate", "SaveTaxRate"], mutating.OrderBy(n => n));
    }

    // ── Tax ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Tax_math_rounds_to_cents_half_up()
    {
        Assert.Equal(0.97m, TaxResolver.TaxOn(9.99m, 9.75m));   // 0.974025 → 0.97
        Assert.Equal(0.98m, TaxResolver.TaxOn(10.00m, 9.75m));  // 0.975 → 0.98, half-up
        Assert.Equal(0.13m, TaxResolver.TaxOn(5.00m, 2.5m));   // 0.125: half-up 0.13, banker's 0.12
        Assert.Equal(0m, TaxResolver.TaxOn(10.00m, 0m));
    }

    [Fact]
    public async Task A_charge_computes_tax_from_the_groups_state_and_freezes_it_on_the_row()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        var record = Body(await Admin(factory, adminId).RecordCharge(
            orgId, new RecordBillingEntryRequest(100m, "Pro band, September", null, null, null), default));

        Assert.Equal(9.75m, record.TaxRatePercent);
        Assert.Equal(9.75m, record.TaxAmount);

        // The freeze: change the rule, the row must not move.
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.TaxRateRules.SingleAsync()).RatePercent = 2m;
            await db.SaveChangesAsync();
        }
        await using (var db2 = await factory.CreateDbContextAsync())
        {
            var row = await db2.BillingLedgerEntries.SingleAsync();
            Assert.Equal(9.75m, row.TaxRatePercent);
            Assert.Equal(9.75m, row.TaxAmount);
        }
    }

    [Fact]
    public async Task A_group_in_a_state_with_no_rule_is_taxed_at_an_honest_zero()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync(state: "OR", stateRate: 9.75m); // rule is for TN
        var record = Body(await Admin(factory, adminId).RecordCharge(
            orgId, new RecordBillingEntryRequest(100m, "x", null, null, null), default));
        Assert.Equal(0m, record.TaxAmount);
    }

    [Fact]
    public async Task A_payment_carries_no_tax_of_its_own_the_charge_it_settles_already_did()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        var record = Body(await Admin(factory, adminId).RecordPayment(
            orgId, new RecordBillingEntryRequest(109.75m, "September, check 4411", "4411", null, null), default));
        Assert.Equal(0m, record.TaxAmount);
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Payments_get_sequential_receipt_numbers_and_nothing_else_gets_one()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        var admin = Admin(factory, adminId);

        var charge = Body(await admin.RecordCharge(orgId, new RecordBillingEntryRequest(50m, "c", null, null, null), default));
        var first  = Body(await admin.RecordPayment(orgId, new RecordBillingEntryRequest(50m, "p1", null, null, null), default));
        var second = Body(await admin.RecordPayment(orgId, new RecordBillingEntryRequest(25m, "p2", null, null, null), default));
        var adjust = Body(await admin.RecordAdjustment(orgId, new RecordAdjustmentRequest(5m, true, "goodwill"), default));

        Assert.Null(charge.ReceiptNumber);
        Assert.Null(adjust.ReceiptNumber);
        Assert.Equal(1, first.ReceiptNumber);
        Assert.Equal(2, second.ReceiptNumber);
    }

    [Fact]
    public async Task A_ledger_row_without_a_description_is_refused()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        Assert.IsType<BadRequestObjectResult>((await Admin(factory, adminId).RecordCharge(
            orgId, new RecordBillingEntryRequest(50m, "  ", null, null, null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Admin(factory, adminId).RecordAdjustment(
            orgId, new RecordAdjustmentRequest(5m, true, ""), default)).Result);
    }

    // ── The group's own view ─────────────────────────────────────────────────

    private static Ben.Data.WebApi.Controllers.OrganizationBillingController OrgSide(
        IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory, new Moq.Mock<AutoMapper.IMapper>().Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    [Fact]
    public async Task An_outsider_may_not_read_a_groups_billing_history_or_its_receipts()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        var payment = Body(await Admin(factory, adminId).RecordPayment(
            orgId, new RecordBillingEntryRequest(50m, "p", null, null, null), default));

        var outsiderId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = outsiderId, UserName = "o@t.com", Email = "o@t.com", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var outsider = OrgSide(factory, outsiderId);
        Assert.IsType<ForbidResult>((await outsider.GetHistory(orgId, default)).Result);
        Assert.IsType<ForbidResult>(await outsider.GetReceipt(orgId, payment.Id, default));
    }

    [Fact]
    public async Task Only_a_payment_row_has_a_receipt_and_the_owner_can_read_it()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        var ownerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = ownerId, UserName = "own@t.com", Email = "own@t.com", DateCreated = DateTime.UtcNow });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }
        await TestSeeds.BridgeAsync(factory, orgId);

        var admin = Admin(factory, adminId);
        var charge = Body(await admin.RecordCharge(orgId, new RecordBillingEntryRequest(50m, "c", null, null, null), default));
        var payment = Body(await admin.RecordPayment(orgId, new RecordBillingEntryRequest(50m, "p", null, null, null), default));

        var owner = OrgSide(factory, ownerId);
        Assert.IsType<BadRequestObjectResult>(await owner.GetReceipt(orgId, charge.Id, default));

        var file = Assert.IsType<FileContentResult>(await owner.GetReceipt(orgId, payment.Id, default));
        var html = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains($"R-{payment.ReceiptNumber:00000}", html);
        Assert.Contains("Night Watch", html);
    }

    // ── Referrals ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Referrer_standings_sum_their_coupons_redemptions_and_subtract_nothing()
    {
        var (factory, orgId, adminId) = await SeedOrgAsync();
        var referrerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = referrerId, UserName = "r@t.com", Email = "r@t.com", DisplayName = "Rita Referrer", DateCreated = DateTime.UtcNow });
            var coupon = new Coupon
            {
                Id = Guid.NewGuid(), Name = "Rita's codes", Kind = CouponKind.Generated,
                PercentOff = 20, Duration = CouponDuration.Once, ReferrerAppUserId = referrerId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            };
            var code = new CouponCode
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, Code = "RITA1", IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            };
            db.Coupons.Add(coupon);
            db.CouponCodes.Add(code);
            db.CouponRedemptions.Add(new CouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, CouponCodeId = code.Id, OrganizationId = orgId,
                ListPrice = 50m, Discount = 10m, Payable = 40m, RedeemedAtUtc = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            });
            await db.SaveChangesAsync();
        }

        var admin = Admin(factory, adminId);
        Body(await admin.RecordReferralPayout(
            new RecordReferralPayoutRequest(referrerId, 8m, "first redemption", null), default));

        var rows = Assert.IsAssignableFrom<IEnumerable<ReferrerSummaryRecord>>(
            Assert.IsType<OkObjectResult>((await admin.GetReferrers(default)).Result).Value).ToList();

        var rita = Assert.Single(rows);
        Assert.Equal("Rita Referrer", rita.ReferrerName);
        Assert.Equal(1, rita.RedemptionCount);
        Assert.Equal(40m, rita.RevenueAttributed);
        Assert.Equal(10m, rita.DiscountGiven);
        Assert.Equal(8m, rita.PaidOut);
    }
}
