using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Saving the whole ladder at once, judged on where it ends up.
/// </summary>
/// <remarks>
/// <para>A band-at-a-time save validates the list after that one edit, which is right for an
/// ordinary change and makes some legitimate reshapes unreachable. Splitting an unbounded top band
/// into a bounded one and a new band above it is the case that forced this endpoint: bounding the
/// top first leaves the members above it unpriced, and adding the band above first overlaps the
/// unbounded one below. Both orders are refused, so the end state cannot be reached at all.</para>
///
/// <para>Found on 2026-09-05, trying to bring one database's ladder in line with the live one.</para>
/// </remarks>
public sealed class LadderSaveTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminSubscriptionTierController Build(IDbContextFactory<BenDataContext> f)
    {
        var ctrl = new AdminSubscriptionTierController(
            f,
            new Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object,
            new TierChangeNotifier(f, new PlatformMessageService(f)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };
        return ctrl;
    }

    /// <summary>Two bands: 1–3 at $20, and 4 upwards at $40 — the shape that cannot be split.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> F, Guid Bottom, Guid Top)> SeedAsync()
    {
        var f = CreateFactory();
        Guid bottom = Guid.NewGuid(), top = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        foreach (var (id, name, min, max, price, sort) in new (Guid, string, int, int?, decimal, int)[]
                 {
                     (bottom, "Small Group", 1, 3, 20m, 1),
                     (top,    "Large Group", 4, null, 40m, 2),
                 })
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = id, Name = name, MinMembers = min, MaxMembers = max, SortOrder = sort,
                IsActive = true, IsBandedByMembers = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = id,
                Interval = BillingInterval.Monthly, Price = price, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
        }
        await db.SaveChangesAsync();
        return (f, bottom, top);
    }

    private static SaveLadderBandRequest Band(
        Guid? id, string name, int min, int? max, decimal monthly, int sort)
        => new(id, name, min, max, sort,
               [new SaveTierPriceRequest(BillingInterval.Monthly, monthly, IsActive: true)],
               []);

    [Fact]
    public async Task A_top_band_can_be_split_in_one_save()
    {
        var (f, bottom, top) = await SeedAsync();

        var result = await Build(f).SaveLadder(new SaveSubscriptionLadderRequest(
        [
            Band(bottom, "Small Group", 1, 3, 20m, 1),
            Band(top,    "Large Group", 4, 25, 60m, 2),
            Band(null,   "Enterprise", 26, null, 100m, 3),
        ]), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await f.CreateDbContextAsync();
        var bands = await db.SubscriptionTiers.AsNoTracking()
            .Where(t => t.IsActive).OrderBy(t => t.MinMembers)
            .Select(t => new { t.Name, t.MinMembers, t.MaxMembers }).ToListAsync();

        Assert.Equal(3, bands.Count);
        Assert.Equal((1, 3), (bands[0].MinMembers, bands[0].MaxMembers));
        Assert.Equal((4, 25), (bands[1].MinMembers, bands[1].MaxMembers));
        Assert.Equal("Enterprise", bands[2].Name);
        Assert.Null(bands[2].MaxMembers);
    }

    /// <summary>
    /// The single-band door is what could not do this, and it still cannot — deliberately. Its
    /// rule protects an ordinary edit from breaking the list, and only a whole-ladder save has
    /// enough information to allow a step that is invalid on its own.
    /// </summary>
    [Fact]
    public async Task The_same_split_is_still_refused_one_band_at_a_time()
    {
        var (f, _, top) = await SeedAsync();

        var result = await Build(f).Update(top, new SaveSubscriptionTierRequest(
            "Large Group", 4, 25, 2, IsActive: true,
            [new SaveTierPriceRequest(BillingInterval.Monthly, 60m, IsActive: true)], []), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("unusable", refusal.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_band_left_out_of_the_ladder_is_retired_not_deleted()
    {
        var (f, bottom, top) = await SeedAsync();

        var result = await Build(f).SaveLadder(new SaveSubscriptionLadderRequest(
        [
            Band(top, "Everything", 1, null, 40m, 1),
        ]), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await f.CreateDbContextAsync();
        var gone = await db.SubscriptionTiers.AsNoTracking().SingleAsync(t => t.Id == bottom);

        // Still there, and still pointed at by anything that billed against it.
        Assert.False(gone.IsActive);
    }

    [Fact]
    public async Task A_ladder_with_a_hole_in_it_is_refused_and_nothing_is_written()
    {
        var (f, bottom, top) = await SeedAsync();

        var result = await Build(f).SaveLadder(new SaveSubscriptionLadderRequest(
        [
            Band(bottom, "Small Group", 1, 3, 20m, 1),
            Band(top,    "Large Group", 8, null, 40m, 2),   // nothing prices 4–7
        ]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Nothing prices 4", refusal.Value?.ToString() ?? "");

        await using var db = await f.CreateDbContextAsync();
        var unchanged = await db.SubscriptionTiers.AsNoTracking().SingleAsync(t => t.Id == top);
        Assert.Equal(4, unchanged.MinMembers);
    }

    [Fact]
    public async Task An_empty_ladder_is_refused()
    {
        var (f, _, _) = await SeedAsync();

        var result = await Build(f).SaveLadder(new SaveSubscriptionLadderRequest([]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("at least one band", refusal.Value?.ToString() ?? "");
    }

    /// <summary>A new band starts all-inclusive, the same rule the seeder follows.</summary>
    [Fact]
    public async Task A_band_added_by_a_ladder_save_includes_every_permission_area()
    {
        var (f, bottom, top) = await SeedAsync();

        await Build(f).SaveLadder(new SaveSubscriptionLadderRequest(
        [
            Band(bottom, "Small Group", 1, 3, 20m, 1),
            Band(top,    "Large Group", 4, 25, 60m, 2),
            Band(null,   "Enterprise", 26, null, 100m, 3),
        ]), default);

        await using var db = await f.CreateDbContextAsync();
        var added = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.PermissionAreas).SingleAsync(t => t.Name == "Enterprise");

        Assert.Equal(Enum.GetValues<OrganizationPermissionArea>().Length, added.PermissionAreas.Count);
    }
}
