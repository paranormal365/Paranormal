using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds a built-in "Logo" upload file type restricted to common image formats.
/// Idempotent — safe to run on every startup.
/// </summary>
internal static class UploadFileTypeSeeder
{
    internal const string LogoFileTypeName = "Logo";

    private static readonly string[] LogoExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        var owner = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null) return; // SuperAdmin not yet seeded — skip

        await using var db = await dbFactory.CreateDbContextAsync();

        // ── Ensure "Logo" file type exists ────────────────────────────────────
        var logoType = await db.UploadFileTypes
            .FirstOrDefaultAsync(t => t.Name == LogoFileTypeName);

        if (logoType is null)
        {
            logoType = new UploadFileType
            {
                Id                 = Guid.NewGuid(),
                Name               = LogoFileTypeName,
                Description        = "Organization logo images — JPEG, PNG, GIF, WebP, SVG",
                IsActive           = true,
                IsPublic           = true,
                SortOrder          = 1,
                AllowAllExtensions = false,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = owner.Id
            };
            db.UploadFileTypes.Add(logoType);
            await db.SaveChangesAsync();
        }

        // ── Ensure all image extension patterns exist ─────────────────────────
        var existingPatterns = (await db.UploadFileTypeExtensions
            .Where(e => e.UploadFileTypeId == logoType.Id)
            .Select(e => e.Pattern)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var ext in LogoExtensions)
        {
            if (existingPatterns.Contains(ext)) continue;

            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id               = Guid.NewGuid(),
                UploadFileTypeId = logoType.Id,
                Pattern          = ext,
                DateCreated      = DateTime.UtcNow,
                CreatedByAppUserId = owner.Id
            });
            added = true;
        }

        if (added)
            await db.SaveChangesAsync();
    }
}
