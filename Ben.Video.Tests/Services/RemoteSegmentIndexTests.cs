using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 160. The governing rule these tests pin down: <b>a stale mapping is worse than a
/// missing one</b>. A wrong remote id would make a later concat splice the wrong footage; a
/// missing one only costs a wasm fallback. So anything that could invalidate the sidecar's segment
/// store must clear the whole map rather than try to salvage part of it.
/// </summary>
public sealed class RemoteSegmentIndexTests
{
    [Fact]
    public void Register_ThenLookup_ReturnsRemoteId()
    {
        var index = new RemoteSegmentIndex();
        var remote = Guid.NewGuid();

        index.Register("bgseg_a.mp4", remote);

        Assert.Equal(remote, index.TryGetRemoteId("bgseg_a.mp4"));
    }

    [Fact]
    public void TryGetRemoteId_Unknown_ReturnsNull() =>
        Assert.Null(new RemoteSegmentIndex().TryGetRemoteId("never-registered.mp4"));

    [Theory]
    [InlineData("")]
    public void Register_EmptyName_IsIgnored(string name)
    {
        var index = new RemoteSegmentIndex();
        index.Register(name, Guid.NewGuid());
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Register_EmptyGuid_IsIgnored()
    {
        // Guid.Empty is what a missing/absent RetainedSegmentId deserializes to if anyone ever
        // treats the nullable as non-nullable — recording it would produce a mapping that points
        // at nothing.
        var index = new RemoteSegmentIndex();
        index.Register("bgseg_a.mp4", Guid.Empty);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Register_SameNameTwice_KeepsTheNewestId()
    {
        // A re-render of the same region produces a new remote copy; the old id is dead.
        var index = new RemoteSegmentIndex();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        index.Register("bgseg_a.mp4", older);
        index.Register("bgseg_a.mp4", newer);

        Assert.Equal(newer, index.TryGetRemoteId("bgseg_a.mp4"));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void Remove_ReturnsIdSoCallerCanIssueRemoteDelete()
    {
        var index = new RemoteSegmentIndex();
        var remote = Guid.NewGuid();
        index.Register("bgseg_a.mp4", remote);

        Assert.Equal(remote, index.Remove("bgseg_a.mp4"));
        Assert.Null(index.TryGetRemoteId("bgseg_a.mp4"));
        Assert.Null(index.Remove("bgseg_a.mp4")); // idempotent
    }

    [Fact]
    public void TryGetAll_AllMapped_ReturnsIdsInRequestedOrder()
    {
        // Order is the whole contract for a concat — the ids must come back in the order asked
        // for, not the order registered.
        var index = new RemoteSegmentIndex();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        index.Register("c.mp4", c);
        index.Register("a.mp4", a);
        index.Register("b.mp4", b);

        Assert.Equal([a, b, c], index.TryGetAll(["a.mp4", "b.mp4", "c.mp4"]));
    }

    [Fact]
    public void TryGetAll_AnyUnmapped_ReturnsNullNotAPartialSet()
    {
        // All-or-nothing: handing back a partial list would invite a caller to concat a subset and
        // silently drop footage.
        var index = new RemoteSegmentIndex();
        index.Register("a.mp4", Guid.NewGuid());

        Assert.Null(index.TryGetAll(["a.mp4", "missing.mp4"]));
    }

    [Fact]
    public void SyncInstance_SameInstance_KeepsMappings()
    {
        var index = new RemoteSegmentIndex();
        var instance = Guid.NewGuid();
        index.SyncInstance(instance);
        index.Register("a.mp4", Guid.NewGuid());

        index.SyncInstance(instance);

        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void SyncInstance_DifferentInstance_ClearsEverything()
    {
        // A restarted sidecar keeps its port and token but has an EMPTY segment store and a fresh
        // instance id. Without this the browser would keep handing it ids for segments that are
        // gone — the exact stale-mapping failure this class exists to prevent.
        var index = new RemoteSegmentIndex();
        index.SyncInstance(Guid.NewGuid());
        index.Register("a.mp4", Guid.NewGuid());

        index.SyncInstance(Guid.NewGuid());

        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Clear_DropsMappingsAndForgetsInstance()
    {
        var index = new RemoteSegmentIndex();
        var instance = Guid.NewGuid();
        index.SyncInstance(instance);
        index.Register("a.mp4", Guid.NewGuid());

        index.Clear();
        Assert.Equal(0, index.Count);

        // Re-syncing the SAME instance after a Clear must not be treated as "unchanged, keep
        // going" — the connection dropped, so this is a fresh start that happens to reach the same
        // process.
        index.SyncInstance(instance);
        index.Register("b.mp4", Guid.NewGuid());
        Assert.Equal(1, index.Count);
    }
}
