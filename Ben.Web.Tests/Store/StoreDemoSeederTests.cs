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
    private static async Task<(SqliteTestDb Db, Guid Owner)> SeededAsync(int times = 1, bool withPeople = false)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        Guid owner;
        await using (var db = await sqlite.NewContextAsync())
        {
            var admin = StoreTestData.Person(db);
            StoreTestData.StoreImageType(db, admin);
            if (withPeople)
            {
                foreach (var (email, first) in new[] { (StoreDemoSeeder.SarahEmail, "Sarah"), (StoreDemoSeeder.JamesEmail, "James") })
                    db.AppUsers.Add(new Ben.Data.Source.Entities.AppUser
                    {
                        Id = Guid.NewGuid(), UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(),
                        FirstName = first, LastName = "Demo", DateCreated = DateTime.UtcNow,
                    });
                // As the development seeders make people: a display name, no first or last name.
                db.AppUsers.Add(new Ben.Data.Source.Entities.AppUser
                {
                    Id = Guid.NewGuid(), UserName = StoreDemoSeeder.EmmaEmail, Email = StoreDemoSeeder.EmmaEmail,
                    NormalizedEmail = StoreDemoSeeder.EmmaEmail.ToUpperInvariant(), DisplayName = "Emma Rodriguez", DateCreated = DateTime.UtcNow,
                });
            }
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
        Assert.Equal(9, await db.StoreProducts.CountAsync());   // seven, and the two versions of the Field Thermometer (P13)
        Assert.False((await db.StoreProducts.SingleAsync(p => p.Slug == "boo-buddy")).IsActive);
        var (v1, v2) = (await db.StoreProducts.SingleAsync(p => p.Slug == "field-thermometer"), await db.StoreProducts.SingleAsync(p => p.Slug == "field-thermometer-v2"));
        Assert.Equal((false, true, v1.Id, true), (v1.IsActive, v1.DiscontinuedUtc is not null, v2.PreviousVersionProductId, v2.IsActive));

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

        Assert.Equal((5, 9, 14, 1), (await db.StoreCategories.CountAsync(), await db.StoreProducts.CountAsync(),
            await db.StoreProductVariants.CountAsync(), await db.StoreCoupons.CountAsync()));
        Assert.Equal(15, await db.UploadFiles.CountAsync());   // one picture each for the two Field Thermometers (P13)
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

    /// <summary>A shelf picture from an older seed (it carried words the hero now draws) is repainted,
    /// even when a migration has moved its bytes to disk.</summary>
    [Fact]
    public async Task An_old_shelf_picture_is_repainted_and_a_current_one_left_alone()
    {
        var (sqlite, owner) = await SeededAsync();
        await using var _d = sqlite;
        var shelfPicture = new Guid("a1000000-0000-0000-0000-000000000011");
        byte[] fresh;
        await using (var db = await sqlite.NewContextAsync())
        {
            var file = await db.UploadFiles.SingleAsync(f => f.Id == shelfPicture);
            fresh = file.FileData!;
            file.FileName = "emf-meters.jpg";
            file.FileData = null;
            file.StoragePath = "store/old/emf-meters.jpg";
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync()) await StoreDemoSeeder.SeedCoreAsync(db, owner, default);

        await using var check = await sqlite.NewContextAsync();
        var after = await check.UploadFiles.AsNoTracking().SingleAsync(f => f.Id == shelfPicture);
        Assert.Equal(fresh, after.FileData);
        Assert.Equal((fresh.LongLength, (string?)null, "emf-meters-shelf-v2.jpg"), (after.FileSize, after.StoragePath, after.FileName));

        // A second run leaves the current picture exactly as it is.
        await using (var db = await sqlite.NewContextAsync())
        {
            await db.UploadFiles.Where(f => f.Id == shelfPicture).ExecuteUpdateAsync(u => u.SetProperty(f => f.StoragePath, "store/moved.jpg"));
            await StoreDemoSeeder.SeedCoreAsync(db, owner, default);
        }
        await using var again = await sqlite.NewContextAsync();
        Assert.Equal("store/moved.jpg", (await again.UploadFiles.AsNoTracking().SingleAsync(f => f.Id == shelfPicture)).StoragePath);
    }

    // ── Orders (S4.12) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Orders_balance_to_the_cent()
    {
        var (sqlite, _) = await SeededAsync(withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();
        var orders = await db.StoreOrders.Include(o => o.Items).ToListAsync();

        Assert.Equal(9, orders.Count);
        Assert.All(orders, o =>
        {
            Assert.Equal(o.Total, o.Subtotal - o.DiscountAmount + o.ShippingAmount + o.TaxAmount);
            Assert.Equal(o.Subtotal, o.Items.Sum(i => i.LineTotal));
            Assert.Equal(o.DiscountAmount, o.Items.Sum(i => i.LineDiscount));
            Assert.Equal(o.TaxAmount, o.Items.Sum(i => i.TaxAmount) + o.ShippingTaxAmount);
            Assert.Equal(Ben.Service.Models.Store.StoreMoney.Cents(o.Total),
                Ben.Service.Models.Store.StoreMoney.Cents(o.Subtotal) - Ben.Service.Models.Store.StoreMoney.Cents(o.DiscountAmount)
                + Ben.Service.Models.Store.StoreMoney.Cents(o.ShippingAmount) + Ben.Service.Models.Store.StoreMoney.Cents(o.TaxAmount));
            Assert.True(o.Total > 0);
        });
        // The shapes the order pages draw: tax on shipping, a code, a refund, tracking.
        Assert.Contains(orders, o => o.ShippingTaxAmount > 0);
        Assert.Contains(orders, o => o.CouponCode == StoreDemoSeeder.CouponCode && o.DiscountAmount > 0);
        Assert.Contains(orders, o => o.Status == Ben.Data.Common.Enums.StoreOrderStatus.Refunded && o.RefundedAmount == o.Total);
        Assert.Contains(await db.StoreOrderParcels.ToListAsync(), x => x.Status == Ben.Data.Common.Enums.StoreParcelStatus.Shipped && x.TrackingUrl != null);
        Assert.Single(orders, o => o.BuyerAppUserId == null);
    }

    [Fact]
    public async Task Sarah_has_one_abandoned_checkout()
    {
        var (sqlite, _) = await SeededAsync(withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();
        var sarahs = await db.StoreOrders.Where(o => o.BuyerEmailNormalized == StoreDemoSeeder.SarahEmail.ToUpperInvariant()).ToListAsync();

        Assert.Equal(5, sarahs.Count);
        var abandoned = Assert.Single(sarahs, o => o.PaidUtc is null);
        Assert.Equal((Ben.Data.Common.Enums.StoreOrderStatus.PendingPayment, StoreDemoSeeder.SeededOrders.SarahAbandoned), (abandoned.Status, abandoned.Id));
        Assert.NotNull(abandoned.ReservationReleasedUtc);
        Assert.Null(abandoned.StripePaymentIntentId);
    }

    /// <summary>History, not sales: nothing seeded is picked up by the tax retry job or the expiry service.</summary>
    [Fact]
    public async Task The_jobs_leave_the_seeded_orders_alone()
    {
        var (sqlite, _) = await SeededAsync(withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await db.StoreOrders.AnyAsync(o => o.PaidUtc != null && o.StripeTaxTransactionId == null && o.StripeTaxCalculationId != null));
        Assert.False(await db.StoreOrders.AnyAsync(o => o.Status == Ben.Data.Common.Enums.StoreOrderStatus.PendingPayment && o.ReservationReleasedUtc == null));
        Assert.All(await db.StoreProductVariants.Where(v => v.Sku == "KII-EMF" || v.Sku == "BAG-OLV-STD").ToListAsync(),
            v => Assert.Equal(0, v.StockReserved));
    }

    [Fact]
    public async Task Seeding_orders_twice_adds_none_and_counts_the_code_once()
    {
        var (sqlite, _) = await SeededAsync(times: 2, withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.Equal(9, await db.StoreOrders.CountAsync());
        Assert.Single(await db.StoreCouponRedemptions.ToListAsync());
        Assert.Equal(1, (await db.StoreCoupons.SingleAsync(c => c.Code == StoreDemoSeeder.CouponCode)).RedemptionCount);
        Assert.Equal(9, (await db.StoreOrders.Select(o => o.OrderNumber).Distinct().ToListAsync()).Count);
    }

    [Fact]
    public async Task Without_the_demo_people_only_the_guest_order_is_seeded()
    {
        var (sqlite, _) = await SeededAsync();
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        var only = Assert.Single(await db.StoreOrders.ToListAsync());
        Assert.Equal(StoreDemoSeeder.SeededOrders.Guest, only.Id);
    }

    // ── Reviews and favourites (S6.4) ─────────────────────────────────────────

    [Fact]
    public async Task Review_caches_equal_4_00_over_2_after_seeding()
    {
        var (sqlite, _) = await SeededAsync(times: 2, withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        var kii = await db.StoreProducts.SingleAsync(p => p.Slug == "k-ii-emf-meter");
        Assert.Equal(4.00m, kii.AverageRating);
        Assert.Equal(2, kii.ReviewCount);

        var reviews = await db.StoreReviews.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(3, reviews.Count);   // twice seeded, still three
        var pending = Assert.Single(reviews, r => r.Status == Ben.Data.Common.Enums.StoreReviewStatus.Pending);
        Assert.Equal(StoreDemoSeeder.SeededReviews.SarahKii, pending.Id);
        Assert.Single(reviews, r => r.AdminReply != null);
        Assert.All(reviews.Where(r => r.Status == Ben.Data.Common.Enums.StoreReviewStatus.Approved), r => Assert.Equal(1, r.HelpfulCount));
        Assert.Equal(2, await db.StoreReviewVotes.CountAsync());
    }

    [Fact]
    public async Task Every_seeded_review_hangs_on_an_order_its_author_paid_for_that_holds_the_product()
    {
        var (sqlite, _) = await SeededAsync(withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        foreach (var review in await db.StoreReviews.ToListAsync())
            Assert.True(await db.StoreOrders.AnyAsync(o => o.Id == review.OrderId && o.BuyerAppUserId == review.AuthorAppUserId
                && o.PaidUtc != null && o.Items.Any(i => i.ProductId == review.ProductId)), review.Title);
        Assert.False(await db.StoreReviewVotes.AnyAsync(v => v.Review.AuthorAppUserId == v.AppUserId));
    }

    [Fact]
    public async Task Sarah_keeps_two_favourites_once()
    {
        var (sqlite, _) = await SeededAsync(times: 2, withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.Equal(["h1n-handy-recorder", "rem-pod"],
            await db.StoreFavourites.Select(f => f.Product.Slug).OrderBy(s => s).ToListAsync());
    }

    [Fact]
    public async Task Without_the_demo_people_there_are_no_reviews()
    {
        var (sqlite, _) = await SeededAsync();
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.Empty(await db.StoreReviews.ToListAsync());
        Assert.Equal((0m, 0), await db.StoreProducts.Where(p => p.Slug == "k-ii-emf-meter").Select(p => new ValueTuple<decimal, int>(p.AverageRating, p.ReviewCount)).SingleAsync());
    }

    [Fact]
    public async Task An_order_is_addressed_to_the_person_by_the_name_the_site_shows()
    {
        var (sqlite, _) = await SeededAsync(withPeople: true);
        await using var _d = sqlite;
        await using var db = await sqlite.NewContextAsync();

        var emmas = await db.StoreOrders.SingleAsync(o => o.Id == StoreDemoSeeder.SeededOrders.EmmaKii);
        Assert.Equal(("Emma Rodriguez", "Emma Rodriguez"), (emmas.BuyerName, emmas.ShipName));
        var sarahs = await db.StoreOrders.FirstAsync(o => o.Id == StoreDemoSeeder.SeededOrders.SarahPaid);
        Assert.Equal("Sarah Demo", sarahs.ShipName);   // no display name: first and last
    }

    [Fact]
    public async Task The_demo_seller_has_one_item_on_sale_and_one_draft_once()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var _d = sqlite;
        Guid owner, hazel = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            var admin = StoreTestData.Person(db);
            StoreTestData.StoreImageType(db, admin);
            db.AppUsers.Add(new Ben.Data.Source.Entities.AppUser
            {
                Id = hazel, UserName = StoreDemoSeeder.HazelEmail, Email = StoreDemoSeeder.HazelEmail,
                NormalizedEmail = StoreDemoSeeder.HazelEmail.ToUpperInvariant(), DisplayName = "Hazel Marsh", DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            owner = admin.Id;
        }
        for (var i = 0; i < 2; i++)
        {
            await using var db = await sqlite.NewContextAsync();
            await StoreDemoSeeder.SeedCoreAsync(db, owner, default);
        }

        await using var check = await sqlite.NewContextAsync();
        var hers = await check.StoreProducts.Where(p => p.SellerAppUserId == hazel).OrderBy(p => p.Name).ToListAsync();
        Assert.Equal([("Hand-Built REM Pod", true), ("Pocket EMF Logger", false)], hers.Select(p => (p.Name, p.IsActive)));
        Assert.Equal(11, await check.StoreProducts.CountAsync());   // the store's nine, and hers
    }
}
