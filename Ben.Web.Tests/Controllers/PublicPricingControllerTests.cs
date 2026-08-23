using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 156 Phase E: the public price list carries each plan's included role areas, with the
/// fail-open rule intact — zero checklist rows means ALL areas and must arrive as null, never
/// as an empty list the page would render as "includes nothing".
/// </summary>
public sealed class PublicPricingControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>Two active bands tiling 1..∞ so the resolver's validation passes.</summary>
    private static async Task<(Guid firstTierId, Guid secondTierId)> SeedTiersAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        var editor = Guid.NewGuid();
        var first  = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = first, Name = "Small", MinMembers = 1, MaxMembers = 10,
            SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = editor,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = second, Name = "Large", MinMembers = 11, MaxMembers = null,
            SortOrder = 2, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = editor,
        });
        db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
        {
            SubscriptionTierId = first, Area = OrganizationPermissionArea.Calendar,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = editor,
        });
        db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
        {
            SubscriptionTierId = first, Area = OrganizationPermissionArea.Cases,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = editor,
        });
        await db.SaveChangesAsync();
        return (first, second);
    }

    [Fact]
    public async Task Tiers_carry_their_checklist_and_zero_rows_arrives_as_null_not_empty()
    {
        var factory = CreateFactory();
        var (firstId, secondId) = await SeedTiersAsync(factory);

        var result = await new PublicPricingController(factory).GetTiers(CancellationToken.None);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var tiers = Assert.IsAssignableFrom<IEnumerable<PublicSubscriptionTier>>(ok.Value).ToList();

        var small = Assert.Single(tiers, t => t.Id == firstId);
        Assert.NotNull(small.IncludedAreas);
        Assert.Equal(
            [OrganizationPermissionArea.Cases, OrganizationPermissionArea.Calendar],
            small.IncludedAreas);

        var large = Assert.Single(tiers, t => t.Id == secondId);
        Assert.Null(large.IncludedAreas); // zero rows = everything included, said as null
    }
}
