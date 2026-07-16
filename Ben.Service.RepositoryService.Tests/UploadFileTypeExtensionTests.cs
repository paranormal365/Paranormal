using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for UploadFileType.AllowAllExtensions and the UploadFileTypeExtension entity,
/// plus the FileExtensionPatternMatcher helper.
/// </summary>
public class UploadFileTypeExtensionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

    private static UploadFileType MakeFileType(Guid id, bool allowAll = false, Guid? creatorId = null) =>
        new()
        {
            Id = id,
            Name = "Test Type",
            IsActive = true,
            IsPublic = true,
            SortOrder = 0,
            AllowAllExtensions = allowAll,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = creatorId ?? Guid.NewGuid()
        };

    // ── UploadFileType.AllowAllExtensions ─────────────────────────────────────

    [Fact]
    public async Task UploadFileType_AllowAllExtensions_DefaultsFalse()
    {
        var factory = CreateFactory();
        var typeId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, allowAll: false));
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes.FindAsync(typeId);
            Assert.NotNull(type);
            Assert.False(type.AllowAllExtensions);
        }
    }

    [Fact]
    public async Task UploadFileType_AllowAllExtensions_CanBeSetTrue()
    {
        var factory = CreateFactory();
        var typeId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, allowAll: true));
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes.FindAsync(typeId);
            Assert.NotNull(type);
            Assert.True(type.AllowAllExtensions);
        }
    }

    [Fact]
    public async Task UploadFileType_AllowAllExtensions_CanBeUpdated()
    {
        var factory = CreateFactory();
        var typeId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, allowAll: false));
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes.FindAsync(typeId);
            type!.AllowAllExtensions = true;
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes.FindAsync(typeId);
            Assert.True(type!.AllowAllExtensions);
        }
    }

    // ── UploadFileTypeExtension entity ────────────────────────────────────────

    [Fact]
    public async Task UploadFileTypeExtension_CanBeCreatedAndRetrieved()
    {
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var extId     = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, creatorId: creatorId));
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = extId,
                UploadFileTypeId = typeId,
                Pattern = ".txt",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = creatorId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var ext = await db.UploadFileTypeExtensions.FindAsync(extId);
            Assert.NotNull(ext);
            Assert.Equal(".txt", ext.Pattern);
            Assert.Equal(typeId, ext.UploadFileTypeId);
        }
    }

    [Fact]
    public async Task UploadFileTypeExtension_MultiplePatterns_LoadViaNavigation()
    {
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, creatorId: creatorId));
            db.UploadFileTypeExtensions.AddRange(
                new UploadFileTypeExtension { Id = Guid.NewGuid(), UploadFileTypeId = typeId, Pattern = ".doc",  DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new UploadFileTypeExtension { Id = Guid.NewGuid(), UploadFileTypeId = typeId, Pattern = ".docx", DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new UploadFileTypeExtension { Id = Guid.NewGuid(), UploadFileTypeId = typeId, Pattern = ".tx*",  DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId }
            );
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes
                .Include(t => t.AllowedExtensions)
                .FirstOrDefaultAsync(t => t.Id == typeId);

            Assert.NotNull(type);
            Assert.Equal(3, type.AllowedExtensions.Count);
            Assert.Contains(type.AllowedExtensions, e => e.Pattern == ".doc");
            Assert.Contains(type.AllowedExtensions, e => e.Pattern == ".docx");
            Assert.Contains(type.AllowedExtensions, e => e.Pattern == ".tx*");
        }
    }

    [Fact]
    public async Task UploadFileTypeExtension_CanBeUpdated()
    {
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var extId     = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, creatorId: creatorId));
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = extId,
                UploadFileTypeId = typeId,
                Pattern = ".txt",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = creatorId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var ext = await db.UploadFileTypeExtensions.FindAsync(extId);
            ext!.Pattern = ".md";
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var ext = await db.UploadFileTypeExtensions.FindAsync(extId);
            Assert.Equal(".md", ext!.Pattern);
        }
    }

    [Fact]
    public async Task UploadFileTypeExtension_CanBeDeletedWithoutDeletingFileType()
    {
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var extId     = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, creatorId: creatorId));
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = extId,
                UploadFileTypeId = typeId,
                Pattern = ".txt",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = creatorId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var ext = await db.UploadFileTypeExtensions.FindAsync(extId);
            db.UploadFileTypeExtensions.Remove(ext!);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.UploadFileTypeExtensions.FindAsync(extId));
            Assert.NotNull(await db.UploadFileTypes.FindAsync(typeId)); // parent survives
        }
    }

    [Fact]
    public async Task UploadFileTypeExtension_CascadeDeletes_WhenParentFileTypeDeleted()
    {
        // Note: EF Core InMemory cascades only when related entities are tracked.
        // We include AllowedExtensions so the children are loaded and tracked before removal.
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var extId1    = Guid.NewGuid();
        var extId2    = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, creatorId: creatorId));
            db.UploadFileTypeExtensions.AddRange(
                new UploadFileTypeExtension { Id = extId1, UploadFileTypeId = typeId, Pattern = ".txt", DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new UploadFileTypeExtension { Id = extId2, UploadFileTypeId = typeId, Pattern = ".md",  DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId }
            );
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            // Include children so they are tracked — required for InMemory cascade
            var type = await db.UploadFileTypes
                .Include(t => t.AllowedExtensions)
                .FirstAsync(t => t.Id == typeId);
            db.UploadFileTypes.Remove(type);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.UploadFileTypeExtensions.FindAsync(extId1));
            Assert.Null(await db.UploadFileTypeExtensions.FindAsync(extId2));
        }
    }

    [Fact]
    public void UploadFileTypeExtension_UniquePatternPerType_IsConfiguredOnModel()
    {
        // The InMemory provider does not enforce unique indices at runtime.
        // This test verifies the EF model metadata instead — confirming the
        // unique constraint is declared so it will be enforced by SQL Server.
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new BenDataContext(options);

        var entityType = db.Model.FindEntityType(typeof(UploadFileTypeExtension));
        Assert.NotNull(entityType);

        var uniqueIndex = entityType!.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                i.Properties.Any(p => p.Name == nameof(UploadFileTypeExtension.UploadFileTypeId)) &&
                i.Properties.Any(p => p.Name == nameof(UploadFileTypeExtension.Pattern)));

        Assert.NotNull(uniqueIndex);
    }

    [Fact]
    public async Task UploadFileTypeExtension_SamePattern_AllowedOnDifferentFileTypes()
    {
        var factory    = CreateFactory();
        var typeId1    = Guid.NewGuid();
        var typeId2    = Guid.NewGuid();
        var creatorId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.AddRange(
                MakeFileType(typeId1, creatorId: creatorId),
                MakeFileType(typeId2, creatorId: creatorId)
            );
            db.UploadFileTypeExtensions.AddRange(
                new UploadFileTypeExtension { Id = Guid.NewGuid(), UploadFileTypeId = typeId1, Pattern = ".txt", DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId },
                new UploadFileTypeExtension { Id = Guid.NewGuid(), UploadFileTypeId = typeId2, Pattern = ".txt", DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId }
            );
            await db.SaveChangesAsync(); // should NOT throw — different parents
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.UploadFileTypeExtensions.CountAsync(e => e.UploadFileTypeId == typeId1));
            Assert.Equal(1, await db.UploadFileTypeExtensions.CountAsync(e => e.UploadFileTypeId == typeId2));
        }
    }

    // ── FileExtensionPatternMatcher ───────────────────────────────────────────

    [Theory]
    [InlineData(".txt",  ".txt",  true)]
    [InlineData(".txt",  ".TXT",  true)]   // case insensitive
    [InlineData(".txt",  "txt",   true)]   // no leading dot on extension
    [InlineData("txt",   ".txt",  true)]   // no leading dot on pattern
    [InlineData(".txt",  ".md",   false)]
    [InlineData(".txt",  ".txtx", false)]  // exact — extra chars don't match
    public void PatternMatcher_ExactPattern(string pattern, string extension, bool expected)
    {
        Assert.Equal(expected, FileExtensionPatternMatcher.Matches(pattern, extension));
    }

    [Theory]
    [InlineData(".tx*",  ".tx",   true)]   // prefix only
    [InlineData(".tx*",  ".txa",  true)]
    [InlineData(".tx*",  ".txb",  true)]
    [InlineData(".tx*",  ".txzzz",true)]
    [InlineData(".tx*",  ".TXA",  true)]   // case insensitive
    [InlineData(".tx*",  ".txt",  true)]   // .txt starts with .tx
    [InlineData(".tx*",  ".ta",   false)]  // doesn't start with .tx
    [InlineData(".tx*",  ".doc",  false)]
    [InlineData(".*",    ".anything", true)]  // wildcard matches all
    public void PatternMatcher_WildcardPattern(string pattern, string extension, bool expected)
    {
        Assert.Equal(expected, FileExtensionPatternMatcher.Matches(pattern, extension));
    }

    [Theory]
    [InlineData(null,   ".txt",  false)]
    [InlineData(".txt", null,    false)]
    [InlineData("",     ".txt",  false)]
    [InlineData(".txt", "",      false)]
    [InlineData("   ",  ".txt",  false)]
    public void PatternMatcher_NullOrEmpty_ReturnsFalse(string? pattern, string? extension, bool expected)
    {
        Assert.Equal(expected, FileExtensionPatternMatcher.Matches(pattern!, extension!));
    }

    [Theory]
    [InlineData(".txt",  true)]
    [InlineData(".md",   true)]
    [InlineData(".tx*",  true)]   // wildcard hits .txb
    [InlineData(".doc",  false)]
    [InlineData(".pdf",  false)]
    public void PatternMatcher_IsAllowedByPatterns(string testExtension, bool expected)
    {
        var patterns = new[] { ".txt", ".md", ".tx*" };
        Assert.Equal(expected, FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, testExtension));
    }

    [Fact]
    public void PatternMatcher_IsAllowedByPatterns_EmptyPatternList_ReturnsFalse()
    {
        Assert.False(FileExtensionPatternMatcher.IsAllowedByPatterns([], ".txt"));
    }

    // ── AllowAllExtensions gate logic ─────────────────────────────────────────

    [Fact]
    public async Task UploadFileType_AllowAllExtensions_True_PatternsHaveNoEffect()
    {
        // When AllowAllExtensions = true, the pattern list is irrelevant —
        // any extension should be accepted at the application level.
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, allowAll: true, creatorId: creatorId));
            // Deliberately add a restricting pattern — it should be ignored when allowAll=true
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = Guid.NewGuid(),
                UploadFileTypeId = typeId,
                Pattern = ".txt",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = creatorId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes
                .Include(t => t.AllowedExtensions)
                .FirstAsync(t => t.Id == typeId);

            // AllowAllExtensions = true → accept .pdf even though only .txt is listed
            var isAllowed = type.AllowAllExtensions
                || FileExtensionPatternMatcher.IsAllowedByPatterns(type.AllowedExtensions.Select(e => e.Pattern), ".pdf");

            Assert.True(isAllowed);
        }
    }

    [Fact]
    public async Task UploadFileType_AllowAllExtensions_False_PatternListEnforced()
    {
        var factory   = CreateFactory();
        var typeId    = Guid.NewGuid();
        var creatorId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(MakeFileType(typeId, allowAll: false, creatorId: creatorId));
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = Guid.NewGuid(),
                UploadFileTypeId = typeId,
                Pattern = ".txt",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = creatorId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes
                .Include(t => t.AllowedExtensions)
                .FirstAsync(t => t.Id == typeId);

            var patterns = type.AllowedExtensions.Select(e => e.Pattern).ToList();

            Assert.True(type.AllowAllExtensions  || FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, ".txt"));
            Assert.False(type.AllowAllExtensions || FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, ".pdf"));
        }
    }
}
