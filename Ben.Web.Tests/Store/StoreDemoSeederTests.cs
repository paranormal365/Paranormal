using Ben.Data.WebApi.SeedData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The demo store seeds on a real database with its keys and checks enforced, twice without
/// doubling, and every picture is an ownerless store image with its metadata (storefront S1.12).
/// </summary>
public sealed class StoreDemoSeederTests
{
    private static async Task<(SqliteTestDb Db, Guid Owner)> SeededAsync(int times = 1)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        Guid owner;
        await using (var db = await sqlite.NewContextAsync())
        {
            var admin = StoreTestData.Person(db);
            StoreTestData.StoreImageType(db, admin);
            await db.SaveChangesAsync();
            owner = admin.Id;
        }
        for (var i = 0; i < times; i++)
        {
            await using var db = await sqlite.NewContextAsync();
            await StoreDemoSeeder.SeedCoreAsync(db, owner, default);
        }
        return (sqlite, owner);
    }

    [Fact]
    public async Task The_demo_store_has_its_shelves_products_and_code()
    {
        var (sqlite, _) = await SeededAsync();
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.Equal(["audio-recorders", "emf-meters", "field-accessories", "spirit-boxes", "trigger-objects"],
            await db.StoreCategories.Select(c => c.Slug).OrderBy(s => s).ToListAsync());
        Assert.Equal(7, await db.StoreProducts.CountAsync());
        Assert.False((await db.StoreProducts.SingleAsync(p => p.Slug == "boo-buddy")).IsActive);

        var bag = await db.StoreProducts.Include(p => p.Variants).SingleAsync(p => p.Slug == "investigators-field-bag");
        Assert.Equal(4, bag.Variants.Count);
        Assert.All(bag.Variants, v => Assert.Equal(20, v.StockOnHand));
        Assert.Equal((39m, 49m), (bag.MinPrice, bag.MaxPrice));
        Assert.Contains(bag.Variants, v => v.Name == "Olive / Large");

        Assert.Equal(1, (await db.StoreProductVariants.SingleAsync(v => v.Sku == "SINGLE-UNIT-PROBE")).StockOnHand);
        Assert.Equal(0, (await db.StoreProductVariants.SingleAsync(v => v.Sku == "PSB7-CAMO")).StockOnHand);
        Assert.True(await db.StoreProductImages.AnyAsync(i => i.Variant!.Sku == "PSB7-CAMO"));
        Assert.Equal(10, (await db.StoreCoupons.SingleAsync(c => c.Code == "GHOST10")).PercentOff);
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing()
    {
        var (sqlite, _) = await SeededAsync(times: 2);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.Equal((5, 7, 12, 1), (await db.StoreCategories.CountAsync(), await db.StoreProducts.CountAsync(),
            await db.StoreProductVariants.CountAsync(), await db.StoreCoupons.CountAsync()));
        Assert.Equal(13, await db.UploadFiles.CountAsync());
    }

    [Fact]
    public async Task Every_picture_is_an_ownerless_store_image_that_never_expires_with_its_metadata()
    {
        var (sqlite, owner) = await SeededAsync();
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        var files = await db.UploadFiles.AsNoTracking().ToListAsync();
        Assert.NotEmpty(files);
        Assert.All(files, f =>
        {
            Assert.Equal(UploadFileTypeSeeder.StoreImageFileTypeId, f.UploadFileTypeId);
            Assert.Equal((null, null, null, owner), (f.ExpiresAtUtc, f.AppUserId, f.OwnerOrganizationId, f.CreatedByAppUserId));
            Assert.NotNull(f.FileData);
        });
        var withMetadata = await db.UploadFileMetadata.Select(m => m.UploadFileId).ToListAsync();
        Assert.All(files, f => Assert.Contains(f.Id, withMetadata));

        var activeWithoutPicture = await db.StoreProducts.Where(p => p.IsActive && !p.Images.Any()).Select(p => p.Slug).ToListAsync();
        Assert.Empty(activeWithoutPicture);
    }
}
