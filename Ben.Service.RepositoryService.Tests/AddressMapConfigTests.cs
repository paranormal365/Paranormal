using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for the OrganizationAddressMapConfig entity and its EF model configuration.
/// </summary>
public class AddressMapConfigTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(BenDataContext db, AppUser user, Organization org, OrganizationAddress addr)>
        SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        var db = await factory.CreateDbContextAsync();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "m@m.com", Email = "m@m.com", DisplayName = "Map User", DateCreated = DateTime.UtcNow };
        db.AppUsers.Add(user);
        var addrType = new OrganizationAddressType { Id = Guid.NewGuid(), Name = "Main", IsActive = true, IsPublic = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        db.OrganizationAddressTypes.Add(addrType);
        var org = new Organization { Id = Guid.NewGuid(), Name = "Map Org", UrlName = "map-org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        db.Organizations.Add(org);
        var addr = new OrganizationAddress
        {
            Id                        = Guid.NewGuid(),
            OrganizationId            = org.Id,
            OrganizationAddressTypeId = addrType.Id,
            StreetAddress1            = "123 Main St",
            City                      = "Austin",
            State                     = "TX",
            ZipCode                   = "78701",
            Country                   = "US",
            Latitude                  = (decimal)30.2672m,
            Longitude                 = (decimal)-97.7431m,
            DateCreated               = DateTime.UtcNow,
            CreatedByAppUserId        = user.Id,
        };
        db.OrganizationAddresses.Add(addr);
        await db.SaveChangesAsync();
        return (db, user, org, addr);
    }

    // ── OrganizationAddressMapConfig entity ───────────────────────────────────

    [Fact]
    public async Task MapConfig_CanBeCreatedWithDefaults()
    {
        var factory = CreateFactory();
        var (db, user, _, addr) = await SeedAsync(factory);
        await using var _d = db;

        var cfg = new OrganizationAddressMapConfig
        {
            Id                    = Guid.NewGuid(),
            OrganizationAddressId = addr.Id,
            IsOnMap               = true,
            ShowMarker            = true,
            ShowRegion            = false,
            RegionRadiusMiles     = 1.0,
            MarkerColor           = "#e63535",
            RegionFillColor       = "#3388ff",
            RegionFillOpacity     = 0.2,
            RegionStrokeColor     = "#1155cc",
            RegionStrokeOpacity   = 0.8,
            RegionStrokeWidth     = 2.0,
            DateCreated           = DateTime.UtcNow,
            CreatedByAppUserId    = user.Id,
        };
        db.OrganizationAddressMapConfigs.Add(cfg);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationAddressMapConfigs.AsNoTracking()
            .FirstAsync(c => c.Id == cfg.Id);

        Assert.True(loaded.IsOnMap);
        Assert.True(loaded.ShowMarker);
        Assert.False(loaded.ShowRegion);
        Assert.Equal(1.0, loaded.RegionRadiusMiles);
        Assert.Equal("#e63535", loaded.MarkerColor);
        Assert.Null(loaded.MarkerIconKey);
    }

    [Fact]
    public async Task MapConfig_CanStoreIconKey()
    {
        var factory = CreateFactory();
        var (db, user, _, addr) = await SeedAsync(factory);
        await using var _d = db;

        var cfg = new OrganizationAddressMapConfig
        {
            Id = Guid.NewGuid(), OrganizationAddressId = addr.Id,
            IsOnMap = true, ShowMarker = true, MarkerColor = "#0055ff",
            MarkerIconKey = "star", RegionFillColor = "#3388ff",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrganizationAddressMapConfigs.Add(cfg);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationAddressMapConfigs.AsNoTracking()
            .FirstAsync(c => c.Id == cfg.Id);
        Assert.Equal("star", loaded.MarkerIconKey);
        Assert.Equal("#0055ff", loaded.MarkerColor);
    }

    [Fact]
    public async Task MapConfig_CascadeDeletesWithAddress()
    {
        var factory = CreateFactory();
        var (db, user, _, addr) = await SeedAsync(factory);
        await using var _d = db;

        db.OrganizationAddressMapConfigs.Add(new OrganizationAddressMapConfig
        {
            Id = Guid.NewGuid(), OrganizationAddressId = addr.Id,
            IsOnMap = true, ShowMarker = true, MarkerColor = "#e63535",
            RegionFillColor = "#3388ff",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.OrganizationAddresses.Remove(addr);
        await db.SaveChangesAsync();

        Assert.False(await db.OrganizationAddressMapConfigs.AnyAsync(c => c.OrganizationAddressId == addr.Id));
    }

    [Fact]
    public async Task MapConfig_UniqueIndex_ExistsInModelConfig()
    {
        // Verify the unique index is configured in the model (not enforced by InMemory DB)
        var factory = CreateFactory();
        var (db, user, _, addr) = await SeedAsync(factory);
        await using var _d = db;
        var indexProps = db.Model.FindEntityType(typeof(OrganizationAddressMapConfig))!
            .GetIndexes().ToList();
        Assert.True(indexProps.Any(i => i.IsUnique),
            "Expected a unique index on OrganizationAddressMapConfig");
    }

    [Fact]
    public async Task OrganizationAddress_NavToMapConfig()
    {
        var factory = CreateFactory();
        var (db, user, _, addr) = await SeedAsync(factory);
        await using var _d = db;

        db.OrganizationAddressMapConfigs.Add(new OrganizationAddressMapConfig
        {
            Id = Guid.NewGuid(), OrganizationAddressId = addr.Id,
            IsOnMap = true, ShowMarker = true, ShowRegion = true,
            RegionRadiusMiles = 2.5, MarkerColor = "#e63535", RegionFillColor = "#3388ff",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var loadedAddr = await db.OrganizationAddresses
            .Include(a => a.MapConfig)
            .AsNoTracking()
            .FirstAsync(a => a.Id == addr.Id);

        Assert.NotNull(loadedAddr.MapConfig);
        Assert.True(loadedAddr.MapConfig!.ShowRegion);
        Assert.Equal(2.5, loadedAddr.MapConfig.RegionRadiusMiles);
    }
}
