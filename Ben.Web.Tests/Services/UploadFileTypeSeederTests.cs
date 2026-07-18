using System.Reflection;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for UploadFileTypeSeeder constants, extension lists, and SeedFileTypeAsync
/// idempotency — exercised via reflection to avoid the full DI/UserManager setup
/// required by SeedAsync.
/// </summary>
public class UploadFileTypeSeederTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BenDataContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BenDataContext(opts);
    }

    /// <summary>
    /// Calls the private static SeedFileTypeAsync method via reflection so we can
    /// test its behaviour without wiring up the full DI/UserManager stack.
    /// </summary>
    private static async Task InvokeSeedFileTypeAsync(
        BenDataContext db, Guid ownerId,
        string name, string description, int sortOrder, string[] extensions)
    {
        var method = typeof(UploadFileTypeSeeder)
            .GetMethod("SeedFileTypeAsync",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(UploadFileTypeSeeder), "SeedFileTypeAsync");

        await (Task)method.Invoke(null,
            [db, ownerId, name, description, sortOrder, extensions])!;
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void LogoFileTypeName_IsLogo()
    {
        Assert.Equal("Logo", UploadFileTypeSeeder.LogoFileTypeName);
    }

    [Fact]
    public void AudioFileTypeName_IsAudio()
    {
        Assert.Equal("Audio", UploadFileTypeSeeder.AudioFileTypeName);
    }

    // ── Audio extension list ──────────────────────────────────────────────────

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".wav")]
    [InlineData(".ogg")]
    [InlineData(".flac")]
    [InlineData(".aac")]
    [InlineData(".m4a")]
    [InlineData(".opus")]
    [InlineData(".webm")]
    public void AudioExtensions_ContainsExpectedFormat(string extension)
    {
        var exts = GetPrivateArray("AudioExtensions");
        Assert.Contains(extension, exts, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AudioExtensions_ContainsExactly8Formats()
    {
        Assert.Equal(8, GetPrivateArray("AudioExtensions").Length);
    }

    // ── Logo extension list ───────────────────────────────────────────────────

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".svg")]
    public void LogoExtensions_ContainsExpectedFormat(string extension)
    {
        var exts = GetPrivateArray("LogoExtensions");
        Assert.Contains(extension, exts, StringComparer.OrdinalIgnoreCase);
    }

    // ── SeedFileTypeAsync — creates new record ────────────────────────────────

    [Fact]
    public async Task SeedFileTypeAsync_CreatesFileType_WhenAbsent()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();

        await InvokeSeedFileTypeAsync(db, ownerId, "Audio", "desc", 2,
            [".mp3", ".wav"]);

        var type = await db.UploadFileTypes.SingleAsync();
        Assert.Equal("Audio", type.Name);
        Assert.Equal(ownerId, type.CreatedByAppUserId);
        Assert.True(type.IsActive);
        Assert.False(type.AllowAllExtensions);
    }

    [Fact]
    public async Task SeedFileTypeAsync_CreatesExtensionPatterns()
    {
        await using var db = CreateDb();

        await InvokeSeedFileTypeAsync(db, Guid.NewGuid(), "Audio", "desc", 2,
            [".mp3", ".wav", ".ogg"]);

        var patterns = await db.UploadFileTypeExtensions
            .Select(e => e.Pattern)
            .ToListAsync();

        Assert.Equal(3, patterns.Count);
        Assert.Contains(".mp3", patterns);
        Assert.Contains(".wav", patterns);
        Assert.Contains(".ogg", patterns);
    }

    // ── SeedFileTypeAsync — idempotency ───────────────────────────────────────

    [Fact]
    public async Task SeedFileTypeAsync_IsIdempotent_DoesNotDuplicateFileType()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();

        await InvokeSeedFileTypeAsync(db, ownerId, "Audio", "desc", 2, [".mp3"]);
        await InvokeSeedFileTypeAsync(db, ownerId, "Audio", "desc", 2, [".mp3"]);

        Assert.Equal(1, await db.UploadFileTypes.CountAsync());
    }

    [Fact]
    public async Task SeedFileTypeAsync_IsIdempotent_DoesNotDuplicateExtensions()
    {
        await using var db = CreateDb();

        await InvokeSeedFileTypeAsync(db, Guid.NewGuid(), "Audio", "desc", 2, [".mp3", ".wav"]);
        await InvokeSeedFileTypeAsync(db, Guid.NewGuid(), "Audio", "desc", 2, [".mp3", ".wav"]);

        // Each extension must appear exactly once
        Assert.Equal(2, await db.UploadFileTypeExtensions.CountAsync());
    }

    [Fact]
    public async Task SeedFileTypeAsync_OnSecondCall_AddsOnlyNewExtensions()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();

        await InvokeSeedFileTypeAsync(db, ownerId, "Audio", "desc", 2, [".mp3"]);
        await InvokeSeedFileTypeAsync(db, ownerId, "Audio", "desc", 2, [".mp3", ".wav"]);

        // .mp3 already existed, only .wav should have been added
        Assert.Equal(2, await db.UploadFileTypeExtensions.CountAsync());
    }

    [Fact]
    public async Task SeedFileTypeAsync_CanSeedTwoDifferentTypes()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();

        await InvokeSeedFileTypeAsync(db, ownerId, "Logo",  "desc", 1, [".png"]);
        await InvokeSeedFileTypeAsync(db, ownerId, "Audio", "desc", 2, [".mp3"]);

        Assert.Equal(2, await db.UploadFileTypes.CountAsync());
        Assert.Equal(2, await db.UploadFileTypeExtensions.CountAsync());
    }

    // ── Private helper ────────────────────────────────────────────────────────

    private static string[] GetPrivateArray(string fieldName) =>
        (string[])(typeof(UploadFileTypeSeeder)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(nameof(UploadFileTypeSeeder), fieldName))
            .GetValue(null)!;
}
