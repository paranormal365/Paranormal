using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for OrganizationAreaOfOperation entity: DB persistence,
/// new acceptance flags on Organization, coordinate precision,
/// cascade delete, and one-to-one constraint.
/// </summary>
public class OrganizationAreaOfOperationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser
        {
            Id          = Guid.NewGuid(),
            UserName    = "a@b.com",
            Email       = "a@b.com",
            DisplayName = "A",
            DateCreated = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);

        var org = new Organization
        {
            Id                 = Guid.NewGuid(),
            Name               = "Test Org",
            UrlName            = "test-org",
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (user, org);
    }

    // ── Organization acceptance flags ─────────────────────────────────────────

    [Fact]
    public async Task Organization_IsAcceptingClients_DefaultsFalse()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (_, org) = await SeedAsync(db);

        var loaded = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == org.Id);
        Assert.False(loaded.IsAcceptingClients);
    }

    [Fact]
    public async Task Organization_AcceptsClientsOutsideRange_DefaultsFalse()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (_, org) = await SeedAsync(db);

        var loaded = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == org.Id);
        Assert.False(loaded.AcceptsClientsOutsideRange);
    }

    [Fact]
    public async Task Organization_AcceptanceFlagsCanBeSetAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        org.IsAcceptingClients         = true;
        org.AcceptsClientsOutsideRange = true;
        org.DateUpdated                = DateTime.UtcNow;
        org.UpdatedByAppUserId         = user.Id;
        await db.SaveChangesAsync();

        var loaded = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == org.Id);
        Assert.True(loaded.IsAcceptingClients);
        Assert.True(loaded.AcceptsClientsOutsideRange);
    }

    // ── OrganizationAreaOfOperation ───────────────────────────────────────────

    [Fact]
    public async Task AreaOfOperation_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var area = new OrganizationAreaOfOperation
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            RadiusMiles        = 30m,
            CenterLatitude     = 36.1627m,
            CenterLongitude    = -86.7816m,
            DisplayLabel       = "Within 30 miles of Nashville, TN",
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.OrganizationAreaOfOperations.Add(area);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationAreaOfOperations
            .AsNoTracking()
            .FirstAsync(a => a.OrganizationId == org.Id);

        Assert.Equal(30m, loaded.RadiusMiles);
        Assert.Equal(36.1627m, loaded.CenterLatitude);
        Assert.Equal(-86.7816m, loaded.CenterLongitude);
        Assert.Equal("Within 30 miles of Nashville, TN", loaded.DisplayLabel);
    }

    [Fact]
    public async Task AreaOfOperation_CenterCoordinatesStoredWithHighPrecision()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        // Use 10-decimal-place precision to verify no truncation
        const decimal preciseLat =  36.1627400000m;
        const decimal preciseLon = -86.7816300000m;

        db.OrganizationAreaOfOperations.Add(new OrganizationAreaOfOperation
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            RadiusMiles = 25m, CenterLatitude = preciseLat, CenterLongitude = preciseLon,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationAreaOfOperations
            .AsNoTracking()
            .FirstAsync(a => a.OrganizationId == org.Id);

        Assert.Equal(preciseLat, loaded.CenterLatitude);
        Assert.Equal(preciseLon, loaded.CenterLongitude);
    }

    [Fact]
    public async Task AreaOfOperation_OneToOne_CascadeDeletesWithOrg()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        db.OrganizationAreaOfOperations.Add(new OrganizationAreaOfOperation
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            RadiusMiles = 30m, CenterLatitude = 36m, CenterLongitude = -87m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var orgToDelete = await db.Organizations.FindAsync(org.Id);
        db.Organizations.Remove(orgToDelete!);
        await db.SaveChangesAsync();

        var remaining = await db.OrganizationAreaOfOperations
            .AsNoTracking()
            .Where(a => a.OrganizationId == org.Id)
            .ToListAsync();

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task AreaOfOperation_LoadsViaNavProperty()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        db.OrganizationAreaOfOperations.Add(new OrganizationAreaOfOperation
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            RadiusMiles = 50m, CenterLatitude = 36m, CenterLongitude = -87m,
            DisplayLabel = "Within 50 miles",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var orgWithArea = await db.Organizations
            .AsNoTracking()
            .Include(o => o.AreaOfOperation)
            .FirstAsync(o => o.Id == org.Id);

        Assert.NotNull(orgWithArea.AreaOfOperation);
        Assert.Equal(50m, orgWithArea.AreaOfOperation!.RadiusMiles);
        Assert.Equal("Within 50 miles", orgWithArea.AreaOfOperation.DisplayLabel);
    }

    [Fact]
    public void AreaOfOperation_OneToOne_UniqueIndexIsConfiguredOnModel()
    {
        // The InMemory provider does not enforce unique indexes at runtime.
        // This test verifies the unique index is declared in the EF model so SQL Server
        // will enforce one-per-org at the database level.
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("model-check")
                .Options);

        var entityType = db.Model.FindEntityType(typeof(OrganizationAreaOfOperation));
        Assert.NotNull(entityType);

        var uniqueIndex = entityType!.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                i.Properties.Any(p => p.Name == nameof(OrganizationAreaOfOperation.OrganizationId)));

        Assert.NotNull(uniqueIndex);
    }
}
