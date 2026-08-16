namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 144 — a pure C# registry tracking which owner
/// currently holds each live blob: URL. Doesn't revoke anything itself (callers still call
/// <see cref="FfmpegService.RevokePreviewUrlAsync"/> or the equivalent) — its whole job is
/// catching the two multi-owner mistakes that actually produced live blob 404s this session
/// (see README-phase-144.md for the concrete bugs each of these would have caught):
///
/// <list type="bullet">
/// <item><b>Double-revoke</b>: revoking a URL that's already been revoked (or was never tracked)
/// — usually means two owners both thought they were responsible for the same string.</item>
/// <item><b>Revoke-while-attached</b>: revoking a URL a DIFFERENT owner has since taken over,
/// without going through <see cref="Transfer"/> first — the exact shape of the
/// <c>OnClipSelectedAsync</c> bug this phase fixed directly (VideoPreview revoked its own
/// previous URL as a side effect of loading a new one, while VideoEditor's own field still
/// pointed at that same, now-dead string).</item>
/// </list>
/// </summary>
public sealed class BlobUrlLifecycle
{
    private readonly ErrorLogService _errorLog;
    private readonly Dictionary<string, string> _ownerByUrl = [];

    public BlobUrlLifecycle(ErrorLogService errorLog) => _errorLog = errorLog;

    public int TrackedCount => _ownerByUrl.Count;

    /// <summary>Record a newly created URL under <paramref name="owner"/>. Overwrites silently if
    /// somehow already tracked (a fresh <c>createPreviewUrl</c> call always returns a brand-new
    /// blob: URL from the browser, so a collision here would itself be a browser-level anomaly,
    /// not a bug in this registry's callers).</summary>
    public void Created(string url, string owner) => _ownerByUrl[url] = owner;

    /// <summary>
    /// Record that <paramref name="url"/> is about to be revoked. Call this BEFORE actually
    /// revoking — logs a diagnostic (does not throw) when the URL isn't tracked at all (a
    /// double-revoke, or a revoke of something this registry never saw created) or when
    /// <paramref name="expectedOwner"/> doesn't match who's actually holding it (a revoke racing
    /// a transfer that already happened).
    /// </summary>
    public void Revoking(string url, string expectedOwner)
    {
        if (!_ownerByUrl.TryGetValue(url, out var actualOwner))
        {
            _errorLog.Log("BlobUrlLifecycle",
                $"'{expectedOwner}' revoking an untracked URL (double-revoke, or never registered): {url}");
            return;
        }

        if (actualOwner != expectedOwner)
        {
            _errorLog.Log("BlobUrlLifecycle",
                $"'{expectedOwner}' revoking a URL now owned by '{actualOwner}' — revoke-while-attached: {url}");
        }

        _ownerByUrl.Remove(url);
    }

    /// <summary>
    /// Record that ownership of <paramref name="url"/> moved to <paramref name="newOwner"/>
    /// without revoking it — the fix for the exact bug <see cref="Revoking"/>'s second check
    /// exists to catch: an owner handing a URL off (or discovering another component now holds
    /// the string it used to) must call this instead of silently letting its own field go stale.
    /// </summary>
    public void Transfer(string url, string newOwner)
    {
        if (!_ownerByUrl.ContainsKey(url))
        {
            _errorLog.Log("BlobUrlLifecycle", $"transfer of untracked URL to '{newOwner}': {url}");
        }
        _ownerByUrl[url] = newOwner;
    }

    /// <summary>True if <paramref name="url"/> is currently tracked as live (by any owner).</summary>
    public bool IsLive(string url) => _ownerByUrl.ContainsKey(url);
}
