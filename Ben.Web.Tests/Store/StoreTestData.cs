using System.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Real rows for the store tests on <see cref="SqliteTestDb"/>, where foreign keys and CHECKs are
/// enforced — every builder here writes the parents its row needs.
/// </summary>
internal static class StoreTestData
{
    public static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    public static AppUser Person(BenDataContext db, string name = "admin")
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"{name}-{Guid.NewGuid():N}@store.test", DateCreated = Now };
        db.AppUsers.Add(user);
        return user;
    }

    /// <summary>A sellable variant: it, its product and its category all active.</summary>
    public static StoreProductVariant Variant(
        BenDataContext db, AppUser admin, int onHand = 12, int reserved = 0, decimal price = 59.99m,
        string? sku = null, bool categoryActive = true, bool productActive = true, bool variantActive = true)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var category = new StoreCategory
        {
            Id = Guid.NewGuid(), Name = $"EMF {slug}", Slug = $"emf-{slug}", IsActive = categoryActive,
            DateCreated = Now, CreatedByAppUserId = admin.Id,
        };
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"K-II {slug}", Slug = $"k-ii-{slug}",
            IsActive = productActive, MinPrice = price, MaxPrice = price, DateCreated = Now, CreatedByAppUserId = admin.Id,
        };
        var variant = new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Sku = sku ?? $"KII-{slug}".ToUpperInvariant(), Price = price,
            StockOnHand = onHand, StockReserved = reserved, IsActive = variantActive, IsDefault = true,
            DateCreated = Now, CreatedByAppUserId = admin.Id,
        };
        db.StoreCategories.Add(category);
        db.StoreProducts.Add(product);
        db.StoreProductVariants.Add(variant);
        return variant;
    }

    public static StoreCoupon Coupon(
        BenDataContext db, AppUser admin, StoreCouponKind kind = StoreCouponKind.Percent, int? percentOff = 10,
        decimal? amountOff = null, int? maxRedemptions = null, int? perBuyer = 1)
    {
        var coupon = new StoreCoupon
        {
            Id = Guid.NewGuid(), Code = $"GHOST{Guid.NewGuid():N}"[..12].ToUpperInvariant(), Name = "Test code",
            Kind = kind, PercentOff = kind == StoreCouponKind.Percent ? percentOff : null,
            AmountOff = kind == StoreCouponKind.Fixed ? amountOff : null,
            MaxRedemptions = maxRedemptions, MaxRedemptionsPerBuyer = perBuyer, IsActive = true,
            DateCreated = Now, CreatedByAppUserId = admin.Id,
        };
        db.StoreCoupons.Add(coupon);
        return coupon;
    }

    public static StoreOrder Order(
        BenDataContext db, StoreOrderStatus status = StoreOrderStatus.PendingPayment, AppUser? buyer = null,
        string email = "sarah@example.com", int? number = null, decimal total = 59.99m)
    {
        var order = new StoreOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = number ?? Random.Shared.Next(200_000, 900_000),
            Status = status,
            BuyerAppUserId = buyer?.Id,
            BuyerEmail = email,
            BuyerEmailNormalized = Ben.Data.WebApi.Services.Store.StoreEmail.Normalize(email),
            BuyerName = "Sarah Hollow",
            ShipName = "Sarah Hollow",
            ShipPhone = "615-555-0100",
            ShipStreet1 = "13 Crossroads Lane",
            ShipStreet2 = "Apt 2",
            ShipCity = "Nashville",
            ShipState = "TN",
            ShipZip = "37203",
            ShipCountry = "US",
            BillingSameAsShipping = false,
            BillName = "Sarah Hollow",
            BillCompany = "Hollow Investigations",
            BillStreet1 = "1 Bill Street",
            BillCity = "Nashville",
            BillState = "TN",
            BillZip = "37203",
            BillCountry = "US",
            Subtotal = total,
            Total = total,
            Currency = "usd",
            CartFingerprint = new string('a', 64),
            PlacedFromIp = "203.0.113.9",
            AccessToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            PlacedUtc = Now,
            DateCreated = Now,
        };
        db.StoreOrders.Add(order);
        return order;
    }
}

/// <summary>
/// Runs <see cref="Competitor"/> once, just before the next <paramref name="verb"/> statement on
/// <paramref name="table"/> executes — the window two real requests would share, made deterministic.
/// </summary>
internal sealed class RunBeforeStatement(string verb, string table) : DbCommandInterceptor
{
    public Func<Task>? Competitor;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Competitor is { } competitor
            && command.CommandText.TrimStart().StartsWith(verb, StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains(table, StringComparison.Ordinal))
        {
            Competitor = null;
            await competitor();
        }
        return result;
    }
}

/// <summary>Runs <see cref="Competitor"/> once, just before the next SaveChanges writes.</summary>
internal sealed class RunBeforeSave : SaveChangesInterceptor
{
    public Func<Task>? Competitor;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Competitor is { } competitor)
        {
            Competitor = null;
            await competitor();
        }
        return result;
    }
}
