using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The pictures on menus: an item's photo and a menu's logo (10/13/2026), and who may see them.
/// </summary>
/// <remarks>
/// <para><b>Anybody</b>, once the picture is on a published menu of an event that is on the public
/// site, because that menu is.</para>
///
/// <para><b>Somebody from a group that can use it</b>, before then, so the builder's preview shows the
/// picture a planner just added: a member of the group that owns the item (or the menu, for a logo),
/// or, for an item kept at a venue, a member of any group holding an event there.</para>
/// </remarks>
public static class EventMenuImages
{
    public static async Task<bool> MaySeeAsync(BenDataContext db, Guid uploadFileId, Guid? userId, CancellationToken ct)
    {
        var onShow = await db.EventMenus.AsNoTracking()
            .Where(m => m.PublishedUtc != null && HostedEventStates.OnThePublicSite.Contains(m.HostedEvent.LifecycleState))
            .AnyAsync(m => m.LogoUploadFileId == uploadFileId
                        || m.Sections.Any(s => s.Entries.Any(e => e.MenuLibraryItem.ImageUploadFileId == uploadFileId)), ct);
        if (onShow) return true;
        if (userId is not { } me || me == Guid.Empty) return false;

        var mine = db.OrganizationUserMemberships.Where(m => m.AppUserId == me && m.IsActive).Select(m => m.OrganizationId);

        var item = await db.MenuLibraryItems.AsNoTracking()
            .Where(i => i.ImageUploadFileId == uploadFileId)
            .AnyAsync(i => mine.Contains(i.OrganizationId)
                        || (i.Scope == MenuItemScope.Venue
                            && db.HostedEvents.Any(e => e.PlaceId == i.PlaceId && mine.Contains(e.OrganizationId))), ct);
        if (item) return true;

        return await db.EventMenus.AsNoTracking()
            .AnyAsync(m => m.LogoUploadFileId == uploadFileId && mine.Contains(m.HostedEvent.OrganizationId), ct);
    }

    /// <summary>The picture's bytes, for drawing into a PDF; null when it has gone.</summary>
    public static async Task<byte[]?> ReadAsync(BenDataContext db, IFileStorageService storage, Guid uploadFileId, CancellationToken ct)
    {
        var path = await db.UploadFiles.AsNoTracking().Where(f => f.Id == uploadFileId)
            .Select(f => f.StoragePath).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(path) || !storage.Exists(path)) return null;

        await using var stream = await storage.OpenReadAsync(path, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
