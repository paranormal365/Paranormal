namespace Ben.Video.Editor.Services;

/// <summary>
/// Maps a local MEMFS segment name to the sidecar's own retained copy of the same segment — item
/// #70 phase 160's client half of dual residency.
///
/// <para>Pure and synchronous on purpose: it holds no connection and does no I/O, so it can be
/// unit-tested exhaustively and can't itself be a source of the main-thread work this arc is
/// removing. Phases 161/162 read it to decide whether a concat/assemble can run natively (every
/// input has a remote twin) or must fall back to wasm.</para>
///
/// <para><b>Correctness rule: a stale entry is worse than no entry.</b> A wrong remote id would
/// make a later concat silently splice the wrong footage, whereas a missing one only costs a wasm
/// fallback. Hence <see cref="Clear"/> on connection loss and on an instance-id change — a
/// restarted sidecar has an empty segment store but the browser's map would still be full of ids
/// that now refer to nothing (or, worse, to a different session's segments).</para>
/// </summary>
public sealed class RemoteSegmentIndex
{
    private readonly Dictionary<string, Guid> _remoteBySegmentName = [];
    private Guid? _instanceId;

    public int Count => _remoteBySegmentName.Count;

    /// <summary>Records that <paramref name="segmentName"/> also exists on the sidecar as
    /// <paramref name="remoteId"/>.</summary>
    public void Register(string segmentName, Guid remoteId)
    {
        if (string.IsNullOrEmpty(segmentName) || remoteId == Guid.Empty) return;
        _remoteBySegmentName[segmentName] = remoteId;
    }

    public Guid? TryGetRemoteId(string segmentName) =>
        _remoteBySegmentName.TryGetValue(segmentName, out var id) ? id : null;

    /// <summary>Drops one mapping and returns the id that was there, so the caller can fire the
    /// matching remote DELETE without a second lookup.</summary>
    public Guid? Remove(string segmentName)
    {
        if (!_remoteBySegmentName.Remove(segmentName, out var id)) return null;
        return id;
    }

    /// <summary>
    /// Every input's remote id, or null if <b>any</b> of them is unmapped.
    ///
    /// <para>All-or-nothing by design: a partial set is useless to a concat, which needs every
    /// input present to produce correct output. Returning nulls to be filtered would invite a
    /// caller to concat a subset and silently drop footage.</para>
    /// </summary>
    public IReadOnlyList<Guid>? TryGetAll(IEnumerable<string> segmentNames)
    {
        var result = new List<Guid>();
        foreach (var name in segmentNames)
        {
            if (TryGetRemoteId(name) is not { } id) return null;
            result.Add(id);
        }
        return result;
    }

    /// <summary>
    /// Records the connected sidecar's instance id, clearing every mapping if it differs from the
    /// last one seen. A restarted sidecar keeps the same port and token but gets a fresh instance
    /// id and an empty segment store — without this the browser would keep handing it ids for
    /// segments it no longer has.
    /// </summary>
    public void SyncInstance(Guid? instanceId)
    {
        if (_instanceId == instanceId) return;
        _instanceId = instanceId;
        _remoteBySegmentName.Clear();
    }

    /// <summary>Called on connection loss. Deliberately also forgets the instance id, so the next
    /// successful connection is always treated as fresh.</summary>
    public void Clear()
    {
        _remoteBySegmentName.Clear();
        _instanceId = null;
    }
}
