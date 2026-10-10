using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="AdminCouponController"/>.
/// </summary>
public sealed class AdminCouponControllerTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminCouponController Build(IDbContextFactory<BenDataContext> f, IAuditLogService audit)
        => new(f, audit)
        {
            ControllerContext = new ControllerContext
            {
                // RequestServices as a host gives it: the audit guard logs its failure through it.
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider(),
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };

    /// <summary>
    /// The audit is written after the change is saved. An audit service that threw there answered
    /// 500 for a coupon that had in fact been created, and skipped whatever came after the audit
    /// (site audit, 10/09/2026). The save stands and the action answers its success.
    /// </summary>
    [Fact]
    public async Task A_failing_audit_write_does_not_turn_a_saved_coupon_into_an_error()
    {
        var f = CreateFactory();
        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
             .Throws(new InvalidOperationException("The audit table is unreachable."));

        var request = new SaveCouponRequest(
            Name: "Spring", Description: null, Kind: CouponKind.Shared,
            PercentOff: 20, AmountOff: null, Duration: CouponDuration.Once, DurationPeriods: null,
            MaxRedemptions: null, ValidFromUtc: null, RedeemByUtc: null, AppliesToInterval: null,
            AppliesTo: CouponApplicability.Any, IsActive: true, SharedCode: "SPRING20");

        var result = await Build(f, audit.Object).Create(request, default);

        var record = Assert.IsType<CouponAdminRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Spring", record.Name);
        audit.Verify(a => a.LogCreateAsync(nameof(Coupon), It.IsAny<Guid>(), It.IsAny<object>(), AdminId, It.IsAny<string>()), Times.Once);
        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(1, await db.Coupons.CountAsync());
    }
}
