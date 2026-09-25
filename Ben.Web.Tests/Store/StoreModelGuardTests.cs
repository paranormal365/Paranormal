using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store's model keeps the rules its design depends on (storefront plan §5.2).
/// </summary>
/// <remarks>
/// <para>Each fact is a rule that, broken, fails somewhere far from the mistake: a key to
/// <c>AppUsers</c> that cascades turns deleting a person into deleting their orders; a decimal
/// without precision is silently <c>decimal(18,2)</c> on one provider and a truncation warning on
/// another; a second cascade path is refused by SQL Server only at migration time; an enum stored
/// as text breaks every conditional UPDATE that compares it as a number.</para>
///
/// <para>Read from the SQL Server model (the one migrations are generated from), except the schema
/// fact, which builds the real thing in <see cref="SqliteTestDb"/>.</para>
/// </remarks>
public sealed class StoreModelGuardTests
{
    private static readonly Lazy<IModel> Model = new(() =>
    {
        using var db = new BenDataContext(new DbContextOptionsBuilder<BenDataContext>()
            .UseSqlServer("Server=model-only;Database=model-only").Options);
        return db.Model;
    });

    private static IEnumerable<IEntityType> StoreEntities()
        => Model.Value.GetEntityTypes().Where(e => e.ClrType.Name.StartsWith("Store", StringComparison.Ordinal)
                                                && e.ClrType != typeof(StoredLinkPreview));

    [Fact]
    public void The_catalogue_is_in_the_model()
    {
        var names = StoreEntities().Select(e => e.ClrType.Name).ToHashSet();
        foreach (var expected in new[]
                 {
                     nameof(StoreCategory), nameof(StoreProduct), nameof(StoreProductOption),
                     nameof(StoreProductOptionValue), nameof(StoreProductVariant),
                     nameof(StoreProductVariantOptionValue), nameof(StoreProductImage),
                     nameof(StoreProductSpec), nameof(StoreCoupon),
                 })
            Assert.Contains(expected, names);
    }

    /// <summary>(a) Deleting a person is the purge's decision, never the database's.</summary>
    [Fact]
    public void No_store_key_to_AppUsers_cascades_or_sets_null()
    {
        var offenders = StoreEntities()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(AppUser)
                      && fk.DeleteBehavior is not (DeleteBehavior.NoAction or DeleteBehavior.Restrict or DeleteBehavior.ClientNoAction))
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name}.{string.Join(",", fk.Properties.Select(p => p.Name))} → {fk.DeleteBehavior}")
            .ToList();

        Assert.True(offenders.Count == 0, "These store keys to AppUsers delete or null on their own:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>(b) Every money and rating column states its precision.</summary>
    [Fact]
    public void Every_store_decimal_has_a_precision()
    {
        var offenders = StoreEntities()
            .SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(decimal) && p.GetPrecision() is null)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "These store decimals have no precision:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>(c) The schema builds on a real relational database, CHECKs and all.</summary>
    [Fact]
    public async Task The_store_schema_builds_and_its_stock_check_holds()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin@store.test", DateCreated = DateTime.UtcNow };
        db.AppUsers.Add(admin);
        var category = new StoreCategory
        {
            Id = Guid.NewGuid(), Name = "EMF meters", Slug = "emf-meters", IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = admin.Id,
        };
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = "K-II", Slug = "k-ii",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = admin.Id,
        };
        db.StoreCategories.Add(category);
        db.StoreProducts.Add(product);
        db.StoreProductVariants.Add(new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Sku = "KII-STD", Price = 59.99m,
            StockOnHand = 2, StockReserved = 3, DateCreated = DateTime.UtcNow, CreatedByAppUserId = admin.Id,
        });

        // Reserving more than is on the shelf is refused by the database itself.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>(d) A SKU is one variant; one set of choices is one variant of a product.</summary>
    [Fact]
    public void Variant_skus_and_choice_sets_are_unique()
    {
        var variant = Model.Value.FindEntityType(typeof(StoreProductVariant))!;
        Assert.Contains(variant.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(StoreProductVariant.Sku)]));
        Assert.Contains(variant.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(StoreProductVariant.ProductId), nameof(StoreProductVariant.OptionSignature)]));
    }

    /// <summary>(e) Every catalogue child is reached from a product by exactly one cascading path.</summary>
    [Fact]
    public void Nothing_cascades_from_a_product_by_two_paths()
        => AssertOneCascadePathFrom(typeof(StoreProduct));

    /// <summary>(f) Store enums are stored as numbers, so conditional UPDATEs compare numbers.</summary>
    [Fact]
    public void Store_enums_are_stored_as_int()
    {
        var offenders = StoreEntities()
            .SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum)
            .Where(p => (p.GetTypeMapping().Converter?.ProviderClrType ?? p.ClrType) is var provider
                     && (Nullable.GetUnderlyingType(provider) ?? provider) != typeof(int)
                     && !(Nullable.GetUnderlyingType(provider) ?? provider).IsEnum)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "These store enums are not stored as int:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_carts_and_orders_are_in_the_model()
    {
        var names = StoreEntities().Select(e => e.ClrType.Name).ToHashSet();
        foreach (var expected in new[]
                 {
                     nameof(StoreCart), nameof(StoreCartItem), nameof(StoreOrder), nameof(StoreOrderItem),
                     nameof(StoreOrderEvent), nameof(StoreRefund), nameof(StoreRefundItem),
                     nameof(StoreCouponRedemption), nameof(StoreStockMovement),
                 })
            Assert.Contains(expected, names);
    }

    /// <summary>
    /// (g) The identities an order is looked up by are each one order: its number, its Stripe
    /// payment, its emailed key — and a Stripe refund is one row.
    /// </summary>
    [Fact]
    public void Order_identities_are_unique()
    {
        var order = Model.Value.FindEntityType(typeof(StoreOrder))!;
        foreach (var column in new[] { nameof(StoreOrder.OrderNumber), nameof(StoreOrder.StripePaymentIntentId), nameof(StoreOrder.AccessToken) })
            Assert.True(order.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([column])),
                $"StoreOrders.{column} has no unique index");

        var refund = Model.Value.FindEntityType(typeof(StoreRefund))!;
        Assert.True(refund.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(StoreRefund.StripeRefundId)])),
            "StoreRefunds.StripeRefundId has no unique index — a redelivered refund event would insert a second row");
    }

    [Fact]
    public void The_favourites_and_reviews_are_in_the_model()
    {
        var names = StoreEntities().Select(e => e.ClrType.Name).ToHashSet();
        foreach (var expected in new[] { nameof(StoreFavourite), nameof(StoreReview), nameof(StoreReviewVote) })
            Assert.Contains(expected, names);
    }

    /// <summary>
    /// (i) One heart per person per product, one review per person per product, one helpful vote
    /// per person per review — the counts on the page are row counts, so a duplicate row is a lie.
    /// </summary>
    [Fact]
    public void Favourites_reviews_and_votes_are_one_per_person()
    {
        static void AssertUniquePair(Type entity, string first, string second)
            => Assert.True(Model.Value.FindEntityType(entity)!.GetIndexes()
                    .Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([first, second])),
                $"{entity.Name} has no unique ({first}, {second}) index");

        AssertUniquePair(typeof(StoreFavourite), nameof(StoreFavourite.AppUserId), nameof(StoreFavourite.ProductId));
        AssertUniquePair(typeof(StoreReview), nameof(StoreReview.ProductId), nameof(StoreReview.AuthorAppUserId));
        AssertUniquePair(typeof(StoreReviewVote), nameof(StoreReviewVote.ReviewId), nameof(StoreReviewVote.AppUserId));
    }

    /// <summary>(h) Every order child is reached from its order by exactly one cascading path.</summary>
    [Fact]
    public void Nothing_cascades_from_an_order_by_two_paths()
        => AssertOneCascadePathFrom(typeof(StoreOrder));

    /// <summary>
    /// SQL Server refuses a table reachable by two cascading paths from ANY table, so every store
    /// table is checked as a root, not only the product and the order.
    /// </summary>
    [Fact]
    public void Nothing_cascades_from_any_store_table_by_two_paths()
    {
        foreach (var root in StoreEntities().Select(e => e.ClrType))
            AssertOneCascadePathFrom(root);
    }

    /// <summary>
    /// Counts the cascading (Cascade or SetNull) paths from <paramref name="root"/> to every entity;
    /// more than one to any entity is the shape SQL Server refuses at migration time.
    /// </summary>
    internal static void AssertOneCascadePathFrom(Type root)
    {
        var edges = Model.Value.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull)
            .Select(fk => (From: fk.PrincipalEntityType.ClrType, To: fk.DeclaringEntityType.ClrType))
            .ToList();

        var paths = new Dictionary<Type, int>();
        void Walk(Type at, HashSet<Type> seen)
        {
            foreach (var (_, to) in edges.Where(e => e.From == at))
            {
                if (!seen.Add(to)) continue;
                paths[to] = paths.GetValueOrDefault(to) + 1;
                Walk(to, seen);
                seen.Remove(to);
            }
        }
        Walk(root, [root]);

        var offenders = paths.Where(p => p.Value > 1).Select(p => $"{p.Key.Name} ({p.Value} paths)").ToList();
        Assert.True(offenders.Count == 0,
            $"These are reached from {root.Name} by more than one cascading path — SQL Server refuses that:\n  "
          + string.Join("\n  ", offenders));
    }
}
