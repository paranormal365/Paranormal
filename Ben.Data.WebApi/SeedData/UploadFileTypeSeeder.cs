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
    internal const string ProfilePhotoFileTypeName   = "Profile Photo";
    internal const string EquipmentPhotoFileTypeName = "Equipment Photo";

    // Fixed GUID so VideoProjectController can reference it without a DB lookup.
    internal static readonly Guid PublishedVideoFileTypeId = new("30000000-0000-0000-0000-000000000001");

    // Fixed GUID so CaseAudioMixController can reference it without a DB lookup.
    internal static readonly Guid AudioMixFileTypeId = new("40000000-0000-0000-0000-000000000001");

    // Fixed GUID so MyProfileController can reference it without a DB lookup.
    internal static readonly Guid ProfilePhotoFileTypeId = new("50000000-0000-0000-0000-000000000001");

    // Fixed GUID so MyEquipmentController (and later phases) can reference it without a DB lookup.
    internal static readonly Guid EquipmentPhotoFileTypeId = new("60000000-0000-0000-0000-000000000001");

    // Fixed GUID so FeedController can reference it without a DB lookup (item 186 F4).
    internal static readonly Guid FeedMediaFileTypeId = new("70000000-0000-0000-0000-000000000001");

    internal const string FeedMediaFileTypeName = "Feed Media";

    /// <summary>
    /// What a feed post may carry: browser-displayable photos, and video the &lt;video&gt; element
    /// plays without a plugin. No SVG — an SVG is a document that can carry script, and the feed
    /// is the one surface where anybody who belongs may upload.
    /// </summary>
    private static readonly string[] FeedMediaExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".webm", ".mov", ".m4v"];

    private static readonly string[] LogoExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"];

    /// <summary>Browser-displayable raster formats. No SVG — see SeedProfilePhotoFileTypeAsync.</summary>
    private static readonly string[] ProfilePhotoExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp"];

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

        await SeedFeedMediaFileTypeAsync(db, owner.Id);

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

        await SeedProfilePhotoFileTypeAsync(db, owner.Id);

        await SeedEquipmentPhotoFileTypeAsync(db, owner.Id);
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

    /// <summary>
    /// Ensures the Profile Photo file type exists (fixed GUID so MyProfileController can use it
    /// directly). Extension-restricted rather than AllowAllExtensions: these render in an
    /// &lt;img&gt; across the whole site, so anything that isn't a browser-displayable image is a
    /// broken avatar at best. SVG is deliberately excluded — unlike the org logo, profile photos
    /// are user-supplied by every account, and SVG can carry script.
    /// </summary>
    private static async Task SeedProfilePhotoFileTypeAsync(BenDataContext db, Guid ownerId)
    {
        if (await db.UploadFileTypes.AnyAsync(t => t.Id == ProfilePhotoFileTypeId)) return;

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id                 = ProfilePhotoFileTypeId,
            Name               = ProfilePhotoFileTypeName,
            Description        = "User profile photos — JPEG, PNG, GIF, WebP",
            IsActive           = true,
            IsPublic           = false, // the per-photo slot decides visibility, not the type
            SortOrder          = 7,
            AllowAllExtensions = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();

        foreach (var ext in ProfilePhotoExtensions)
        {
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id                 = Guid.NewGuid(),
                UploadFileTypeId   = ProfilePhotoFileTypeId,
                Pattern            = ext,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = ownerId,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ensures the Equipment Photo file type exists (fixed GUID so MyEquipmentController can use
    /// it directly). Same browser-displayable-raster-only reasoning as Profile Photo — these
    /// render in an &lt;img&gt; wherever an item's gallery appears.
    /// </summary>
    private static async Task SeedEquipmentPhotoFileTypeAsync(BenDataContext db, Guid ownerId)
    {
        if (await db.UploadFileTypes.AnyAsync(t => t.Id == EquipmentPhotoFileTypeId)) return;

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id                 = EquipmentPhotoFileTypeId,
            Name               = EquipmentPhotoFileTypeName,
            Description        = "Equipment item photos — JPEG, PNG, GIF, WebP",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 8,
            AllowAllExtensions = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();

        foreach (var ext in ProfilePhotoExtensions)
        {
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id                 = Guid.NewGuid(),
                UploadFileTypeId   = EquipmentPhotoFileTypeId,
                Pattern            = ext,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = ownerId,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The file type a feed post's photo or video belongs to (item 186 F4).
    /// </summary>
    /// <remarks>
    /// Fixed GUID for the same reason as the others: the controller names it directly rather than
    /// looking it up by a display string somebody could rename. <c>IsPublic</c> stays FALSE —
    /// the feed's own endpoint decides who may see a post's media, and it refuses anything that
    /// has not been screened. A file marked public here would be reachable by the general file
    /// routes, going around that entirely.
    /// </remarks>
    private static async Task SeedFeedMediaFileTypeAsync(BenDataContext db, Guid ownerId)
    {
        if (await db.UploadFileTypes.AnyAsync(t => t.Id == FeedMediaFileTypeId)) return;

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id                 = FeedMediaFileTypeId,
            Name               = FeedMediaFileTypeName,
            Description        = "Photos and video attached to feed posts — JPEG, PNG, GIF, WebP, MP4, WebM, MOV",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 9,
            AllowAllExtensions = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();

        foreach (var ext in FeedMediaExtensions)
        {
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id                 = Guid.NewGuid(),
                UploadFileTypeId   = FeedMediaFileTypeId,
                Pattern            = ext,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = ownerId,
            });
        }
        await db.SaveChangesAsync();
    }
}
