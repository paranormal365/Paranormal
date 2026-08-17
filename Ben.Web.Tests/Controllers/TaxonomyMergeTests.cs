using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Correcting a name in the shared catalog: renaming, and the merge a rename sometimes is.
/// </summary>
/// <remarks>
/// <para>Ben's second question — <i>"what happens when I try to change Samsung to Sansung?"</i> —
/// had no answer at all before this, because nothing could rename. A collision is now <b>offered</b>
/// as a merge rather than performed: two manufacturers becoming one changes what make somebody's
/// equipment is, which is a large thing to have happen because a name was typed.</para>
///
/// <para>The tests lean on the positive side deliberately. That a merge is refused when it would
/// lose an approval is easy; that the items actually arrive at the surviving model, that a
/// same-named model on both sides does not break the unique index, and that an ordinary rename
/// still just renames — that is where a merge tool is either usable on real data or not.</para>
/// </remarks>
public sealed class TaxonomyMergeTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminEquipmentTaxonomyController Build(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                        new Claim(ClaimTypes.Role, RoleNames.SuperAdmin),
                    ], "Bearer"))
                }
            }
        };

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid CategoryId, Guid SamsungId, Guid SansungId, Guid SansungModelId, Guid ItemId);

    /// <summary>The real brand, the typo, and one item sitting under the typo.</summary>
    private static async Task<World> SeedAsync(bool samsungApproved = true, bool sansungApproved = false)
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        var categoryId = Guid.NewGuid();
        var samsungId  = Guid.NewGuid();
        var sansungId  = Guid.NewGuid();
        var modelId    = Guid.NewGuid();
        var itemId     = Guid.NewGuid();

        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId });

        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = samsungId, Name = "Samsung", IsApproved = samsungApproved, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = sansungId, Name = "Sansung", IsApproved = sansungApproved, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId });

        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = sansungId, EquipmentCategoryId = categoryId,
            Name = "X1", IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = itemId, OwnerAppUserId = AdminId, EquipmentModelId = modelId,
            DisplayName = "My recorder", DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        await db.SaveChangesAsync();
        return new World(factory, categoryId, samsungId, sansungId, modelId, itemId);
    }

    // ── Renaming ─────────────────────────────────────────────────────────────

    /// <summary>An ordinary correction is just a rename, and everything under it comes along.</summary>
    [Fact]
    public async Task Renaming_to_an_unused_name_simply_renames()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory).RenameBrand(
            w.SansungId, new UpsertEquipmentBrandRequest("Sansui"), default);
        var record = Assert.IsType<EquipmentBrandRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Sansui", record.Name);

        await using var db = await w.Factory.CreateDbContextAsync();
        // The model and its item are untouched — a rename moves nothing.
        var model = await db.EquipmentModels.AsNoTracking().SingleAsync(m => m.Id == w.SansungModelId);
        Assert.Equal(w.SansungId, model.EquipmentBrandId);
        Assert.True(await db.EquipmentItems.AnyAsync(i => i.EquipmentModelId == w.SansungModelId));
    }

    /// <summary>
    /// Renaming the typo onto the real name is refused, and the refusal carries what would happen
    /// and which brand it collided with — so the caller can choose the merge rather than guess.
    /// </summary>
    [Fact]
    public async Task Renaming_onto_an_existing_name_offers_the_merge_instead()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory).RenameBrand(
            w.SansungId, new UpsertEquipmentBrandRequest("Samsung"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var offer = Assert.IsType<TaxonomyMergeOffer>(conflict.Value);

        Assert.Equal(w.SansungId, offer.SourceId);
        Assert.Equal(w.SamsungId, offer.TargetId);
        Assert.Contains("cannot be undone", offer.Message);

        // And nothing happened yet.
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal("Sansung", (await db.EquipmentBrands.AsNoTracking().SingleAsync(b => b.Id == w.SansungId)).Name);
    }

    [Fact]
    public async Task A_name_that_differs_only_in_case_is_the_same_name()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory).RenameBrand(
            w.SansungId, new UpsertEquipmentBrandRequest("samsung"), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    // ── Merging ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point: after merging, the item is a Samsung and the typo is gone.
    /// </summary>
    [Fact]
    public async Task Merging_moves_everything_across_and_removes_the_duplicate()
    {
        var w = await SeedAsync();

        Assert.IsType<NoContentResult>(
            await Build(w.Factory).MergeBrand(w.SansungId, w.SamsungId, default));

        await using var db = await w.Factory.CreateDbContextAsync();

        Assert.False(await db.EquipmentBrands.AnyAsync(b => b.Id == w.SansungId));

        var model = await db.EquipmentModels.AsNoTracking().SingleAsync(m => m.Id == w.SansungModelId);
        Assert.Equal(w.SamsungId, model.EquipmentBrandId);

        // The item never moved model, and its model is now under the right make.
        var item = await db.EquipmentItems.AsNoTracking().SingleAsync(i => i.Id == w.ItemId);
        Assert.Equal(w.SansungModelId, item.EquipmentModelId);
    }

    /// <summary>
    /// Both brands having an "X1" would break the unique index on (brand, name). The duplicate's
    /// items move to the survivor instead — the same merge one level down.
    /// </summary>
    [Fact]
    public async Task A_model_name_on_both_sides_is_merged_rather_than_colliding()
    {
        var w = await SeedAsync();

        var survivingModelId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentModels.Add(new EquipmentModel
            {
                Id = survivingModelId, EquipmentBrandId = w.SamsungId, EquipmentCategoryId = w.CategoryId,
                Name = "X1", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(
            await Build(w.Factory).MergeBrand(w.SansungId, w.SamsungId, default));

        await using var check = await w.Factory.CreateDbContextAsync();

        // One X1 left, and the item is on it.
        var models = await check.EquipmentModels.AsNoTracking()
            .Where(m => m.EquipmentBrandId == w.SamsungId && m.Name == "X1").ToListAsync();
        Assert.Single(models);
        Assert.Equal(survivingModelId, models[0].Id);

        var item = await check.EquipmentItems.AsNoTracking().SingleAsync(i => i.Id == w.ItemId);
        Assert.Equal(survivingModelId, item.EquipmentModelId);
    }

    /// <summary>
    /// The direction guard. Somebody correcting a typo has the two the wrong way round more often
    /// than not, and the result would be a catalog where the endorsed name vanished.
    /// </summary>
    [Fact]
    public async Task Merging_an_approved_brand_into_an_unapproved_one_is_refused()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory).MergeBrand(w.SamsungId, w.SansungId, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("other way", Assert.IsType<string>(conflict.Value));

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.EquipmentBrands.AnyAsync(b => b.Id == w.SamsungId));
    }

    /// <summary>
    /// Two unapproved brands merge in either direction — there is no approval to lose.
    /// </summary>
    [Fact]
    public async Task Two_unapproved_brands_merge_freely()
    {
        var w = await SeedAsync(samsungApproved: false);

        Assert.IsType<NoContentResult>(
            await Build(w.Factory).MergeBrand(w.SamsungId, w.SansungId, default));
    }

    [Fact]
    public async Task A_brand_cannot_be_merged_into_itself()
        => Assert.IsType<BadRequestObjectResult>(
            await Build((await SeedAsync()).Factory).MergeBrand(AdminId, AdminId, default));

    // ── Models ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merging_models_moves_the_items_and_removes_the_duplicate()
    {
        var w = await SeedAsync();

        var targetModelId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentModels.Add(new EquipmentModel
            {
                Id = targetModelId, EquipmentBrandId = w.SansungId, EquipmentCategoryId = w.CategoryId,
                Name = "X1 Mk2", IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(
            await Build(w.Factory).MergeModel(w.SansungModelId, targetModelId, default));

        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.False(await check.EquipmentModels.AnyAsync(m => m.Id == w.SansungModelId));
        Assert.Equal(targetModelId,
            (await check.EquipmentItems.AsNoTracking().SingleAsync(i => i.Id == w.ItemId)).EquipmentModelId);
    }

    /// <summary>
    /// Across makes this would silently change what somebody's equipment is. That may be a fair
    /// correction, but it is the brand merge's decision, not this one's.
    /// </summary>
    [Fact]
    public async Task Models_under_different_makes_are_not_merged_here()
    {
        var w = await SeedAsync();

        var otherModelId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentModels.Add(new EquipmentModel
            {
                Id = otherModelId, EquipmentBrandId = w.SamsungId, EquipmentCategoryId = w.CategoryId,
                Name = "Totally Different", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(w.Factory).MergeModel(w.SansungModelId, otherModelId, default);
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
