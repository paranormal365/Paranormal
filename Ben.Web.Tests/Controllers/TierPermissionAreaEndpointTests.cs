using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>The tier area-checklist endpoint (item 156 Phase A): whole-list replace semantics.</summary>
public sealed class TierPermissionAreaEndpointTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static AdminSubscriptionTierController Build(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new AdminSubscriptionTierController(
            factory,
            new Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object,
            new Ben.Data.WebApi.Services.Billing.TierChangeNotifier(factory, new Ben.Data.WebApi.Services.PlatformMessageService(factory)));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<Guid> SeedTierAsync(IDbContextFactory<BenDataContext> factory,
        params OrganizationPermissionArea[] areas)
    {
        var tierId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "T", MinMembers = 1, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        foreach (var a in areas)
            db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
            {
                SubscriptionTierId = tierId, Area = a,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
        await db.SaveChangesAsync();
        return tierId;
    }

    [Fact]
    public async Task Replace_checks_and_unchecks_in_one_save()
    {
        var factory = CreateFactory();
        var tierId = await SeedTierAsync(factory,
            OrganizationPermissionArea.Cases, OrganizationPermissionArea.Equipment);

        var result = await Build(factory).SetPermissionAreas(tierId,
            new SetTierPermissionAreasRequest([OrganizationPermissionArea.Equipment, OrganizationPermissionArea.Files]),
            default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<SubscriptionTierAdminRecord>(ok.Value);
        Assert.Equal(
            new[] { OrganizationPermissionArea.Equipment, OrganizationPermissionArea.Files },
            record.IncludedAreas!.OrderBy(a => (int)a));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.SubscriptionTierPermissionAreas.CountAsync(a => a.SubscriptionTierId == tierId));
    }

    [Fact]
    public async Task An_unknown_area_value_is_refused()
    {
        var factory = CreateFactory();
        var tierId = await SeedTierAsync(factory, OrganizationPermissionArea.Cases);

        var result = await Build(factory).SetPermissionAreas(tierId,
            new SetTierPermissionAreasRequest([(OrganizationPermissionArea)999]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.SubscriptionTierPermissionAreas.CountAsync(a => a.SubscriptionTierId == tierId));
    }

    [Fact]
    public async Task A_missing_tier_is_NotFound()
    {
        var result = await Build(CreateFactory()).SetPermissionAreas(Guid.NewGuid(),
            new SetTierPermissionAreasRequest([OrganizationPermissionArea.Cases]), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }
}
