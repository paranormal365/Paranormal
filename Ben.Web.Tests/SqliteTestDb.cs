using Ben.Data.Source.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ben.Web.Tests;

/// <summary>
/// A real relational database, in memory, for the tests that have to watch a DELETE actually
/// happen.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The purges — case, person, group — are built out of
/// <c>ExecuteDeleteAsync</c> and <c>ExecuteUpdateAsync</c>, and the EF InMemory provider
/// implements neither: it throws "not supported by the current database provider" on the first
/// statement. So the most destructive code in the product had no behaviour test at all, only
/// model-derived coverage tests. This closes that (item 183).</para>
///
/// <para><b>Foreign keys are enforced.</b> That is the point rather than a nuisance: the bug these
/// tests exist to catch is a purge that deletes rows in the wrong order and is refused by the
/// database, which is exactly what happened to the group purge on production twice. A fixture has
/// to seed real parent rows here, and a purge that skips a table fails loudly.</para>
///
/// <para><b>What is relaxed.</b> The model carries SQL Server column types — <c>nvarchar(max)</c>,
/// <c>varbinary(max)</c> — that SQLite cannot parse, so the customizer below drops every explicit
/// column type and any server-specific default or computed SQL. SQLite is dynamically typed and
/// infers what it needs. Nothing about relationships, keys or delete behaviour is touched, which
/// is the half these tests are about.</para>
///
/// <para><b>Lifetime.</b> The database lives as long as the connection, so the connection is held
/// by the returned handle and closed when it is disposed. Every context the factory hands out
/// shares it, which is how the purge's context and the test's context see the same rows.</para>
/// </remarks>
public sealed class SqliteTestDb : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _connection;

    public IDbContextFactory<BenDataContext> Factory { get; }

    private SqliteTestDb(SqliteConnection connection, IDbContextFactory<BenDataContext> factory)
    {
        _connection = connection;
        Factory     = factory;
    }

    /// <summary>Opens a fresh database with the whole schema created.</summary>
    public static async Task<SqliteTestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>()
            .Options;

        await using (var db = new BenDataContext(options))
            await db.Database.EnsureCreatedAsync();

        return new SqliteTestDb(connection, new PooledDbContextFactory<BenDataContext>(options));
    }

    public Task<BenDataContext> NewContextAsync() => Factory.CreateDbContextAsync();

    public void Dispose() => _connection.Dispose();

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    /// <summary>Drops the SQL Server-only bits of the model so SQLite can create the tables.</summary>
    private sealed class SqliteFriendlyModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnType(null);
                    property.SetDefaultValueSql(null);
                    property.SetComputedColumnSql(null);
                }
            }
        }
    }
}
