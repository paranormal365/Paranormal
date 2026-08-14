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
    internal const string LogoFileTypeName           = "Logo";
    internal const string AudioFileTypeName          = "Audio";
    internal const string EvidenceFileTypeName       = "Case Evidence";
    internal const string PublishedVideoFileTypeName = "Published Video";
    internal const string AudioMixFileTypeName       = "Audio Mix";

    // Fixed GUID so VideoProjectController can reference it without a DB lookup.
    internal static readonly Guid PublishedVideoFileTypeId = new("30000000-0000-0000-0000-000000000001");

    // Fixed GUID so CaseAudioMixController can reference it without a DB lookup.
    internal static readonly Guid AudioMixFileTypeId = new("40000000-0000-0000-0000-000000000001");

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

        // AllowAllExtensions=true so clients can attach any media file to an occurrence
        await SeedEvidenceFileTypeAsync(db, owner.Id);

        await SeedPublishedVideoFileTypeAsync(db, owner.Id);

        await SeedAudioMixFileTypeAsync(db, owner.Id);
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

    /// <summary>Ensures the Case Evidence file type exists (AllowAllExtensions=true).</summary>
    private static async Task SeedEvidenceFileTypeAsync(BenDataContext db, Guid ownerId)
    {
        if (await db.UploadFileTypes.AnyAsync(t => t.Name == EvidenceFileTypeName)) return;

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id                 = new Guid("20000000-0000-0000-0000-000000000001"), // fixed — referenced by MyCaseController
            Name               = EvidenceFileTypeName,
            Description        = "Client-submitted evidence files attached to case occurrences — photos, audio, video and documents",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 3,
            AllowAllExtensions = true,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Ensures the Published Video file type exists (fixed GUID so the publish endpoint can use it directly).</summary>
    private static async Task SeedPublishedVideoFileTypeAsync(BenDataContext db, Guid ownerId)
    {
        if (await db.UploadFileTypes.AnyAsync(t => t.Id == PublishedVideoFileTypeId)) return;

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id                 = PublishedVideoFileTypeId,
            Name               = PublishedVideoFileTypeName,
            Description        = "Rendered video exports published from the Ben.Video editor",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 5,
            AllowAllExtensions = true, // any video format the editor produces
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Ensures the Audio Mix file type exists (fixed GUID so the mixer export endpoint can use it directly).</summary>
    private static async Task SeedAudioMixFileTypeAsync(BenDataContext db, Guid ownerId)
    {
        if (await db.UploadFileTypes.AnyAsync(t => t.Id == AudioMixFileTypeId)) return;

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id                 = AudioMixFileTypeId,
            Name               = AudioMixFileTypeName,
            Description        = "Multi-track mixdowns exported from the case audio mixer",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 6,
            AllowAllExtensions = true, // always WAV today, but the mixer's own output format
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
    }
}
