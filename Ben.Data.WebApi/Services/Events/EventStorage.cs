using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// How much an event is holding, and whether one more file fits (item 235 phase 11).
/// </summary>
/// <remarks>
/// <para><b>Everything attached to the event counts:</b> the host's files, the gallery, and the photos and
/// videos posted in the room. A guest's room photo is the guest's own file, but it is stored because of the
/// event and served by it, so it is the event's space it uses.</para>
///
/// <para><b>Checked before the bytes are kept, and said in words</b> — how much is left, and what to do
/// about it — rather than as a bare 413 on a phone in a dark corridor.</para>
/// </remarks>
public static class EventStorage
{
    public const int DefaultMegabytes = 2000;

    /// <summary>Bytes the event holds now.</summary>
    public static async Task<long> UsedBytesAsync(BenDataContext db, Guid hostedEventId, CancellationToken ct)
    {
        var files = await db.HostedEventFiles.Where(f => f.HostedEventId == hostedEventId)
            .SumAsync(f => (long?)f.UploadFile.FileSize, ct) ?? 0;
        var gallery = await db.HostedEventGalleryImages.Where(g => g.HostedEventId == hostedEventId)
            .SumAsync(g => (long?)g.UploadFile.FileSize, ct) ?? 0;
        var room = await db.OrgMessages
            .Where(m => m.HostedEventId == hostedEventId && m.ChannelType == OrgMessageChannel.EventRoom && m.MediaUploadFileId != null)
            .SumAsync(m => (long?)m.MediaUploadFile!.FileSize, ct) ?? 0;
        return files + gallery + room;
    }

    /// <summary>The event's allowance, from the site setting.</summary>
    public static async Task<long> CapBytesAsync(BenDataContext db, CancellationToken ct)
    {
        var raw = await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventStorageMegabytes, ct);
        var megabytes = int.TryParse(raw, out var mb) && mb > 0 ? mb : DefaultMegabytes;
        return megabytes * 1024L * 1024L;
    }

    /// <summary>Why a file of this size does not fit, or null when it does.</summary>
    public static async Task<string?> WhyItDoesNotFitAsync(BenDataContext db, Guid hostedEventId, long incomingBytes, CancellationToken ct)
    {
        var cap = await CapBytesAsync(db, ct);
        var used = await UsedBytesAsync(db, hostedEventId, ct);
        if (used + incomingBytes <= cap) return null;

        var left = Math.Max(0, cap - used);
        return $"This event has used its {Megabytes(cap)} of space for files and photos, with {Megabytes(left)} left — "
             + "not enough for this one. The organizers can make room by removing files they no longer need.";
    }

    private static string Megabytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
        : $"{bytes / (1024.0 * 1024):N0} MB";
}
