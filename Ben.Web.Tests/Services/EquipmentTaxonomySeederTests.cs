using Ben.Data.Source.Context;
using Ben.Data.WebApi.SeedData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for <see cref="EquipmentTaxonomySeeder.SeedIntoAsync"/> — categories and the
/// "Generic / Unbranded" brand + one generic model per category.
/// </summary>
/// <remarks>
/// The rule under test is the <c>ContactTypeSeeder</c> lesson quoted in the seeder's own doc
/// comment: an empty category picker is a feature dead on arrival, so seeding must run and must
/// be safe to run every startup without duplicating rows.
/// </remarks>
public class EquipmentTaxonomySeederTests
{
    private static BenDataContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BenDataContext(opts);
    }

    [Fact]
    public async Task SeedIntoAsync_CreatesCategories()
    {
        await using var db = CreateDb();
        await EquipmentTaxonomySeeder.SeedIntoAsync(db, Guid.NewGuid());

        var count = await db.EquipmentCategories.CountAsync();
        Assert.True(count > 0);
        Assert.Contains(await db.EquipmentCategories.ToListAsync(), c => c.Name == "Audio Recorder");
    }

    [Fact]
    public async Task SeedIntoAsync_IsIdempotent_DoesNotDuplicateCategories()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();

        await EquipmentTaxonomySeeder.SeedIntoAsync(db, ownerId);
        var firstCount = await db.EquipmentCategories.CountAsync();

        await EquipmentTaxonomySeeder.SeedIntoAsync(db, ownerId);
        var secondCount = await db.EquipmentCategories.CountAsync();

        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public async Task SeedIntoAsync_CreatesOneGenericModelPerCategory_AllApproved()
    {
        await using var db = CreateDb();
        await EquipmentTaxonomySeeder.SeedIntoAsync(db, Guid.NewGuid());

        var categoryCount = await db.EquipmentCategories.CountAsync();
        var genericBrand = await db.EquipmentBrands.SingleAsync(b => b.Name == "Generic / Unbranded");
        Assert.True(genericBrand.IsApproved);

        var genericModels = await db.EquipmentModels
            .Where(m => m.EquipmentBrandId == genericBrand.Id)
            .ToListAsync();
        Assert.Equal(categoryCount, genericModels.Count);
        Assert.All(genericModels, m => Assert.True(m.IsApproved));
    }

    [Fact]
    public async Task SeedIntoAsync_IsIdempotent_DoesNotDuplicateGenericBrandOrModels()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();

        await EquipmentTaxonomySeeder.SeedIntoAsync(db, ownerId);
        await EquipmentTaxonomySeeder.SeedIntoAsync(db, ownerId);

        Assert.Equal(1, await db.EquipmentBrands.CountAsync(b => b.Name == "Generic / Unbranded"));
        var genericBrand = await db.EquipmentBrands.SingleAsync(b => b.Name == "Generic / Unbranded");
        var modelCount = await db.EquipmentModels.CountAsync(m => m.EquipmentBrandId == genericBrand.Id);
        var categoryCount = await db.EquipmentCategories.CountAsync();
        Assert.Equal(categoryCount, modelCount);
    }

    [Fact]
    public async Task SeedIntoAsync_OnSecondCallWithNewCategory_AddsGenericModelForIt()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();
        await EquipmentTaxonomySeeder.SeedIntoAsync(db, ownerId);

        // Simulate an admin adding a brand-new category between seeder runs.
        db.EquipmentCategories.Add(new Ben.Data.Source.Entities.EquipmentCategory
        {
            Id = Guid.NewGuid(), Name = "Brand New Category", SortOrder = 999, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();

        await EquipmentTaxonomySeeder.SeedIntoAsync(db, ownerId);

        var genericBrand = await db.EquipmentBrands.SingleAsync(b => b.Name == "Generic / Unbranded");
        Assert.True(await db.EquipmentModels.AnyAsync(m =>
            m.EquipmentBrandId == genericBrand.Id && m.Name == "Brand New Category"));
    }
}
