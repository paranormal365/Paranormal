using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The Sansung problem: a typo in shared vocabulary, and what becomes of it.
/// </summary>
/// <remarks>
/// <para>Somebody types <b>Sansung</b> meaning <b>Samsung</b>. The unique index does not help — the
/// names genuinely differ — so the catalog gains a manufacturer nobody meant to create, and the
/// member who created it cannot remove it, because rejecting taxonomy is a SuperAdmin action.</para>
///
/// <para>Two answers, and the tests below are mostly the <b>positive</b> half of each: real names
/// that must still be accepted, and entries that must survive cleanup. Catching the typo is easy;
/// not flagging Ring as a typo of Ping, and not deleting an approved brand somebody is about to
/// use, is where the work is.</para>
/// </remarks>
public sealed class TaxonomyTypoTests
{
    // ── Spotting the typo ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sansung", "Samsung")]
    [InlineData("Canonn", "Canon")]
    [InlineData("Zooom", "Zoom")]
    [InlineData("Olympsu", "Olympus")]
    [InlineData("samsng", "Samsung")]
    public void A_near_miss_is_recognised(string typed, string existing)
        => Assert.True(NameSimilarity.IsProbableTypo(typed, existing),
            $"'{typed}' should have been offered '{existing}'.");

    /// <summary>
    /// The half that matters more. A check that flagged genuinely different manufacturers would
    /// teach people to click past the warning, which is worse than not having one.
    /// </summary>
    [Theory]
    [InlineData("Ring", "Ping")]
    [InlineData("Sony", "Sonos")]
    [InlineData("Nikon", "Canon")]
    [InlineData("Zoom", "Boom")]
    [InlineData("GoPro", "Garmin")]
    public void Two_real_names_are_not_confused(string a, string b)
        => Assert.False(NameSimilarity.IsProbableTypo(a, b),
            $"'{a}' and '{b}' are different names and should not have been flagged.");

    /// <summary>
    /// An identical name is not a "probable typo" — it is the same thing, and the exact-match path
    /// handles it before this is ever consulted.
    /// </summary>
    [Theory]
    [InlineData("Samsung", "Samsung")]
    [InlineData("samsung", "SAMSUNG")]
    [InlineData("  Samsung  ", "Samsung")]
    public void The_same_name_is_not_a_typo_of_itself(string a, string b)
        => Assert.False(NameSimilarity.IsProbableTypo(a, b));

    [Fact]
    public void Nothing_is_not_a_typo_of_anything()
    {
        Assert.False(NameSimilarity.IsProbableTypo(null, "Samsung"));
        Assert.False(NameSimilarity.IsProbableTypo("Samsung", null));
        Assert.False(NameSimilarity.IsProbableTypo("   ", "Samsung"));
    }

    // ── What happens when the item goes ──────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Seeded(Guid BrandId, Guid ModelId, Guid ItemId);

    private static async Task<Seeded> SeedAsync(
        IDbContextFactory<BenDataContext> factory, bool approved, bool withItem = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();

        var categoryId = Guid.NewGuid();
        var brandId    = Guid.NewGuid();
        var modelId    = Guid.NewGuid();
        var itemId     = Guid.NewGuid();

        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Sansung", IsApproved = approved, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "X1", IsApproved = approved, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        if (withItem)
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = itemId, OwnerAppUserId = userId, EquipmentModelId = modelId,
                DisplayName = "My recorder", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

        await db.SaveChangesAsync();
        return new Seeded(brandId, modelId, itemId);
    }

    /// <summary>
    /// Deleting the only item that used a proposed brand takes the typo with it — model first, then
    /// the brand that existed only to hold it.
    /// </summary>
    [Fact]
    public async Task Deleting_the_last_item_clears_an_unapproved_typo()
    {
        var factory = CreateFactory();
        var seeded  = await SeedAsync(factory, approved: false);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(i => i.Id == seeded.ItemId));
            await db.SaveChangesAsync();
            await TaxonomyCleanup.RemoveOrphanedTaxonomyAsync(db, seeded.ModelId, default);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.EquipmentModels.AnyAsync(m => m.Id == seeded.ModelId));
        Assert.False(await check.EquipmentBrands.AnyAsync(b => b.Id == seeded.BrandId));
    }

    /// <summary>
    /// An <b>approved</b> brand is shared vocabulary and stays. The catalog describes what exists in
    /// the world, not only what somebody happens to own this week — and a Zoom H1n is still a real
    /// recorder on the day the last owner here sells theirs.
    /// </summary>
    [Fact]
    public async Task An_approved_brand_survives_losing_its_last_item()
    {
        var factory = CreateFactory();
        var seeded  = await SeedAsync(factory, approved: true);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(i => i.Id == seeded.ItemId));
            await db.SaveChangesAsync();
            await TaxonomyCleanup.RemoveOrphanedTaxonomyAsync(db, seeded.ModelId, default);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.True(await check.EquipmentModels.AnyAsync(m => m.Id == seeded.ModelId));
        Assert.True(await check.EquipmentBrands.AnyAsync(b => b.Id == seeded.BrandId));
    }

    /// <summary>
    /// Somebody else's item keeps the taxonomy alive. Cleanup is about things nothing uses, not
    /// about the person who happened to delete last.
    /// </summary>
    [Fact]
    public async Task Taxonomy_still_in_use_by_somebody_else_is_kept()
    {
        var factory = CreateFactory();
        var seeded  = await SeedAsync(factory, approved: false);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = Guid.NewGuid(), OwnerAppUserId = Guid.NewGuid(), EquipmentModelId = seeded.ModelId,
                DisplayName = "Somebody else's", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(i => i.Id == seeded.ItemId));
            await db.SaveChangesAsync();
            await TaxonomyCleanup.RemoveOrphanedTaxonomyAsync(db, seeded.ModelId, default);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.True(await check.EquipmentModels.AnyAsync(m => m.Id == seeded.ModelId));
    }

    /// <summary>
    /// A brand with another model under it keeps the brand, even once this model goes. The typo
    /// might be in the model name alone.
    /// </summary>
    [Fact]
    public async Task A_brand_with_another_model_is_kept()
    {
        var factory = CreateFactory();
        var seeded  = await SeedAsync(factory, approved: false);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var category = await db.EquipmentCategories.FirstAsync();
            db.EquipmentModels.Add(new EquipmentModel
            {
                Id = Guid.NewGuid(), EquipmentBrandId = seeded.BrandId, EquipmentCategoryId = category.Id,
                Name = "X2", IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(i => i.Id == seeded.ItemId));
            await db.SaveChangesAsync();
            await TaxonomyCleanup.RemoveOrphanedTaxonomyAsync(db, seeded.ModelId, default);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.EquipmentModels.AnyAsync(m => m.Id == seeded.ModelId));
        Assert.True(await check.EquipmentBrands.AnyAsync(b => b.Id == seeded.BrandId));
    }
}
