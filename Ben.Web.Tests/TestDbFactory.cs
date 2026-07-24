using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ben.Web.Tests;

/// <summary>
/// Shared helper for creating isolated in-memory <see cref="BenDataContext"/> factories.
/// Each call produces a uniquely-named database so tests never share state.
/// </summary>
internal static class TestDbFactory
{
    public static IDbContextFactory<BenDataContext> Create()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }
}
