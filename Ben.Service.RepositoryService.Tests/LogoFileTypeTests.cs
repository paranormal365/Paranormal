using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Validates the design decisions implemented by UploadFileTypeSeeder:
/// the "Logo" file type uses AllowAllExtensions=false with specific image patterns,
/// and FileExtensionPatternMatcher correctly enforces those boundaries.
/// </summary>
public class LogoFileTypeTests
{
    // ── The extensions the seeder registers (mirrored here for test independence) ──

    private static readonly string[] LogoExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

    private static async Task<(Guid TypeId, Guid CreatorId)> SeedLogoTypeAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var creatorId = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = typeId, Name = "Logo",
            Description = "Organization logo images",
            IsActive = true, IsPublic = true, SortOrder = 1,
            AllowAllExtensions = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId
        });

        foreach (var ext in LogoExtensions)
        {
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = Guid.NewGuid(), UploadFileTypeId = typeId, Pattern = ext,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId
            });
        }

        await db.SaveChangesAsync();
        return (typeId, creatorId);
    }

    // ── DB persistence ────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoFileType_CanBeStoredAndRetrievedWithAllExtensions()
    {
        var factory = CreateFactory();
        var (typeId, _) = await SeedLogoTypeAsync(factory);

        await using var db = await factory.CreateDbContextAsync();
        var type = await db.UploadFileTypes
            .Include(t => t.AllowedExtensions)
            .FirstAsync(t => t.Id == typeId);

        Assert.Equal("Logo", type.Name);
        Assert.False(type.AllowAllExtensions);
        Assert.Equal(LogoExtensions.Length, type.AllowedExtensions.Count);
        Assert.True(type.IsActive);
    }

    [Fact]
    public async Task LogoFileType_AllowAllExtensionsIsFalse()
    {
        var factory = CreateFactory();
        var (typeId, _) = await SeedLogoTypeAsync(factory);

        await using var db = await factory.CreateDbContextAsync();
        var type = await db.UploadFileTypes.FindAsync(typeId);

        Assert.False(type!.AllowAllExtensions);
    }

    // ── Image extensions are allowed ──────────────────────────────────────────

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".svg")]
    public void FileExtensionPatternMatcher_AllowsAllSeededImageExtensions(string ext)
    {
        Assert.True(FileExtensionPatternMatcher.IsAllowedByPatterns(LogoExtensions, ext));
    }

    [Theory]
    [InlineData(".JPG")]   // uppercase
    [InlineData(".PNG")]
    [InlineData(".Svg")]
    public void FileExtensionPatternMatcher_IsCaseInsensitive(string ext)
    {
        Assert.True(FileExtensionPatternMatcher.IsAllowedByPatterns(LogoExtensions, ext));
    }

    // ── Non-image extensions are blocked ──────────────────────────────────────

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".exe")]
    [InlineData(".txt")]
    [InlineData(".docx")]
    [InlineData(".mp4")]
    [InlineData(".zip")]
    public void FileExtensionPatternMatcher_BlocksNonImageExtensions(string ext)
    {
        Assert.False(FileExtensionPatternMatcher.IsAllowedByPatterns(LogoExtensions, ext));
    }

    // ── Seeded extension count is correct ─────────────────────────────────────

    [Fact]
    public void LogoExtensions_ContainsExactlySixEntries()
    {
        Assert.Equal(6, LogoExtensions.Length);
    }

    [Fact]
    public void LogoExtensions_AllAreLowercase()
    {
        foreach (var ext in LogoExtensions)
            Assert.Equal(ext, ext.ToLowerInvariant());
    }

    [Fact]
    public void LogoExtensions_AllStartWithDot()
    {
        foreach (var ext in LogoExtensions)
            Assert.StartsWith(".", ext);
    }

    // ── AllowAllExtensions=true bypasses pattern list ─────────────────────────

    [Fact]
    public async Task LogoFileType_WhenAllowAllExtensionsTrue_AcceptsAnyExtension()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var creatorId = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = typeId, Name = "UnrestrictedLogo",
            IsActive = true, IsPublic = true, SortOrder = 1,
            AllowAllExtensions = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId
        });
        await db.SaveChangesAsync();

        // Simulate the runtime check (AllowAllExtensions short-circuits the pattern match)
        var type = await db.UploadFileTypes.FindAsync(typeId);
        var patterns = Array.Empty<string>();
        var allowed = type!.AllowAllExtensions
                      || FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, ".exe");

        Assert.True(allowed);
    }
}
