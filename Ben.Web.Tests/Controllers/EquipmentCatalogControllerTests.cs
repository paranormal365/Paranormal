using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System.Security.Claims;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="EquipmentCatalogController"/> (public read + propose) and
/// <see cref="AdminEquipmentTaxonomyController"/> (SuperAdmin moderation).
/// </summary>
/// <remarks>
/// The rule under test: the public, anonymous catalog shows only approved entries; a proposer can
/// keep using their own unapproved entry immediately, but nobody else's pending work is visible.
/// Dedupe-by-name means proposing the same brand twice returns the same row, not a second one.
/// </remarks>
public class EquipmentCatalogControllerTests
{
    private static readonly Guid ProposerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private static EquipmentCatalogController BuildPublic(IDbContextFactory<BenDataContext> f)
        => new(f) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static EquipmentCatalogController BuildAuthed(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };

    private static AdminEquipmentTaxonomyController BuildAdmin(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer"))
                }
            }
        };

    private static async Task<Guid> SeedCategoryAsync(TestDbFactoryWrapper w)
    {
        var categoryId = Guid.NewGuid();
        await using var db = await w.Factory.CreateDbContextAsync();
        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "EMF Meter", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId });
        await db.SaveChangesAsync();
        return categoryId;
    }

    private sealed record TestDbFactoryWrapper(IDbContextFactory<BenDataContext> Factory);

    private static async Task<TestDbFactoryWrapper> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = ProposerId, UserName = "p@t", Email = "p@t", DisplayName = "Proposer" });
        db.Users.Add(new AppUser { Id = OtherUserId, UserName = "o@t", Email = "o@t", DisplayName = "Other" });
        db.Users.Add(new AppUser { Id = AdminId, UserName = "a@t", Email = "a@t", DisplayName = "Admin" });
        await db.SaveChangesAsync();
        return new TestDbFactoryWrapper(factory);
    }

    [Fact]
    public async Task GetBrands_Anonymous_OnlyReturnsApproved()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentBrands.Add(new EquipmentBrand { Id = Guid.NewGuid(), Name = "Approved Co", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId });
            db.EquipmentBrands.Add(new EquipmentBrand { Id = Guid.NewGuid(), Name = "Pending Co", IsApproved = false, ProposedByAppUserId = ProposerId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ProposerId });
            await db.SaveChangesAsync();
        }

        var result = await BuildPublic(w.Factory).GetBrands(null, default);
        var brands = Assert.IsAssignableFrom<IEnumerable<EquipmentBrandRecord>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(brands);
        Assert.Equal("Approved Co", brands.Single().Name);
    }

    [Fact]
    public async Task GetBrands_Proposer_SeesOwnPendingEntry_ButNotSomeoneElses()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentBrands.Add(new EquipmentBrand { Id = Guid.NewGuid(), Name = "My Pending Co", IsApproved = false, ProposedByAppUserId = ProposerId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ProposerId });
            db.EquipmentBrands.Add(new EquipmentBrand { Id = Guid.NewGuid(), Name = "Their Pending Co", IsApproved = false, ProposedByAppUserId = OtherUserId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OtherUserId });
            await db.SaveChangesAsync();
        }

        var result = await BuildAuthed(w.Factory, ProposerId).GetBrands(null, default);
        var brands = Assert.IsAssignableFrom<IEnumerable<EquipmentBrandRecord>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(brands);
        Assert.Equal("My Pending Co", brands.Single().Name);
    }

    [Fact]
    public async Task ProposeBrand_ByOrdinaryUser_StartsUnapproved()
    {
        var w = await SeedAsync();
        var ctrl = BuildAuthed(w.Factory, ProposerId);
        var result = await ctrl.ProposeBrand(new UpsertEquipmentBrandRequest("New Brand"), default);
        var record = Assert.IsType<EquipmentBrandRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(record.IsApproved);
        Assert.Equal(ProposerId, record.ProposedByAppUserId);
    }

    [Fact]
    public async Task ProposeBrand_BySuperAdmin_IsAutoApproved()
    {
        var w = await SeedAsync();
        var ctrl = new EquipmentCatalogController(w.Factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer"))
                }
            }
        };
        var result = await ctrl.ProposeBrand(new UpsertEquipmentBrandRequest("Admin Brand"), default);
        var record = Assert.IsType<EquipmentBrandRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(record.IsApproved);
    }

    [Fact]
    public async Task ProposeBrand_TwiceWithSameName_ReturnsTheSameRow_NotADuplicate()
    {
        var w = await SeedAsync();
        var ctrl = BuildAuthed(w.Factory, ProposerId);
        var first  = (EquipmentBrandRecord)((OkObjectResult)(await ctrl.ProposeBrand(new UpsertEquipmentBrandRequest("Zoom"), default)).Result!).Value!;
        var second = (EquipmentBrandRecord)((OkObjectResult)(await ctrl.ProposeBrand(new UpsertEquipmentBrandRequest("Zoom"), default)).Result!).Value!;
        Assert.Equal(first.Id, second.Id);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.EquipmentBrands.CountAsync(b => b.Name == "Zoom"));
    }

    [Fact]
    public async Task ProposeModel_UnderUnknownBrand_ReturnsBadRequest()
    {
        var w = await SeedAsync();
        var categoryId = await SeedCategoryAsync(w);
        var ctrl = BuildAuthed(w.Factory, ProposerId);
        var result = await ctrl.ProposeModel(new UpsertEquipmentModelRequest(Guid.NewGuid(), categoryId, "Ghost", null, null), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AdminApproveBrand_MakesItVisibleAnonymously()
    {
        var w = await SeedAsync();
        Guid brandId;
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var brand = new EquipmentBrand { Id = Guid.NewGuid(), Name = "Pending Co", IsApproved = false, ProposedByAppUserId = ProposerId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ProposerId };
            db.EquipmentBrands.Add(brand);
            await db.SaveChangesAsync();
            brandId = brand.Id;
        }

        var approveResult = await BuildAdmin(w.Factory).ApproveBrand(brandId, default);
        Assert.True(((EquipmentBrandRecord)((OkObjectResult)approveResult.Result!).Value!).IsApproved);

        var publicResult = await BuildPublic(w.Factory).GetBrands(null, default);
        var brands = Assert.IsAssignableFrom<IEnumerable<EquipmentBrandRecord>>(Assert.IsType<OkObjectResult>(publicResult.Result).Value);
        Assert.Single(brands);
    }

    [Fact]
    public async Task AdminRejectBrand_WithModelsAttached_ReturnsConflict()
    {
        var w = await SeedAsync();
        var categoryId = await SeedCategoryAsync(w);
        Guid brandId;
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var brand = new EquipmentBrand { Id = Guid.NewGuid(), Name = "Has Models Co", IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ProposerId };
            db.EquipmentBrands.Add(brand);
            db.EquipmentModels.Add(new EquipmentModel
            {
                Id = Guid.NewGuid(), EquipmentBrandId = brand.Id, EquipmentCategoryId = categoryId,
                Name = "M1", IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ProposerId,
            });
            await db.SaveChangesAsync();
            brandId = brand.Id;
        }

        var result = await BuildAdmin(w.Factory).RejectBrand(brandId, default);
        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task AdminRejectModel_WithNoItemsAttached_Deletes()
    {
        var w = await SeedAsync();
        var categoryId = await SeedCategoryAsync(w);
        Guid brandId, modelId;
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var brand = new EquipmentBrand { Id = Guid.NewGuid(), Name = "Brand", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId };
            db.EquipmentBrands.Add(brand);
            var model = new EquipmentModel
            {
                Id = Guid.NewGuid(), EquipmentBrandId = brand.Id, EquipmentCategoryId = categoryId,
                Name = "Rejectable", IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ProposerId,
            };
            db.EquipmentModels.Add(model);
            await db.SaveChangesAsync();
            brandId = brand.Id; modelId = model.Id;
        }

        var result = await BuildAdmin(w.Factory).RejectModel(modelId, default);
        Assert.IsType<NoContentResult>(result);

        await using var checkDb = await w.Factory.CreateDbContextAsync();
        Assert.False(await checkDb.EquipmentModels.AnyAsync(m => m.Id == modelId));
    }
}
