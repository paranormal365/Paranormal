using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Smoke-tests the <see cref="RepositoryBase{T}"/> read-path methods through two
/// representative concrete repositories: <see cref="UploadFileTypeRepository"/>
/// (minimal seed requirements, has a collection navigation) and
/// <see cref="OrganizationAddressTypeRepository"/> (cross-entity sanity check).
/// </summary>
public class RepositoryReadPathTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static UploadFileTypeRepository FileTypeRepo(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    private static OrganizationAddressTypeRepository AddressTypeRepo(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    /// <summary>Creates an <see cref="UploadFileType"/> with required scalar fields only.</summary>
    private static UploadFileType MakeFileType(string name, bool isActive = true) => new()
    {
        Id                 = Guid.NewGuid(),
        Name               = name,
        IsActive           = isActive,
        IsPublic           = true,
        AllowAllExtensions = false,
        SortOrder          = 1,
        DateCreated        = DateTime.UtcNow,
        CreatedByAppUserId = Guid.NewGuid(),   // in-memory DB does not enforce FK
    };

    private static UploadFileTypeExtension MakeExtension(Guid fileTypeId, string pattern) => new()
    {
        Id                 = Guid.NewGuid(),
        UploadFileTypeId   = fileTypeId,
        Pattern            = pattern,
        DateCreated        = DateTime.UtcNow,
        CreatedByAppUserId = Guid.NewGuid(),
    };

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_WhenEmpty_ReturnsEmptyCollection()
    {
        var factory = CreateFactory();
        var repo    = FileTypeRepo(factory);

        var result = await repo.GetAllAsync(trackChanges: false, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSeededEntities()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(MakeFileType("Images"), MakeFileType("Documents"), MakeFileType("Audio"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory).GetAllAsync(trackChanges: false, CancellationToken.None);

        Assert.Equal(3, result.Count());
    }

    [Fact]
    public async Task GetAllAsync_WithExplicitIncludes_LoadsNavigationCollection()
    {
        var factory  = CreateFactory();
        var fileType = MakeFileType("Images");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(fileType);
            db.UploadFileTypeExtensions.Add(MakeExtension(fileType.Id, ".jpg"));
            db.UploadFileTypeExtensions.Add(MakeExtension(fileType.Id, ".png"));
            await db.SaveChangesAsync();
        }

        var result = (await FileTypeRepo(factory)
            .GetAllAsync(
                includes: [ft => ft.AllowedExtensions],
                trackChanges: false,
                CancellationToken.None))
            .ToList();

        Assert.Single(result);
        Assert.Equal(2, result[0].AllowedExtensions.Count);
    }

    [Fact]
    public async Task GetAllAsync_WithIncludeAllNavigations_PopulatesCollections()
    {
        var factory   = CreateFactory();
        var creatorId = Guid.NewGuid();
        var fileType  = MakeFileType("Docs");
        fileType.CreatedByAppUserId = creatorId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = creatorId, UserName = creatorId.ToString(), Email = $"{creatorId}@test.com" });
            db.UploadFileTypes.Add(fileType);
            db.UploadFileTypeExtensions.Add(MakeExtension(fileType.Id, ".pdf"));
            await db.SaveChangesAsync();
        }

        var result = (await FileTypeRepo(factory)
            .GetAllAsync(includeAllNavigations: true, trackChanges: false, CancellationToken.None))
            .ToList();

        Assert.Single(result);
        Assert.NotEmpty(result[0].AllowedExtensions);
    }

    // ── FindListAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FindListAsync_WithMatchingPredicate_ReturnsOnlyMatches()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(
                MakeFileType("Images"),
                MakeFileType("Documents"),
                MakeFileType("Audio"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .FindListAsync(ft => ft.Name == "Images", trackChanges: false, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Images", result.First().Name);
    }

    [Fact]
    public async Task FindListAsync_WithNoMatch_ReturnsEmptyCollection()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType("Images"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .FindListAsync(ft => ft.Name == "DoesNotExist", trackChanges: false, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindListAsync_WithIncludes_LoadsNavigationCollection()
    {
        var factory  = CreateFactory();
        var ft1      = MakeFileType("Images");
        var ft2      = MakeFileType("Documents");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(ft1, ft2);
            db.UploadFileTypeExtensions.Add(MakeExtension(ft1.Id, ".jpg"));
            await db.SaveChangesAsync();
        }

        var result = (await FileTypeRepo(factory)
            .FindListAsync(
                ft => ft.Name == "Images",
                includes: [ft => ft.AllowedExtensions],
                trackChanges: false,
                CancellationToken.None))
            .ToList();

        Assert.Single(result);
        Assert.Single(result[0].AllowedExtensions);
        Assert.Equal(".jpg", result[0].AllowedExtensions.First().Pattern);
    }

    [Fact]
    public async Task FindListAsync_WithIsActive_FiltersInactiveEntities()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(
                MakeFileType("Active1", isActive: true),
                MakeFileType("Active2", isActive: true),
                MakeFileType("Inactive", isActive: false));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .FindListAsync(ft => ft.IsActive, trackChanges: false, CancellationToken.None);

        Assert.Equal(2, result.Count());
        Assert.All(result, r => Assert.True(r.IsActive));
    }

    // ── FindOneAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FindOneAsync_WithMatchingPredicate_ReturnsEntity()
    {
        var factory  = CreateFactory();
        var fileType = MakeFileType("Images");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(fileType, MakeFileType("Documents"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .FindOneAsync(ft => ft.Name == "Images", trackChanges: false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Images", result!.Name);
        Assert.Equal(fileType.Id, result.Id);
    }

    [Fact]
    public async Task FindOneAsync_WithNoMatch_ReturnsNull()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType("Images"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .FindOneAsync(ft => ft.Name == "DoesNotExist", trackChanges: false, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindOneAsync_WithIncludes_LoadsNavigationCollection()
    {
        var factory  = CreateFactory();
        var fileType = MakeFileType("Images");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(fileType);
            db.UploadFileTypeExtensions.Add(MakeExtension(fileType.Id, ".jpg"));
            db.UploadFileTypeExtensions.Add(MakeExtension(fileType.Id, ".png"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .FindOneAsync(
                ft => ft.Id == fileType.Id,
                includes: [ft => ft.AllowedExtensions],
                trackChanges: false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.AllowedExtensions.Count);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsCorrectEntity()
    {
        var factory  = CreateFactory();
        var fileType = MakeFileType("Images");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(fileType, MakeFileType("Documents"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .GetByIdAsync(fileType.Id, trackChanges: false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(fileType.Id,   result!.Id);
        Assert.Equal("Images", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType("Images"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .GetByIdAsync(Guid.NewGuid(), trackChanges: false, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_WithIncludes_LoadsNavigationCollection()
    {
        var factory  = CreateFactory();
        var fileType = MakeFileType("Images");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(fileType);
            db.UploadFileTypeExtensions.AddRange(
                MakeExtension(fileType.Id, ".jpg"),
                MakeExtension(fileType.Id, ".png"),
                MakeExtension(fileType.Id, ".gif"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .GetByIdAsync(
                fileType.Id,
                includes: [ft => ft.AllowedExtensions],
                trackChanges: false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.AllowedExtensions.Count);
    }

    [Fact]
    public async Task GetByIdAsync_WithIncludeAllNavigations_LoadsAllNavProps()
    {
        var factory   = CreateFactory();
        var creatorId = Guid.NewGuid();
        var fileType  = MakeFileType("Docs");
        fileType.CreatedByAppUserId = creatorId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = creatorId, UserName = creatorId.ToString(), Email = $"{creatorId}@test.com" });
            db.UploadFileTypes.Add(fileType);
            db.UploadFileTypeExtensions.Add(MakeExtension(fileType.Id, ".pdf"));
            await db.SaveChangesAsync();
        }

        var result = await FileTypeRepo(factory)
            .GetByIdAsync(fileType.Id, includeAllNavigations: true, trackChanges: false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.AllowedExtensions);
    }

    // ── CountAllAsync / CountFindAsync ────────────────────────────────────────

    [Fact]
    public async Task CountAllAsync_WhenEmpty_ReturnsZero()
    {
        var factory = CreateFactory();

        var count = await FileTypeRepo(factory).CountAllAsync(CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountAllAsync_ReturnsCorrectCount()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(MakeFileType("A"), MakeFileType("B"), MakeFileType("C"));
            await db.SaveChangesAsync();
        }

        var count = await FileTypeRepo(factory).CountAllAsync(CancellationToken.None);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task CountFindAsync_WithPredicate_ReturnsMatchingCount()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(
                MakeFileType("Active1",  isActive: true),
                MakeFileType("Active2",  isActive: true),
                MakeFileType("Inactive", isActive: false));
            await db.SaveChangesAsync();
        }

        var count = await FileTypeRepo(factory)
            .CountFindAsync(ft => ft.IsActive, CancellationToken.None);

        Assert.Equal(2, count);
    }

    // ── Cross-entity smoke test ───────────────────────────────────────────────

    [Fact]
    public async Task OrganizationAddressTypeRepository_GetAllAsync_WorksCorrectly()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var creatorId = Guid.NewGuid();
            db.OrganizationAddressTypes.AddRange(
                new OrganizationAddressType { Id = Guid.NewGuid(), Name = "Main",     IsActive = true, IsPublic = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new OrganizationAddressType { Id = Guid.NewGuid(), Name = "Billing",  IsActive = true, IsPublic = true, SortOrder = 2, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new OrganizationAddressType { Id = Guid.NewGuid(), Name = "Shipping", IsActive = true, IsPublic = true, SortOrder = 3, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId });
            await db.SaveChangesAsync();
        }

        var result = await AddressTypeRepo(factory).GetAllAsync(trackChanges: false, CancellationToken.None);

        Assert.Equal(3, result.Count());
    }

    [Fact]
    public async Task OrganizationAddressTypeRepository_FindOneAsync_ByName()
    {
        var factory   = CreateFactory();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationAddressTypes.AddRange(
                new OrganizationAddressType { Id = Guid.NewGuid(), Name = "Main",    IsActive = true, IsPublic = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new OrganizationAddressType { Id = Guid.NewGuid(), Name = "Billing", IsActive = true, IsPublic = true, SortOrder = 2, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId });
            await db.SaveChangesAsync();
        }

        var result = await AddressTypeRepo(factory)
            .FindOneAsync(t => t.Name == "Billing", trackChanges: false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Billing", result!.Name);
    }
}
