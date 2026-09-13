using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// How long an event's files are kept, and what goes when the time is up (item 235 phase 12).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"a data retention policy. I am not sure how long after an event we should retain
/// links to collected files like images or media."</i> Then: <i>"90 days is fine."</i></para>
///
/// <para><b>What goes:</b> the event's own files, its gallery pictures, and the room's links to the photos
/// posted in it. <b>What stays:</b> the event and everything that records what happened — bookings, reviews,
/// the programme — and every guest's own photo, which lives in their library and was always theirs. A photo
/// a guest sent to the organizers stays shared with them, because sharing was the guest's choice.</para>
///
/// <para><b>Nobody loses anything without being told twice.</b> The organizer is written to a month and a
/// week before, with a link to pick what to keep and take it away as one zip.</para>
/// </remarks>
public static class EventRetention
{
    public const int DefaultDays = 90;

    /// <summary>The events the rule applies to: ones that happened, or were called off. Never a draft or a live one.</summary>
    public static readonly HostedEventLifecycleState[] Applies =
    [
        HostedEventLifecycleState.Ended,
        HostedEventLifecycleState.Archived,
        HostedEventLifecycleState.Cancelled,
        HostedEventLifecycleState.VenueWithdrawn,
        HostedEventLifecycleState.Removed,
    ];

    public static async Task<int> DaysAsync(BenDataContext db, CancellationToken ct)
    {
        var raw = await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventRetentionDays, ct);
        return int.TryParse(raw, out var days) && days > 0 ? days : DefaultDays;
    }

    /// <summary>The day the files go: the given number of days after the last date.</summary>
    public static DateTime ClearsOn(HostedEvent ev, int days) => ev.EndsOn.Date.AddDays(days + 1);

    /// <summary>What would go, counted.</summary>
    public sealed record Holding(int Files, int Pictures, int Photos)
    {
        public bool IsEmpty => Files == 0 && Pictures == 0 && Photos == 0;
    }

    public static async Task<Holding> HoldingAsync(BenDataContext db, Guid eventId, CancellationToken ct)
        => new(
            await db.HostedEventFiles.CountAsync(f => f.HostedEventId == eventId, ct),
            await db.HostedEventGalleryImages.CountAsync(g => g.HostedEventId == eventId, ct),
            await db.OrgMessages.CountAsync(m => m.HostedEventId == eventId && m.MediaUploadFileId != null, ct));

    /// <summary>
    /// Removes the event's files and pictures and unlinks the room's photos, then stamps the event.
    /// </summary>
    /// <returns>The storage paths whose bytes may now be deleted; the caller deletes them after this saves.</returns>
    public static async Task<IReadOnlyList<string>> ClearAsync(
        BenDataContext db, HostedEvent ev, DateTime now, CancellationToken ct)
    {
        var fileRows = await db.HostedEventFiles.Include(f => f.UploadFile)
            .Where(f => f.HostedEventId == ev.Id).ToListAsync(ct);
        var pictureRows = await db.HostedEventGalleryImages.Include(g => g.UploadFile)
            .Where(g => g.HostedEventId == ev.Id).ToListAsync(ct);

        var uploads = fileRows.Select(f => (f.UploadFileId, f.UploadFile.StoragePath))
            .Concat(pictureRows.Select(g => (g.UploadFileId, g.UploadFile.StoragePath)))
            .ToList();

        db.HostedEventFiles.RemoveRange(fileRows);
        db.HostedEventGalleryImages.RemoveRange(pictureRows);

        // The guests' photos stay theirs; only the room's link to them goes.
        await db.OrgMessages
            .Where(m => m.HostedEventId == ev.Id && m.MediaUploadFileId != null)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.MediaUploadFileId, (Guid?)null), ct);

        ev.MediaClearedUtc = now;
        ev.DateUpdated = now;
        await db.SaveChangesAsync(ct);

        // The rows first, then the bytes, and only where nothing else still points at the file — a
        // copied event's cover or an advert's picture may.
        var paths = new List<string>();
        foreach (var (uploadId, path) in uploads)
        {
            if (await Admin.UploadFileRows.TryDeleteAsync(db, uploadId, ct) && path is { Length: > 0 })
                paths.Add(path);
        }

        return paths;
    }
}
