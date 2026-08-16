namespace Ben.Video.Editor.Services;

/// <summary>
/// Maps a <see cref="RenderSignatureBuilder"/> signature to the MEMFS filename of the already-encoded
/// preview segment for it — item #36 phase B. <see cref="VideoEditor"/>'s <c>PreviewTimelineAsync</c>
/// checks this before re-encoding a clip's trim/effects pass; a hit means that pass can be skipped
/// entirely and the cached segment reused in the concat list. Pure bookkeeping — actual MEMFS
/// read/write/delete stays in <see cref="FfmpegService"/>, called by the consumer.
/// </summary>
public sealed class PreviewSegmentCache
{
    private readonly Dictionary<string, string> _bySignature = [];

    public bool TryGet(string signature, out string memFsName) =>
        _bySignature.TryGetValue(signature, out memFsName!);

    public void Set(string signature, string memFsName) => _bySignature[signature] = memFsName;

    /// <summary>
    /// Returns the MEMFS filenames of every cached entry whose signature is NOT in
    /// <paramref name="liveSignatures"/> and removes them from the cache. The caller is
    /// responsible for actually deleting the returned files from MEMFS — this only stops
    /// tracking them. Call after every <see cref="ClipStore"/> resync so a clip's old trim/effects
    /// state never lingers past the edit that made it stale.
    /// </summary>
    public List<string> EvictOrphans(IReadOnlyCollection<string> liveSignatures)
    {
        var live = new HashSet<string>(liveSignatures);
        var orphanKeys = _bySignature.Keys.Where(k => !live.Contains(k)).ToList();

        var orphanNames = new List<string>(orphanKeys.Count);
        foreach (var key in orphanKeys)
        {
            orphanNames.Add(_bySignature[key]);
            _bySignature.Remove(key);
        }
        return orphanNames;
    }

    /// <summary>Drops every cached entry without returning filenames. Call if the ffmpeg core is
    /// ever reloaded independently of this cache's own scope (e.g. <see cref="FfmpegService.TerminateAsync"/>
    /// followed by a fresh <c>LoadAsync</c> within the same session) — that wipes MEMFS out from
    /// under the cache, so every cached filename is already gone and must not be reused.</summary>
    public void Clear() => _bySignature.Clear();
}
