namespace Ben.Video.Editor.Services;

/// <summary>
/// Owns WORKERFS mount lifecycle for source clips on the main ffmpeg instance (item #38 phase B —
/// see <c>DESIGN-item38-long-form-memory.md</c> §3.1 and §"Phase B"). Before this phase, every
/// source clip's bytes were copied into main-instance MEMFS regardless of import path, permanently
/// costing full WASM heap for the clip's whole lifetime in the session — even though
/// <c>mountWorkerFs</c> (zero-copy, read-only) already existed in <c>ffmpegInterop.js</c> and was
/// already proven in production by the background render worker
/// (<see cref="RenderWorkerService.MountSourceAsync"/>); it simply had no caller on the main
/// instance.
///
/// <para><b>Semantics contract</b> (§3.1): once a clip is mounted, its <c>MemFsName</c> is a path
/// the main ffmpeg instance can read — not necessarily a MEMFS-owned, independently-deletable file.
/// Deleting a mounted source must go through <see cref="UnmountAsync"/>, never a bare
/// <see cref="FfmpegService.DeleteFileAsync"/>. (A repo-wide audit at phase-B time found zero
/// existing call sites that delete a persisted clip's own <c>MemFsName</c> — every
/// <c>DeleteFileAsync</c> call in the codebase targets an ephemeral temp/export/segment file, never
/// a clip's own source — so this contract has nothing to retrofit today, only to hold going
/// forward.)</para>
///
/// <para>Registered Scoped, matching both <see cref="FfmpegService"/> and <see cref="OPFSService"/>.</para>
/// </summary>
public sealed class SourceMounter
{
    private readonly FfmpegService _ffmpeg;
    private readonly OPFSService   _opfs;

    // Tracks every currently-mounted clip (id -> its OPFS extension) so a core reload — which
    // silently drops every WORKERFS mount along with the rest of the virtual filesystem — can
    // remount them all via RemountAllAsync. This is the "sneakiest bug class" the design doc
    // calls out for this phase.
    private readonly Dictionary<Guid, string> _mountedExt = [];

    public SourceMounter(FfmpegService ffmpeg, OPFSService opfs)
    {
        _ffmpeg = ffmpeg;
        _opfs   = opfs;
    }

    /// <summary>
    /// Mounts <paramref name="clipId"/>'s OPFS copy into the main ffmpeg instance and returns the
    /// resulting path, or <c>null</c> when there's nothing to mount (OPFS unavailable, or this
    /// clip has no OPFS copy — e.g. it predates item #38 phase A). Callers fall back to a normal
    /// MEMFS write in that case, exactly as every import path did before this phase existed.
    /// </summary>
    public async Task<string?> MountAsync(Guid clipId, string ext)
    {
        // No IsAvailable pre-check here on purpose: it defaults to false until
        // OPFSService.EnsureInitAsync has run at least once, and ReadAsJSFileAsync already calls
        // that itself before checking availability — a pre-check here would risk a false "OPFS
        // unavailable" bail-out on the very first OPFS call of a session, before anything else
        // has had a chance to initialize it.
        var fileRef = await _opfs.ReadAsJSFileAsync(clipId, ext);
        if (fileRef is null) return null;

        var path = await _ffmpeg.MountWorkerFsAsync(fileRef, MountDir(clipId));
        if (path is not null) _mountedExt[clipId] = ext;
        return path;
    }

    /// <summary>Unmounts a clip's source, if it was mounted (no-op otherwise). Always call this —
    /// rather than deleting <c>clip.MemFsName</c> directly — whenever a clip's source is being
    /// replaced (relink) or the clip is removed from the project.</summary>
    public async Task UnmountAsync(Guid clipId)
    {
        if (!_mountedExt.Remove(clipId)) return;
        await _ffmpeg.UnmountWorkerFsAsync(MountDir(clipId));
    }

    /// <summary>
    /// Re-mounts every currently-tracked source. WORKERFS mounts do not survive
    /// <see cref="FfmpegService.TerminateAsync"/> + reload (the core restart clears the whole
    /// virtual filesystem, mounts included) — callers must invoke this immediately after any such
    /// reload, alongside clearing <c>PreviewSegmentCache</c> (its cached segment filenames stop
    /// existing at the exact same moment, for the exact same reason).
    /// </summary>
    /// <returns>clipId → new mounted path, for callers to update each clip's <c>MemFsName</c>.
    /// A clip whose OPFS copy has since disappeared is dropped from tracking and omitted here —
    /// callers should treat that clip as needing a normal re-import.</returns>
    public async Task<IReadOnlyDictionary<Guid, string>> RemountAllAsync()
    {
        var result = new Dictionary<Guid, string>();
        foreach (var (clipId, ext) in _mountedExt.ToList())
        {
            var fileRef = await _opfs.ReadAsJSFileAsync(clipId, ext);
            // Only drop tracking when the OPFS copy is confirmed gone — that's the one genuinely
            // permanent case. A failed MountWorkerFsAsync (phase 143 fix) does NOT mean the same
            // thing: before this fix, any mount failure — including a stale WORKERFS directory
            // left over from a fake-recovery reload that never actually terminated the worker —
            // silently and permanently dropped a perfectly fine clip from remount tracking. Now it
            // just stays tracked and gets another chance on the next RemountAllAsync (e.g. the next
            // Reset), instead of being abandoned.
            if (fileRef is null) { _mountedExt.Remove(clipId); continue; }

            var path = await _ffmpeg.MountWorkerFsAsync(fileRef, MountDir(clipId));
            if (path is not null) result[clipId] = path;
        }
        return result;
    }

    // Flat, not nested: ffmpeg.wasm's createDir wraps Emscripten's FS.mkdir, which is
    // non-recursive and requires the parent directory to already exist — a "/sources/{id}" path
    // fails with ErrnoError (parent "/sources" is never created). RenderWorkerService.MountSourceAsync
    // hit this exact constraint first and settled on the same flat "/src_{id}" shape.
    private static string MountDir(Guid clipId) => $"/src_{clipId:N}";
}
