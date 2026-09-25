using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The migrations on disk build the model the code describes (storefront, S0.8).
/// </summary>
/// <remarks>
/// <para>Found the hard way on 09/24/2026: the first <c>StoreCatalog</c> migration was generated
/// before the catalogue's audit keys were set to NoAction, so it created nine cascading keys from
/// <c>AppUsers</c> and a second cascade path to product pictures — a migration SQL Server refuses.
/// Every model guard passed, because they read the model and the model was right; the next
/// migration then quietly "fixed" the first by dropping and re-adding its keys.</para>
///
/// <para>This compares the model with the migrations' snapshot, the way <c>dotnet ef migrations
/// add</c> does, without a database. It fails whenever the model has changed and nobody generated
/// the migration for it — or a migration was generated against an older model.</para>
/// </remarks>
public sealed class MigrationsMatchTheModelTests
{
    [Fact]
    public void Nothing_in_the_model_is_missing_a_migration()
    {
        using var db = new BenDataContext(new DbContextOptionsBuilder<BenDataContext>()
            .UseSqlServer("Server=model-only;Database=model-only").Options);

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model differs from the last migration's snapshot — run `dotnet ef migrations add` "
          + "(and read what it generates: a change to an EXISTING table here means an earlier migration was stale)");
    }
}
