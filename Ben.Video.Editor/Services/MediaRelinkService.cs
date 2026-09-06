using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Fetches a project's missing media again from the server it came from.
/// </summary>
/// <remarks>
/// <para>The half of portability that does the work. A clip records which server file its media
/// came from (<see cref="TrackItem.SourceFileId"/>); this uses that to put the file back into this
/// browser's storage under the id the restore looks for, so a project opened on a second machine
/// comes back whole instead of showing every clip as missing (2026-09-05 audit, F14).</para>
///
/// <para>The library cache is consulted first. The Server tab already stores a downloaded file
/// under the library file's own id, so a person who has used that file before in this browser gets
/// it back with no network at all.</para>
///
/// <para><b>A file that does not match is not used.</b> The recorded size and hash are checked
/// against what came back, and a clip whose media has been replaced on the server stays missing
/// rather than being quietly relinked to different footage. Every other failure also leaves the
/// clip exactly as it was — this only ever improves a project's state, which is what lets it run
/// unattended.</para>
/// </remarks>
public sealed class MediaRelinkService(
    OPFSService opfs,
    SourceMounter mounter,
    FfmpegService ffmpeg,
    ErrorLogService errorLog,
    IMediaLibraryProvider? library = null,
    VideoAssetCatalogService? assets = null)
{
    /// <summary>What one run managed.</summary>
    /// <param name="Restored">Clips whose media is back.</param>
    /// <param name="Mismatched">Clips whose file came back different, so it was not used.</param>
    /// <param name="Failed">Clips the fetch could not complete for.</param>
    public sealed record Outcome(int Restored, int Mismatched, int Failed)
    {
        public int Attempted => Restored + Mismatched + Failed;
    }

    /// <summary>Whether re-fetching is possible at all on this host.</summary>
    public bool IsAvailable => library is not null;

    /// <summary>
    /// The items that could be fetched again, out of everything currently missing.
    /// </summary>
    /// <remarks>
    /// A clip imported straight off somebody's disk has no source file id and cannot appear here —
    /// nothing can re-fetch a file that only ever existed on one machine, and offering to would be
    /// a promise this cannot keep.
    /// </remarks>
    public static IReadOnlyList<MediaRelinkCandidate> Candidates(IEnumerable<TrackItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .Where(i => i.IsMediaMissing)
            .Where(i => i.SourceFileId is not null && i.OpfsExt is not null)
            .Select(i => new MediaRelinkCandidate(
                i.Id, i.SourceFileId!.Value, i.OpfsExt!, i.SourceFileSize))
            .ToList();
    }

    /// <summary>
    /// Fetches each item's media and mounts it, leaving anything it cannot do untouched.
    /// </summary>
    /// <param name="items">The missing items to try, as returned by <see cref="Candidates"/>.</param>
    public async Task<Outcome> RelinkAsync(
        IReadOnlyCollection<TrackItem> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (library is null) return new Outcome(0, 0, 0);

        int restored = 0, mismatched = 0, failed = 0;

        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;
            if (item.SourceFileId is not { } fileId || item.OpfsExt is not { } ext) continue;

            try
            {
                var result = await RelinkOneAsync(item, fileId, ext, ct);
                switch (result)
                {
                    case Verdict.Restored:   restored++;   break;
                    case Verdict.Mismatched: mismatched++; break;
                    default:                 failed++;     break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errorLog.Log($"MediaRelinkService({item.Name})", ex);
            }
        }

        return new Outcome(restored, mismatched, failed);
    }

    private enum Verdict { Restored, Mismatched, Failed }

    private async Task<Verdict> RelinkOneAsync(
        TrackItem item, Guid fileId, string ext, CancellationToken ct)
    {
        // Already in the library cache from an earlier session in this browser — no network.
        if (!await opfs.ExistsAsync(fileId, ext))
        {
            if (!await FetchIntoCacheAsync(fileId, ext, ct)) return Verdict.Failed;
        }

        // What came back has to be the file the project was saved against. A server file replaced
        // since then would otherwise be relinked in silence, and editing against different footage
        // is worse than a clip that still says it is missing.
        var print = await opfs.FingerprintAsync(fileId, ext);
        var verdict = MediaFingerprint.Compare(
            item.SourceFileSize, item.SourceContentHash, print?.Size, print?.Hash);

        if (!MediaFingerprint.MayUse(verdict))
        {
            errorLog.Log($"MediaRelinkService({item.Name})",
                "The file on the server is not the one this clip was saved against, so it was left "
                + "missing rather than relinked to different footage.");
            return Verdict.Mismatched;
        }

        // Its own copy, under its own id, exactly as every other placed clip has — the restore
        // looks under the clip id first, and a clip sharing the cache entry would lose its media
        // the moment the cache was swept.
        var cached = await opfs.ReadAsJSFileAsync(fileId, ext);
        if (cached is null) return Verdict.Failed;

        await opfs.WriteAsync(item.Id, ext, cached);

        var memFsName = await mounter.MountAsync(item.Id, ext);
        if (memFsName is null)
        {
            var jsFile = await opfs.ReadAsJSFileAsync(item.Id, ext);
            if (jsFile is null) return Verdict.Failed;
            memFsName = $"{item.Id}{ext}";
            await ffmpeg.WriteFileAsync(memFsName, jsFile);
        }

        switch (item)
        {
            case VideoClip v: v.MemFsName = memFsName; break;
            case AudioClip a: a.MemFsName = memFsName; break;
            case ImageClip i: i.MemFsName = memFsName; break;
            default: return Verdict.Failed;
        }

        item.IsMediaMissing = false;
        return Verdict.Restored;
    }

    /// <summary>
    /// Puts back any clip art whose asset file is not on this machine.
    /// </summary>
    /// <remarks>
    /// <para>Clip art had the same problem as footage and none of the repair. Its
    /// <c>AssetSource</c> is documented as the key to re-downloading the file, and nothing ever
    /// used it: the restore walked video, audio and image clips only. At export the layer was left
    /// out, in the preview it drew nothing, and the timeline chip looked entirely normal because
    /// clip art's <c>IsMediaMissing</c> was never set either (2026-09-05 audit, callouts-14).</para>
    ///
    /// <para>Two outcomes, and the second one matters as much as the first: art that can be
    /// fetched again is fetched, and art that cannot is <b>marked missing</b>, so the chip carries
    /// the warning it always had a place for rather than the layer quietly vanishing from the
    /// finished video.</para>
    /// </remarks>
    public async Task<int> RestoreClipArtAsync(
        IReadOnlyCollection<ClipArtClip> art, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(art);
        if (art.Count == 0) return 0;

        var catalogue = assets is null
            ? []
            : await SafeCatalogueAsync(ct);

        var restored = 0;

        foreach (var clip in art)
        {
            if (ct.IsCancellationRequested) break;
            if (!Guid.TryParse(clip.AssetId, out var assetId)) continue;

            var ext = "." + FormatExtension(clip.AssetFormat);

            if (await opfs.ExistsAsync(assetId, ext))
            {
                clip.IsMediaMissing = false;
                continue;
            }

            var item = catalogue.FirstOrDefault(
                i => string.Equals(i.Id, clip.AssetId, StringComparison.OrdinalIgnoreCase)
                     && i.Source == clip.AssetSource);

            if (item is null || assets is null)
            {
                // Nothing to fetch it from — say so on the chip rather than letting the layer
                // disappear from the render with the timeline still looking fine.
                clip.IsMediaMissing = true;
                continue;
            }

            try
            {
                await assets.EnsureLocalAsync(item, ct: ct);
                var here = await opfs.ExistsAsync(assetId, ext);
                clip.IsMediaMissing = !here;
                if (here) restored++;
            }
            catch (Exception ex)
            {
                clip.IsMediaMissing = true;
                errorLog.Log($"MediaRelinkService.ClipArt({clip.Name})", ex);
            }
        }

        return restored;
    }

    private async Task<IReadOnlyList<VideoAssetCatalogItem>> SafeCatalogueAsync(CancellationToken ct)
    {
        // A catalogue that will not load is a reason to mark art missing, not a reason to fail the
        // whole restore — everything else about the project is still worth having.
        try { return await assets!.GetAllAssetsAsync(ct); }
        catch (Exception ex)
        {
            errorLog.Log("MediaRelinkService.Catalogue", ex);
            return [];
        }
    }

    /// <summary>Where clip art of this format is filed, matching the asset providers.</summary>
    private static string FormatExtension(VideoAssetFormat format) => format switch
    {
        VideoAssetFormat.Svg    => "svg",
        VideoAssetFormat.Avif   => "avif",
        VideoAssetFormat.WebP   => "webp",
        VideoAssetFormat.Gif    => "gif",
        VideoAssetFormat.Lottie => "json",
        _                       => "png",
    };

    /// <summary>
    /// Puts the server's copy into the library cache, by whichever route the host offers.
    /// </summary>
    /// <remarks>
    /// The browser fetches it itself when the host can say where from; under Blazor Server the
    /// alternative pulls the file through the circuit (2026-09-05 audit, site-2). The byte path is
    /// kept as the fallback because a host may have no URL to give.
    /// </remarks>
    private async Task<bool> FetchIntoCacheAsync(Guid fileId, string ext, CancellationToken ct)
    {
        var directUrl = await library!.GetDownloadUrlAsync(fileId, ct);
        if (directUrl is not null)
        {
            var written = await opfs.DownloadToClipAsync(directUrl, fileId, ext);
            if (written >= 0) return true;

            errorLog.Log("MediaRelinkService",
                "Direct download failed; falling back to fetching through the host.");
        }

        var bytes = await library.DownloadFileAsync(fileId, ct);
        await opfs.WriteFromBytesAsync(fileId, ext, bytes);
        return true;
    }
}
