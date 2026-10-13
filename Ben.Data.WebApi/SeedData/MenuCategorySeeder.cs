using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// The categories a menu starts with (Ben, 10/13/2026). Idempotent: safe on every startup.
/// </summary>
/// <remarks>
/// <para>Ben: "All the possible categories will be available. Also, the event planner will be able to
/// create one if it is missing." These are the ones every planner will want; anything else a planner
/// adds joins the list for everybody.</para>
///
/// <para>Matched by <see cref="MenuCategory.KeyFor"/>, so a category a planner already added under
/// the same name is kept, not duplicated. Nothing is renamed or reordered on a later run.</para>
/// </remarks>
internal static class MenuCategorySeeder
{
    /// <summary>In the order a menu usually reads.</summary>
    internal static readonly string[] Starters =
    [
        "Appetizers", "Soups", "Salads", "First Course", "Second Course", "Main Course", "Entrées",
        "Dishes", "Sandwiches", "Burgers", "Pizza", "Pasta", "Sides", "Specials", "Kids' Menu",
        "Breakfast", "Desserts", "Snacks", "Candy", "Drinks", "Soft Drinks", "Coffee & Tea",
        "Beer & Wine", "Cocktails", "Add-ons",
    ];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await SeedAsync(db, CancellationToken.None);
    }

    internal static async Task SeedAsync(BenDataContext db, CancellationToken ct)
    {
        var have = (await db.MenuCategories.Select(c => c.NameKey).ToListAsync(ct)).ToHashSet();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Starters.Length; i++)
        {
            var key = MenuCategory.KeyFor(Starters[i]);
            if (!have.Add(key)) continue;

            db.MenuCategories.Add(new MenuCategory
            {
                Id = Guid.NewGuid(), Name = Starters[i], NameKey = key,
                SortOrder = (i + 1) * 10, IsStarter = true, DateCreated = now,
            });
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }
}
