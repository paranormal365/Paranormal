using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds built-in upload file types: "Logo" (images) and "Audio" (WaveSurfer-playable audio).
/// Idempotent — safe to run on every startup.
/// </summary>
internal static class UploadFileTypeSeeder
{
    internal const string LogoFileTypeName  = "Logo";
    internal const string AudioFileTypeName = "Audio";

    private static readonly string[] LogoExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"];

    /// <summary>
    /// Audio formats natively decoded by the Web Audio API / MediaElement backend
    /// and supported by WaveSurfer.js v7 in all modern browsers.
    /// </summary>
    private static readonly string[] AudioExtensions =
        [".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".opus", ".webm"];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        var owner = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        await SeedFileTypeAsync(db, owner.Id,
            name:        LogoFileTypeName,
            description: "Organization logo images — JPEG, PNG, GIF, WebP, SVG",
            sortOrder:   1,
            extensions:  LogoExtensions);

        await SeedFileTypeAsync(db, owner.Id,
            name:        AudioFileTypeName,
            description: "Audio recordings displayed with the WaveSurfer waveform player — MP3, WAV, OGG, FLAC, AAC, M4A, Opus, WebM",
            sortOrder:   2,
            extensions:  AudioExtensions);
    }

    // ── Private helper ────────────────────────────────────────────────────────

    private static async Task SeedFileTypeAsync(
        BenDataContext db,
        Guid           ownerId,
        string         name,
        string         description,
        int            sortOrder,
        string[]       extensions)
    {
        var fileType = await db.UploadFileTypes.FirstOrDefaultAsync(t => t.Name == name);

        if (fileType is null)
        {
            fileType = new UploadFileType
            {
                Id                 = Guid.NewGuid(),
                Name               = name,
                Description        = description,
                IsActive           = true,
                IsPublic           = true,
                SortOrder          = sortOrder,
                AllowAllExtensions = false,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = ownerId,
            };
            db.UploadFileTypes.Add(fileType);
            await db.SaveChangesAsync();
        }

        var existing = (await db.UploadFileTypeExtensions
            .Where(e => e.UploadFileTypeId == fileType.Id)
            .Select(e => e.Pattern)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var ext in extensions)
        {
            if (existing.Contains(ext)) continue;
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id                 = Guid.NewGuid(),
                UploadFileTypeId   = fileType.Id,
                Pattern            = ext,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = ownerId,
            });
            added = true;
        }

        if (added) await db.SaveChangesAsync();
    }
}
