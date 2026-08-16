namespace Ben.Video.Editor.Services;

/// <summary>
/// Pure C# ledger of the main ffmpeg instance's expected MEMFS residency — item #59-#65
/// flakiness investigation, phase 141. This is the "heap readout" <c>DESIGN-item38-long-form-
/// memory.md</c> specified (a diagnostics panel showing current WASM-heap usage) but never
/// built: the worker's real <c>HEAPU8</c> buffer lives inside the ffmpeg.wasm Worker thread and
/// is not reachable from the main thread or from .NET at all, so an *exact* live readout isn't
/// possible without instrumenting the worker itself. This ledger is the practical alternative —
/// every write/delete/rename this app performs against MEMFS updates a running total here, so a
/// large or ever-growing figure is a real, visible signal of the exact class of silent pressure
/// the exploration found (animated-overlay PNG bursts, orphaned fallback source copies,
/// uncapped preview segments) even though it can drift from the true heap size over time (see
/// <see cref="Track"/>'s note on unknown-size entries).
///
/// <para>Deliberately backend-agnostic, matching <see cref="PreviewSegmentCache"/>'s own shape —
/// no JS interop, no ffmpeg-specific types, fully unit-testable in isolation. Registered Scoped
/// alongside <see cref="FfmpegService"/>, which is the only writer today.</para>
/// </summary>
public sealed class MemFsLedger
{
    private readonly Dictionary<string, MemFsLedgerEntry> _entries = [];

    /// <summary>Every currently-tracked entry, for diagnostics UI / export.</summary>
    public IReadOnlyCollection<MemFsLedgerEntry> Entries => _entries.Values;

    /// <summary>Sum of every tracked entry's <see cref="MemFsLedgerEntry.Bytes"/>. Entries whose
    /// size wasn't known at write time (see <see cref="Track"/>) contribute 0 here even though
    /// they occupy real MEMFS space — this total is a lower bound, not an exact figure.</summary>
    public long TotalBytes => _entries.Values.Sum(e => e.Bytes);

    /// <summary>Number of currently-tracked entries.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Records (or updates, if <paramref name="name"/> is already tracked — e.g. a re-write of
    /// the same temp filename) an entry. <paramref name="bytes"/> is <c>0</c> for call sites that
    /// only have an opaque JS <c>File</c>/<c>Blob</c> reference and never read its <c>.size</c>
    /// (<see cref="FfmpegService.WriteFileAsync"/>) — the entry still exists (so <see cref="Count"/>
    /// and <see cref="Entries"/> stay accurate for "how many things are resident"), it just can't
    /// contribute a real number to <see cref="TotalBytes"/>.
    /// </summary>
    public void Track(string name, long bytes, string category = "unknown")
        => _entries[name] = new MemFsLedgerEntry(name, bytes, category);

    /// <summary>Stops tracking <paramref name="name"/> (the caller is responsible for the actual
    /// MEMFS delete — this is bookkeeping only, matching <see cref="PreviewSegmentCache"/>'s own
    /// division of responsibility). No-op if not tracked.</summary>
    public void Untrack(string name) => _entries.Remove(name);

    /// <summary>Renames a tracked entry in place, preserving its size/category — for
    /// <see cref="FfmpegService.RenameFileAsync"/>, a genuine in-place MEMFS rename with no size
    /// change. No-op if <paramref name="from"/> isn't tracked (matches <c>RenameFileAsync</c>'s
    /// own no-op-when-module-unavailable contract rather than throwing).</summary>
    public void Rename(string from, string to)
    {
        if (!_entries.Remove(from, out var entry)) return;
        _entries[to] = entry with { Name = to };
    }

    /// <summary>Drops every tracked entry — call whenever MEMFS itself is known to have been
    /// wiped (a core reload), mirroring <see cref="PreviewSegmentCache.Clear"/>'s own trigger.
    /// Deliberately named the same as that method's for the same reason: keeping every "MEMFS
    /// just disappeared out from under us" reset call site recognizable at a glance.</summary>
    public void Clear() => _entries.Clear();
}

/// <summary>One <see cref="MemFsLedger"/> entry.</summary>
public sealed record MemFsLedgerEntry(string Name, long Bytes, string Category);
